using System.Reactive;
using System.Reactive.Linq;
using Autofac;
using Miningcore.Blockchain.Dero.Configuration;
using Miningcore.Blockchain.Dero.DaemonRequests;
using Miningcore.Blockchain.Dero.DaemonResponses;
using Miningcore.Blockchain.Dero.StratumRequests;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications.Messages;
using Miningcore.Rpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using NLog;
using static Miningcore.Util.ActionUtils;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain.Dero;

public class DeroJobManager : JobManagerBase<DeroJob>
{
    public DeroJobManager(
        IComponentContext ctx,
        IMasterClock clock,
        IMessageBus messageBus) :
        base(ctx, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);

        this.clock = clock;
    }

    private DaemonEndpointConfig[] daemonEndpoints;
    private RpcClient rpc;
    private RpcClient walletRpc;
    private readonly IMasterClock clock;
    private DeroNetworkType networkType;
    private DaemonEndpointConfig[] walletDaemonEndpoints;
    private DeroCoinTemplate coin;
    private DeroPoolConfigExtra extraPoolConfig;

    protected async Task<bool> UpdateJob(CancellationToken ct, string via = null, string json = null)
    {
        try
        {
            var response = string.IsNullOrEmpty(json) ? await GetBlockTemplateAsync(ct) : GetBlockTemplateFromJson(json);

            // may happen if daemon is currently not connected to peers
            if(response.Error != null)
            {
                logger.Warn(() => $"Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                return false;
            }

            var blockTemplate = response.Response;
            var job = currentJob;

            var isNewBlock = job == null || blockTemplate.Height != job.Height;
            var isNew = job == null || job.BlockTemplate.HashingBlob[..72] != blockTemplate.HashingBlob[..72];

            if(isNew)
            {
                job = new DeroJob(blockTemplate);
                currentJob = job;

                logger.Debug(() => $"V: {job.MVersion}, HD: {job.MHighDiff}, F: {job.MFinal}, PC: {job.MPastCount}");

                if(via != null)
                    logger.Debug(() => $"New mini block job {blockTemplate.Height} and {blockTemplate.Difficulty} [{via}]");
                else
                    logger.Debug(() => $"New mini block job {blockTemplate.Height} and {blockTemplate.Difficulty}");

            }

            if(isNewBlock)
            {
                messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                if(via != null)
                    logger.Info(() => $"Detected new block {blockTemplate.Height} [{via}]");
                else
                    logger.Info(() => $"Detected new block {blockTemplate.Height}");

                // update stats
                BlockchainStats.LastNetworkBlockTime = clock.Now;
                BlockchainStats.BlockHeight = blockTemplate.Height;
                BlockchainStats.NextNetworkTarget = "";
                BlockchainStats.NextNetworkBits = "";
            }

            return isNew;
        }

        catch(OperationCanceledException)
        {
            // ignored
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
        }

        return false;
    }


    private async Task<RpcResponse<GetBlockTemplateResponse>> GetBlockTemplateAsync(CancellationToken ct)
    {
        var request = new GetBlockTemplateRequest
        {
            WalletAddress = poolConfig.Address,
            Block = true,
            Miner = poolConfig.Id
        };

        return await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger, DeroCommands.GetBlockTemplate, ct, request);
    }

    private RpcResponse<GetBlockTemplateResponse> GetBlockTemplateFromJson(string json)
    {
        var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

        return new RpcResponse<GetBlockTemplateResponse>(result.ResultAs<GetBlockTemplateResponse>());
    }

    private async Task ShowDaemonSyncProgressAsync(CancellationToken ct)
    {
        var response = await rpc.ExecuteAsync<GetInfoResponse>(logger, DeroCommands.GetInfo, ct);
        var info = response.Response;

        if(info != null)
        {
            var lowestHeight = info.Height;

            var totalBlocks = info.TargetHeight;
            var percent = (double) lowestHeight / totalBlocks * 100;

            logger.Info(() => $"Daemon has downloaded {percent:0.00}% of blockchain from {info.OutgoingConnectionsCount} peers");
        }
    }

    private async Task UpdateNetworkStatsAsync(CancellationToken ct)
    {
        try
        {
            var response = await rpc.ExecuteAsync(logger, DeroCommands.GetInfo, ct);

            if(response.Error != null)
                logger.Warn(() => $"Error(s) refreshing network stats: {response.Error.Message} (Code {response.Error.Code})");

            if(response.Response != null)
            {
                var info = response.Response.ToObject<GetInfoResponse>();

                //BlockchainStats.NetworkHashrate = info.Target > 0 ? (double) info.Difficulty / info.Target : 0;
                BlockchainStats.NetworkHashrate = info.Difficulty;
                BlockchainStats.ConnectedPeers = info.OutgoingConnectionsCount + info.IncomingConnectionsCount;
                BlockchainStats.NetworkDifficulty = info.Difficulty;
            }
        }

        catch(Exception e)
        {
            logger.Error(e);
        }
    }

    private async Task<string> SubmitBlockAsync(Share share, string jobId, string blobHex)
    {
        var request = new SubmitBlockRequest
        {
            JobId = jobId,
            HashingBlob = blobHex,
        };

        var response = await rpc.ExecuteAsync<SubmitResponse>(logger, DeroCommands.SubmitBlock, CancellationToken.None, request);

        if(response.Error != null || response?.Response?.Status != "OK")
        {
            var error = response.Error?.Message ?? response.Response?.Status;

            logger.Warn(() => $"Block {share.BlockHeight} submission failed with: {error}");
            messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {error}"));
            return error;
        }

        share.IsBlockCandidate = true;

        if(!response.Response.mini)
        {
            share.BlockHash = response.Response?.BlockId;
            logger.Info(() => $"Block {share.BlockHeight} [{share.BlockHash[..6]}] submission done");
        }

        return null;
    }

    #region API-Surface

    public IObservable<Unit> Blocks { get; private set; }

    public DeroCoinTemplate Coin => coin;

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {

        extraPoolConfig = pc.Extra.SafeExtensionDataAs<DeroPoolConfigExtra>();

        Contract.RequiresNonNull(pc);
        Contract.RequiresNonNull(cc);

        logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<DeroJob>), pc);
        poolConfig = pc;
        clusterConfig = cc;
        coin = pc.Template.As<DeroCoinTemplate>();

        // extract standard daemon endpoints
        daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = DeroConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        if(cc.PaymentProcessing?.Enabled == true && pc.PaymentProcessing?.Enabled == true)
        {
            // extract wallet daemon endpoints
            walletDaemonEndpoints = pc.Daemons
                .Where(x => x.Category?.ToLower() == DeroConstants.WalletDaemonCategory)
                .Select(x =>
                {
                    if(string.IsNullOrEmpty(x.HttpPath))
                        x.HttpPath = DeroConstants.DaemonRpcLocation;

                    return x;
                })
                .ToArray();

            if(walletDaemonEndpoints.Length == 0)
                throw new PoolStartupException("Wallet-RPC daemon is not configured (Daemon configuration for dero-pools require an additional entry of category \'wallet' pointing to the wallet daemon)", pc.Id);
        }

        ConfigureDaemons();
    }

    public async Task<bool> ValidateAddress(string address, CancellationToken ct)
    {
        if(string.IsNullOrEmpty(address))
            return false;

        switch(networkType)
        {
            case DeroNetworkType.Main:
                if(!address.ToLower().StartsWith("dero"))
                    return false;
                break;

            case DeroNetworkType.Test:
                if(!address.ToLower().StartsWith("deto"))
                    return false;
                break;
        }

        var request = new GetEncryptedBalanceRequest
        {
            Address = address,
        };

        var response = await rpc.ExecuteAsync<GetEncryptedBalanceResponse>(logger, DeroCommands.GetEncryptedBalance, ct, request);

        if(response.Error != null || response?.Response?.Status != "OK")
        {
            var error = response.Error?.Message ?? response.Response?.Status;

            throw new StratumException(StratumError.UnauthorizedWorker, error);
        }

        return true;
    }

    public bool IsAddressBlacklisted(string address)
    {
        if(extraPoolConfig.BlacklistedAddresses == null || extraPoolConfig.BlacklistedAddresses.Length == 0)
            return false;

        return extraPoolConfig.BlacklistedAddresses.Contains(address);
    }

    public BlockchainStats BlockchainStats { get; } = new();

    public void PrepareWorkerJob(DeroWorkerJob workerJob, out string blob, out string target)
    {
        blob = null;
        target = null;

        var job = currentJob;

        if(job != null)
        {
            lock(job)
            {
                job.PrepareWorkerJob(workerJob, out blob, out target);
            }
        }
    }

    public async ValueTask<Share> SubmitShareAsync(StratumConnection worker,
        DeroSubmitShareRequest request, DeroWorkerJob workerJob, CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(request);

        var context = worker.ContextAs<DeroWorkerContext>();

        var job = currentJob;
        if(workerJob.Height != job?.BlockTemplate.Height)
            throw new StratumException(StratumError.MinusOne, "block expired");

        // validate & process
        var (share, blockHex) = job.ProcessShare(workerJob, request.Nonce, request.Result, worker);

        // enrich share with common data
        share.PoolId = poolConfig.Id;
        share.IpAddress = worker.RemoteEndpoint.Address.ToString();
        share.Miner = context.Miner;
        share.Worker = context.Worker;
        share.UserAgent = context.UserAgent;
        share.Source = clusterConfig.ClusterName;
        share.NetworkDifficulty = BlockchainStats.NetworkDifficulty > 0 ? BlockchainStats.NetworkDifficulty : job.BlockTemplate.Difficulty;
        share.Created = clock.Now;

        // if block candidate, submit & check if accepted by network
        if(share.IsBlockCandidate)
        {
            if(job.MFinal)
            {
                logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash[..6]}]");
            }
            else
            {
                logger.Info(() => $"Submitting mini block {share.BlockHeight} [{share.BlockHash[..6]}]");
            }

            var error = await SubmitBlockAsync(share, workerJob.JobId, blockHex);

            if(error != null)
            {
                throw new StratumException(StratumError.MinusOne, error);
            }

            if(share.IsBlockCandidate)
            {
                if(job.MFinal)
                {
                    logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash[..6]}] submitted by {context.Miner}");
                    OnBlockFound();
                    share.TransactionConfirmationData = share.BlockHash;
                }
                else
                {
                    share.BlockHash = $"pr{share.BlockHash}";
                    share.TransactionConfirmationData = $"mb{request.Result}";
                }
            }

            else
            {
                // clear fields that no longer apply
                share.TransactionConfirmationData = null;
            }
        }

        return share;
    }

    #endregion // API-Surface

    #region Overrides

    protected override void ConfigureDaemons()
    {
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

        rpc = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);
        rpc.SetHideCharSetFromContentType(true);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // also setup wallet daemon
            walletRpc = new RpcClient(walletDaemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);
            walletRpc.SetHideCharSetFromContentType(true);
        }
    }

    protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
    {
        // test daemons
        var response = await rpc.ExecuteAsync<GetInfoResponse>(logger, DeroCommands.GetInfo, ct);

        if(response.Error != null)
            return false;

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // test wallet daemons
            var responses2 = await walletRpc.ExecuteAsync<GetAddressResponse>(logger, DeroWalletCommands.GetAddress, ct);

            if(responses2.Response != null)
            {
                if(responses2.Response.Address != poolConfig.Address)
                {
                    logger.Warn(() => $"Pool address in wallet differs from configuration! ${poolConfig.Address} vs ${responses2.Response.Address}");
                    return false;
                }
            }

            return responses2.Error == null;
        }

        return true;
    }

    protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
    {
        var response = await rpc.ExecuteAsync<GetInfoResponse>(logger, DeroCommands.GetInfo, ct);

        return response.Error == null && response.Response != null &&
            (response.Response.OutgoingConnectionsCount + response.Response.IncomingConnectionsCount) > 0;
    }

    protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        var syncPendingNotificationShown = false;

        do
        {
            var request = new GetBlockTemplateRequest
            {
                WalletAddress = poolConfig.Address,
            };

            var response = await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger,
                DeroCommands.GetBlockTemplate, ct, request);

            var isSynched = response.Error is not { Code: -9 };

            if(isSynched)
            {
                logger.Info(() => "All daemons synched with blockchain");
                break;
            }

            if(!syncPendingNotificationShown)
            {
                logger.Info(() => "Daemon is still syncing with network. Manager will be started once synced.");
                syncPendingNotificationShown = true;
            }

            await ShowDaemonSyncProgressAsync(ct);
        } while(await timer.WaitForNextTickAsync(ct));
    }

    protected override async Task PostStartInitAsync(CancellationToken ct)
    {
        // coin config
        var coin = poolConfig.Template.As<DeroCoinTemplate>();

        var infoResponse = await rpc.ExecuteAsync(logger, DeroCommands.GetInfo, ct);

        if(infoResponse.Error != null)
            throw new PoolStartupException($"Init RPC failed: {infoResponse.Error.Message} (Code {infoResponse.Error.Code})", poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            var addressResponse = await walletRpc.ExecuteAsync<GetAddressResponse>(logger, DeroWalletCommands.GetAddress, ct);

            // ensure pool owns wallet
            if(clusterConfig.PaymentProcessing?.Enabled == true && addressResponse.Response?.Address != poolConfig.Address)
                throw new PoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'", poolConfig.Id);
        }

        var info = infoResponse.Response.ToObject<GetInfoResponse>();

        // chain detection
        networkType = info.IsTestnet ? DeroNetworkType.Test : DeroNetworkType.Main;

        // update stats
        BlockchainStats.RewardType = "POW";
        BlockchainStats.NetworkType = networkType.ToString();

        await UpdateNetworkStatsAsync(ct);

        // Periodically update network stats
        Observable.Interval(TimeSpan.FromMinutes(1))
            .Select(via => Observable.FromAsync(() =>
                Guard(() => UpdateNetworkStatsAsync(ct),
                    ex => logger.Error(ex))))
            .Concat()
            .Subscribe();

        SetupJobUpdates(ct);
    }

    protected virtual void SetupJobUpdates(CancellationToken ct)
    {
        var blockSubmission = blockFoundSubject.Synchronize();
        var pollTimerRestart = blockFoundSubject.Synchronize();

        var triggers = new List<IObservable<(string Via, string Data)>>
        {
            blockSubmission.Select(x => (JobRefreshBy.BlockFound, (string) null))
        };

        if(poolConfig.BlockRefreshInterval > 0)
        {
            // periodically update block-template
            var pollingInterval = poolConfig.BlockRefreshInterval > 0 ? poolConfig.BlockRefreshInterval : 1000;

            triggers.Add(Observable.Timer(TimeSpan.FromMilliseconds(pollingInterval))
                .TakeUntil(pollTimerRestart)
                .Select(_ => (JobRefreshBy.Poll, (string) null))
                .Repeat());
        }

        else
        {
            // get initial blocktemplate
            triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                .Select(_ => (JobRefreshBy.Initial, (string) null))
                .TakeWhile(_ => !hasInitialBlockTemplate));
        }

        Blocks = triggers.Merge()
            .Select(x => Observable.FromAsync(() => UpdateJob(ct, x.Via, x.Data)))
            .Concat()
            .Where(isNew => isNew)
            .Do(_ => hasInitialBlockTemplate = true)
            .Select(_ => Unit.Default)
            .Publish()
            .RefCount();
    }

    #endregion // Overrides
}
