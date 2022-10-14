using System.Reactive;
using System.Reactive.Linq;
using Autofac;
using Miningcore.Blockchain.Kaspa.Configuration;
using Miningcore.Blockchain.Kaspa.RPC;
using Miningcore.Blockchain.Kaspa.RPC.Messages;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications.Messages;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NLog;
using static Miningcore.Util.ActionUtils;
using BigInteger = System.Numerics.BigInteger;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain.Kaspa;

public class KaspaJobManager : JobManagerBase<KaspaJob>
{
    public KaspaJobManager(
        IComponentContext ctx,
        IMasterClock clock,
        IExtraNonceProvider extraNonceProvider,
        IMessageBus messageBus) :
        base(ctx, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(extraNonceProvider);
        Contract.RequiresNonNull(messageBus);

        this.clock = clock;
        this.extraNonceProvider = extraNonceProvider;

        extraNonceSize = 8 - extraNonceProvider.ByteSize;
    }

    private DaemonEndpointConfig[] daemonEndpoints;
    private DaemonEndpointConfig[] walletDaemonEndpoints;
    private KaspaGrpcRPCClient grpc;
    private KaspaGrpcWalletClient grpcWallet;
    private KaspaPoolConfigExtra extraPoolConfig;
    private readonly IMasterClock clock;
    private KaspaNetworkType networkType;
    private readonly IExtraNonceProvider extraNonceProvider;
    private KaspaCoinTemplate coin;
    private readonly int extraNonceSize;
    private readonly List<KaspaJob> validJobs = new();

    protected async Task<bool> UpdateJob(CancellationToken ct, string via = null, string json = null)
    {
        try
        {
            KaspadMessage response = string.IsNullOrEmpty(json) ? await GetBlockTemplateAsync(ct) : GetBlockTemplateFromJson(json);

            if(response == null || response.GetBlockTemplateResponse == null || response.GetBlockTemplateResponse.Error != null)
            {
                logger.Warn(() => $"Unable to update job. Daemon responded with: {response?.GetBlockTemplateResponse?.Error?.Message}");
                return false;
            }

            var blockTemplate = response.GetBlockTemplateResponse;

            if(!blockTemplate.IsSynced)
            {
                logger.Warn(() => $"Unable to update job. Daemon is not synced");
                return false;
            }

            var block = blockTemplate.Block;

            var job = currentJob;

            var newHash = KaspaJob.HashBlock(block, true).ToHexString();
            var isNew = job == null || newHash != job.PrevHash;

            if(isNew)
            {
                if(via != null)
                    logger.Info(() => $"Detected new block {newHash} [{via}]");
                else
                    logger.Info(() => $"Detected new block {newHash}");

                var jobId = NextJobId();

                job = new KaspaJob(block, jobId, newHash, extraNonceSize);

                lock(jobLock)
                {
                    validJobs.Insert(0, job);

                    // trim active jobs
                    while(validJobs.Count > KaspaConstants.MaxActiveJobs)
                        validJobs.RemoveAt(validJobs.Count - 1);
                }

                currentJob = job;

                BlockchainStats.LastNetworkBlockTime = clock.Now;
                BlockchainStats.BlockHeight = block.Header.DaaScore;
                BlockchainStats.NetworkDifficulty = job.DifficultyFromTargetBits();
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

    private async Task<KaspadMessage> GetBlockTemplateAsync(CancellationToken ct)
    {
        var request = new KaspadMessage();
        request.GetBlockTemplateRequest = new GetBlockTemplateRequestMessage();
        request.GetBlockTemplateRequest.PayAddress = poolConfig.Address;
        request.GetBlockTemplateRequest.ExtraData = extraPoolConfig.BlockTemplatePayload ?? String.Empty;
        return await grpc.ExecuteAsync(logger, request, ct);
    }

    private KaspadMessage GetBlockTemplateFromJson(string json)
    {
        return KaspadMessage.Parser.ParseJson(json);
    }

    private async Task UpdateNetworkStatsAsync(CancellationToken ct)
    {
        try
        {
            var requestInfo = new KaspadMessage();
            requestInfo.GetBlockDagInfoRequest = new GetBlockDagInfoRequestMessage();
            var responseInfo = await grpc.ExecuteAsync(logger, requestInfo, ct);

            if(responseInfo != null && responseInfo.GetBlockDagInfoResponse != null)
            {

                var requestHashrate = new KaspadMessage();
                requestHashrate.EstimateNetworkHashesPerSecondRequest = new EstimateNetworkHashesPerSecondRequestMessage();
                requestHashrate.EstimateNetworkHashesPerSecondRequest.WindowSize = 1000;
                requestHashrate.EstimateNetworkHashesPerSecondRequest.StartHash = responseInfo.GetBlockDagInfoResponse.TipHashes.First();
                var responseHashrate = await grpc.ExecuteAsync(logger, requestHashrate, ct);
                if(responseHashrate != null && responseHashrate.EstimateNetworkHashesPerSecondResponse != null && responseHashrate.EstimateNetworkHashesPerSecondResponse.Error == null)
                {
                    BlockchainStats.NetworkHashrate = responseHashrate.EstimateNetworkHashesPerSecondResponse.NetworkHashesPerSecond;
                }
            }

            var requestPeers = new KaspadMessage();
            requestPeers.GetConnectedPeerInfoRequest = new GetConnectedPeerInfoRequestMessage();
            var responsePeers = await grpc.ExecuteAsync(logger, requestPeers, ct);

            if(responsePeers != null && responsePeers.GetConnectedPeerInfoResponse != null)
            {
                BlockchainStats.ConnectedPeers = responsePeers.GetConnectedPeerInfoResponse.Infos.Count;
            }
        }

        catch(Exception e)
        {
            logger.Error(e);
        }
    }

    private async Task<bool> SubmitBlockAsync(Share share, RpcBlock block)
    {
        var request = new KaspadMessage();
        request.SubmitBlockRequest = new SubmitBlockRequestMessage();
        request.SubmitBlockRequest.Block = block;
        request.SubmitBlockRequest.AllowNonDAABlocks = false;
        var response = await grpc.ExecuteAsync(logger, request, CancellationToken.None);

        if(response == null || response.SubmitBlockResponse == null || response.SubmitBlockResponse.Error != null)
        {
            var error = response?.SubmitBlockResponse?.Error?.Message ?? "Unknown";
            logger.Warn(() => $"Block {share.BlockHeight} [{share.BlockHash[..6]}] submission failed with: {error}");
            messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {error}"));
            return false;
        }

        return true;
    }

    #region API-Surface

    public IObservable<Unit> Blocks { get; private set; }

    public KaspaCoinTemplate Coin => coin;

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        Contract.RequiresNonNull(pc);
        Contract.RequiresNonNull(cc);

        logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<KaspaJob>), pc);
        poolConfig = pc;
        clusterConfig = cc;
        coin = pc.Template.As<KaspaCoinTemplate>();

        extraPoolConfig = pc.Extra.SafeExtensionDataAs<KaspaPoolConfigExtra>();

        // extract standard daemon endpoints
        daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .ToArray();

        if(cc.PaymentProcessing?.Enabled == true && pc.PaymentProcessing?.Enabled == true)
        {
            // extract wallet daemon endpoints
            walletDaemonEndpoints = pc.Daemons
                .Where(x => x.Category?.ToLower() == KaspaConstants.WalletDaemonCategory)
                .ToArray();

            if(walletDaemonEndpoints.Length == 0)
                throw new PoolStartupException("Wallet-RPC daemon is not configured (Daemon configuration for monero-pools require an additional entry of category \'wallet' pointing to the wallet daemon)", pc.Id);
        }

        ConfigureDaemons();
    }

