using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using Autofac;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using Miningcore.Rpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;
using Miningcore.Blockchain.Kaspa.RPC;

namespace Miningcore.Blockchain.Kaspa;

public class KaspaJobManager : JobManagerBase<KaspaJob>
{
    public KaspaJobManager(
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
    private KaspaGrpcClient grpc;
    private readonly IMasterClock clock;
    private KaspaNetworkType networkType;
    private KaspaCoinTemplate coin;

    protected async Task<bool> UpdateJob(CancellationToken ct, string via = null, string json = null)
    {
        try
        {
            //var response = string.IsNullOrEmpty(json) ? await GetBlockTemplateAsync(ct) : GetBlockTemplateFromJson(json);

            //// may happen if daemon is currently not connected to peers
            //if(response.Error != null)
            //{
            //    logger.Warn(() => $"Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
            //    return false;
            //}

            //var blockTemplate = response.Response;
            //var job = currentJob;
            //var newHash = blockTemplate.Blob.HexToByteArray().AsSpan().Slice(7, 32).ToHexString();

            //var isNew = job == null || newHash != job.PrevHash;

            //if(isNew)
            //{
            //    messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

            //    if(via != null)
            //        logger.Info(() => $"Detected new block {blockTemplate.Height} [{via}]");
            //    else
            //        logger.Info(() => $"Detected new block {blockTemplate.Height}");

            //    UpdateHashParams(blockTemplate);

            //    // init job
            //    job = new CryptonoteJob(blockTemplate, instanceId, NextJobId(), coin, poolConfig, clusterConfig, newHash, randomXRealm);
            //    currentJob = job;

            //    // update stats
            //    BlockchainStats.LastNetworkBlockTime = clock.Now;
            //    BlockchainStats.BlockHeight = job.BlockTemplate.Height;
            //    BlockchainStats.NetworkDifficulty = job.BlockTemplate.Difficulty;
            //    BlockchainStats.NextNetworkTarget = "";
            //    BlockchainStats.NextNetworkBits = "";
            //}

            //else
            //{
            //    if(via != null)
            //        logger.Debug(() => $"Template update {blockTemplate.Height} [{via}]");
            //    else
            //        logger.Debug(() => $"Template update {blockTemplate.Height}");
            //}

            //return isNew;
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

    //private async Task<RpcResponse<GetBlockTemplateResponse>> GetBlockTemplateAsync(CancellationToken ct)
    //{
    //    var request = new GetBlockTemplateRequest
    //    {
    //        PayAddress = poolConfig.Address,
    //    };

    //    return await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger, KaspaCommands.GetBlockTemplate, ct, request);
    //}

    //private RpcResponse<GetBlockTemplateResponse> GetBlockTemplateFromJson(string json)
    //{
    //    var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

    //    return new RpcResponse<GetBlockTemplateResponse>(result.ResultAs<GetBlockTemplateResponse>());
    //}

    private async Task UpdateNetworkStatsAsync(CancellationToken ct)
    {
        try
        {
            var requestInfo = new KaspadMessage();
            requestInfo.GetBlockDagInfoRequest = new GetBlockDagInfoRequestMessage();
            var responseInfo = await grpc.ExecuteAsync(logger, requestInfo, ct);


            var requestPeers = new KaspadMessage();
            requestPeers.GetConnectedPeerInfoRequest = new GetConnectedPeerInfoRequestMessage();
            var responsePeers = await grpc.ExecuteAsync(logger, requestPeers, ct);


            if(responseInfo != null && responsePeers != null && responseInfo.GetBlockDagInfoResponse != null && responsePeers.GetConnectedPeerInfoResponse != null)
            {
                BlockchainStats.NetworkDifficulty = responseInfo.GetBlockDagInfoResponse.Difficulty;
                BlockchainStats.ConnectedPeers = responsePeers.GetConnectedPeerInfoResponse.Infos.Count;
            }
        }

        catch(Exception e)
        {
            logger.Error(e);
        }
    }

    private async Task<bool> SubmitBlockAsync(Share share, string blobHex, string blobHash)
    {
        //var response = await rpc.ExecuteAsync<SubmitResponse>(logger, CryptonoteCommands.SubmitBlock, CancellationToken.None, new[] { blobHex });

        //if(response.Error != null || response?.Response?.Status != "OK")
        //{
        //    var error = response.Error?.Message ?? response.Response?.Status;

        //    logger.Warn(() => $"Block {share.BlockHeight} [{blobHash[..6]}] submission failed with: {error}");
        //    messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {error}"));
        //    return false;
        //}

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

        // extract standard daemon endpoints
        daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .ToArray();

        ConfigureDaemons();
    }

    public bool ValidateAddress(string address)
    {
        if(string.IsNullOrEmpty(address))
            return false;

        var addressPrefix = CryptonoteBindings.DecodeAddress(address);
        var addressIntegratedPrefix = CryptonoteBindings.DecodeIntegratedAddress(address);
        var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();

        switch(networkType)
        {
            //case KaspaNetworkType.Main:
            //    if(addressPrefix != coin.AddressPrefix &&
            //       addressIntegratedPrefix != coin.AddressPrefixIntegrated)
            //        return false;
            //    break;

            //case KaspaNetworkType.Test:
            //    if(addressPrefix != coin.AddressPrefixTestnet &&
            //       addressIntegratedPrefix != coin.AddressPrefixIntegratedTestnet)
            //        return false;
            //    break;

            //case KaspaNetworkType.Stage:
            //    if(addressPrefix != coin.AddressPrefixStagenet &&
            //       addressIntegratedPrefix != coin.AddressPrefixIntegratedStagenet)
            //        return false;
            //    break;
        }

        return true;
    }

    public BlockchainStats BlockchainStats { get; } = new();

    //public void PrepareWorkerJob(CryptonoteWorkerJob workerJob, out string blob, out string target)
    //{
    //    blob = null;
    //    target = null;

    //    var job = currentJob;

    //    if(job != null)
    //    {
    //        lock(job)
    //        {
    //            job.PrepareWorkerJob(workerJob, out blob, out target);
    //        }
    //    }
    //}

    //public async ValueTask<Share> SubmitShareAsync(StratumConnection worker,
    //    CryptonoteSubmitShareRequest request, CryptonoteWorkerJob workerJob, CancellationToken ct)
    //{
    //    Contract.RequiresNonNull(worker);
    //    Contract.RequiresNonNull(request);

    //    var context = worker.ContextAs<CryptonoteWorkerContext>();

    //    var job = currentJob;
    //    if(workerJob.Height != job?.BlockTemplate.Height)
    //        throw new StratumException(StratumError.MinusOne, "block expired");

    //    // validate & process
    //    var (share, blobHex) = job.ProcessShare(request.Nonce, workerJob.ExtraNonce, request.Hash, worker);

    //    // enrich share with common data
    //    share.PoolId = poolConfig.Id;
    //    share.IpAddress = worker.RemoteEndpoint.Address.ToString();
    //    share.Miner = context.Miner;
    //    share.Worker = context.Worker;
    //    share.UserAgent = context.UserAgent;
    //    share.Source = clusterConfig.ClusterName;
    //    share.NetworkDifficulty = job.BlockTemplate.Difficulty;
    //    share.Created = clock.Now;

    //    // if block candidate, submit & check if accepted by network
    //    if(share.IsBlockCandidate)
    //    {
    //        logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash[..6]}]");

    //        share.IsBlockCandidate = await SubmitBlockAsync(share, blobHex, share.BlockHash);

    //        if(share.IsBlockCandidate)
    //        {
    //            logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash[..6]}] submitted by {context.Miner}");

    //            OnBlockFound();

    //            share.TransactionConfirmationData = share.BlockHash;
    //        }

    //        else
    //        {
    //            // clear fields that no longer apply
    //            share.TransactionConfirmationData = null;
    //        }
    //    }

    //    return share;
    //}

    #endregion // API-Surface

    #region Overrides

    protected override void ConfigureDaemons()
    {
        grpc = new KaspaGrpcClient(daemonEndpoints.First(), messageBus, poolConfig.Id);
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
            //var addressResponse = await walletRpc.ExecuteAsync<GetAddressResponse>(logger, CryptonoteWalletCommands.GetAddress, ct);

            //// ensure pool owns wallet
            //if(clusterConfig.PaymentProcessing?.Enabled == true && addressResponse.Response?.Address != poolConfig.Address)
            //    throw new PoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'", poolConfig.Id);
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
                case "simnet":
                    networkType = KaspaNetworkType.Sim;
                    break;
                default:
                    throw new PoolStartupException($"Unsupport net type '{response.GetCurrentNetworkResponse.CurrentNetwork}'", poolConfig.Id);
            }
        }

        //// address validation
        //poolAddressBase58Prefix = CryptonoteBindings.DecodeAddress(poolConfig.Address);
        //if(poolAddressBase58Prefix == 0)
        //    throw new PoolStartupException("Unable to decode pool-address", poolConfig.Id);

        //switch(networkType)
        //{
        //    case CryptonoteNetworkType.Main:
        //        if(poolAddressBase58Prefix != coin.AddressPrefix)
        //            throw new PoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefix}, got {poolAddressBase58Prefix}", poolConfig.Id);
        //        break;

        //    case CryptonoteNetworkType.Stage:
        //        if(poolAddressBase58Prefix != coin.AddressPrefixStagenet)
        //            throw new PoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefixStagenet}, got {poolAddressBase58Prefix}", poolConfig.Id);
        //        break;

        //    case CryptonoteNetworkType.Test:
        //        if(poolAddressBase58Prefix != coin.AddressPrefixTestnet)
        //            throw new PoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefixTestnet}, got {poolAddressBase58Prefix}", poolConfig.Id);
        //        break;
        //}

        // update stats
        BlockchainStats.RewardType = "POW";
        BlockchainStats.NetworkType = networkType.ToString();

        await UpdateNetworkStatsAsync(ct);

        // Periodically update network stats
        Observable.Interval(TimeSpan.FromMinutes(1))
            .Select(via => Observable.FromAsync(() =>
                Guard(()=> UpdateNetworkStatsAsync(ct),
                    ex=> logger.Error(ex))))
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

        // get initial blocktemplate
        triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
            .Select(_ => (JobRefreshBy.Initial, (string) null))
            .TakeWhile(_ => !hasInitialBlockTemplate));

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