    public bool ValidateAddress(string address)
    {
        if(string.IsNullOrEmpty(address))
            return false;

        switch(networkType)
        {
            case KaspaNetworkType.Main:
                if(!address.ToLower().StartsWith("kaspa:"))
                    return false;
                break;

            case KaspaNetworkType.Test:
                if(!address.ToLower().StartsWith("kaspatest:"))
                    return false;
                break;

            case KaspaNetworkType.Dev:
                if(!address.ToLower().StartsWith("kaspadev:"))
                    return false;
                break;
        }

        return true;
    }

    public BlockchainStats BlockchainStats { get; } = new();

    public void PrepareWorkerJob(KaspaWorkerJob workerJob, out string hash, out BigInteger[] jobs, out long timestamp)
    {
        hash = null;
        jobs = null;
        timestamp = 0;

        var job = currentJob;

        if(job != null)
        {
            lock(job)
            {
                job.PrepareWorkerJob(workerJob, out hash, out jobs, out timestamp);
            }
        }
    }

    public async ValueTask<Share> SubmitShareAsync(StratumConnection worker, string[] request, CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(request);

        var context = worker.ContextAs<KaspaWorkerContext>();
        var jobId = request[1];
        var nonce = request[2].StripHexPrefix();

        KaspaJob job;
        lock(jobLock)
        {
            job = validJobs.FirstOrDefault(x => x.JobId == jobId);
        }

        if(job == null)
            throw new StratumException(StratumError.JobNotFound, "job not found");


        // validate & process
        var (share, block) = job.ProcessShare(nonce, worker);

        // enrich share with common data
        share.PoolId = poolConfig.Id;
        share.IpAddress = worker.RemoteEndpoint.Address.ToString();
        share.Miner = context.Miner;
        share.Worker = context.Worker;
        share.UserAgent = context.UserAgent;
        share.Source = clusterConfig.ClusterName;
        share.Created = clock.Now;

        // if block candidate, submit & check if accepted by network
        if(share.IsBlockCandidate)
        {
            logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash[..6]}]");

            share.IsBlockCandidate = await SubmitBlockAsync(share, block);

            if(share.IsBlockCandidate)
            {
                logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash[..6]}] submitted by {context.Miner}");

                OnBlockFound();

                share.TransactionConfirmationData = nonce;
            }

            else
            {
                // clear fields that no longer apply
                share.TransactionConfirmationData = null;

                throw new StratumException(StratumError.MinusOne, "Daemon rejected block");
            }
        }

        return share;
    }

    public void PrepareWorker(StratumConnection client)
    {
        var context = client.ContextAs<KaspaWorkerContext>();
        context.ExtraNonce1 = extraNonceProvider.Next();
    }

    #endregion // API-Surface

    #region Overrides

    protected override void ConfigureDaemons()
    {
        grpc = new KaspaGrpcRPCClient(daemonEndpoints.First(), messageBus, poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // also setup wallet daemon
            grpcWallet = new KaspaGrpcWalletClient(walletDaemonEndpoints.First(), messageBus, poolConfig.Id);
        }
    }

    protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
    {
        var request = new KaspadMessage();
        request.GetBlockDagInfoRequest = new GetBlockDagInfoRequestMessage();

        // test daemons
        var response = await grpc.ExecuteAsync(logger, request, ct);

        if(response == null || response.GetBlockDagInfoResponse == null)
            return false;

        return true;
    }

    protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
    {
        var request = new KaspadMessage();
        request.GetConnectedPeerInfoRequest = new GetConnectedPeerInfoRequestMessage();

        var response = await grpc.ExecuteAsync(logger, request, ct);

        return (response != null && response.GetConnectedPeerInfoResponse != null && response.GetConnectedPeerInfoResponse.Infos.Count > 0);
    }

    protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        var syncPendingNotificationShown = false;

        do
        {
            var request = new KaspadMessage();
            request.GetInfoRequest = new GetInfoRequestMessage();

            var response = await grpc.ExecuteAsync(logger, request, ct);

            if(response != null && response.GetInfoResponse != null && response.GetInfoResponse.IsSynced)
            {
                logger.Info(() => "All daemons synched with blockchain");
                break;
            }

            if(!syncPendingNotificationShown)
            {
                logger.Info(() => "Daemon is still syncing with network. Manager will be started once synced.");
                syncPendingNotificationShown = true;
            }

        } while(await timer.WaitForNextTickAsync(ct));
    }

    protected override async Task PostStartInitAsync(CancellationToken ct)
    {
        // coin config
        var coin = poolConfig.Template.As<KaspaCoinTemplate>();
        var request = new KaspadMessage();
        request.GetCurrentNetworkRequest = new GetCurrentNetworkRequestMessage();

        var response = await grpc.ExecuteAsync(logger, request, ct);

        if(response == null || response.GetCurrentNetworkResponse == null)
            throw new PoolStartupException($"Init gRPC failed", poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            var addressResponse = await grpcWallet.ShowAddressesAsync(logger, ct);
            var found = false;

            if(addressResponse != null)
            {
                foreach(var address in addressResponse.Address)
                {
                    if(address == poolConfig.Address)
                    {
                        found = true;
                        break;
                    }
                }
            }
            else
            {
                throw new PoolStartupException($"Init gRPC for Wallet-Daemon failed", poolConfig.Id);
            }

            if(!found)
            {
                throw new PoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'", poolConfig.Id);
            }
        }

        // chain detection
        if(!string.IsNullOrEmpty(response.GetCurrentNetworkResponse.CurrentNetwork))
        {
            switch(response.GetCurrentNetworkResponse.CurrentNetwork.ToLower())
            {
                case "mainnet":
                    networkType = KaspaNetworkType.Main;
                    break;
                case "devnet":
                    networkType = KaspaNetworkType.Dev;
                    break;
                case "testnet":
                    networkType = KaspaNetworkType.Test;
                    break;
                default:
                    throw new PoolStartupException($"Unsupport net type '{response.GetCurrentNetworkResponse.CurrentNetwork}'", poolConfig.Id);
            }
        }

        //// address validation
        switch(networkType)
        {
            case KaspaNetworkType.Main:
                if(!poolConfig.Address.ToLower().StartsWith("kaspa:"))
                    throw new PoolStartupException($"Invalid pool address, should start with kaspa", poolConfig.Id);
                break;

            case KaspaNetworkType.Test:
                if(!poolConfig.Address.ToLower().StartsWith("kaspatest:"))
                    throw new PoolStartupException($"Invalid pool address, should start with kaspatest", poolConfig.Id);
                break;

            case KaspaNetworkType.Dev:
                if(!poolConfig.Address.ToLower().StartsWith("kaspadev:"))
                    throw new PoolStartupException($"Invalid pool address, should start with kaspadev", poolConfig.Id);
                break;
        }

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
