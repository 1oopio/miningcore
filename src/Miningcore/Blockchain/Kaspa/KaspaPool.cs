using Miningcore.Extensions;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.IO;
using Miningcore.Configuration;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Nicehash;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Notifications.Messages;
using Newtonsoft.Json;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Kaspa;

[CoinFamily(CoinFamily.Kaspa)]
public class KaspaPool : PoolBase
{
    public KaspaPool(IComponentContext ctx,
        JsonSerializerSettings serializerSettings,
        IConnectionFactory cf,
        IStatsRepository statsRepo,
        IMapper mapper,
        IMasterClock clock,
        IMessageBus messageBus,
        RecyclableMemoryStreamManager rmsm,
        NicehashService nicehashService) :
        base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, rmsm, nicehashService)
    {
    }

    private long currentJobId;

    private KaspaJobManager manager;

    private async Task OnSubscribeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<KaspaWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.Other, "missing request id");

        var requestParams = request.ParamsAs<string[]>();

        if(requestParams == null || requestParams.Length < 1 || requestParams.Any(string.IsNullOrEmpty))
            throw new StratumException(StratumError.MinusOne, "invalid request");

        context.UserAgent = requestParams.FirstOrDefault()?.Trim();

        if(requestParams.Length >= 2 && requestParams[1] == "EthereumStratum/1.0.0")
        {
            context.EthereumStratumVariant = true;

            var data = new object[]
            {
                 true,
                "EthereumStratum/1.0.0"
            }
            .ToArray();

            var response = new JsonRpcResponse<object[]>(data, request.Id);
            await connection.RespondAsync(response);
        }
        else
        {
            await connection.RespondAsync(true, request.Id);
        }

        // setup worker context
        context.IsSubscribed = true;
    }

    private async Task OnAuthorizeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<KaspaWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var requestParams = request.ParamsAs<string[]>();
        var workerValue = requestParams?.Length > 0 ? requestParams[0] : "0";
        var password = requestParams?.Length > 1 ? requestParams[1] : null;
        var passParts = password?.Split(PasswordControlVarsSeparator);

        // extract worker/miner
        var workerParts = workerValue?.Split('.');
        var minerName = workerParts?.Length > 0 ? workerParts[0].Trim() : null;
        var workerName = workerParts?.Length > 1 ? workerParts[1].Trim() : "0";

        context.IsAuthorized = manager.ValidateAddress(minerName);

        // respond
        await connection.RespondAsync(context.IsAuthorized, request.Id);

        if(context.IsAuthorized)
        {
            context.Miner = minerName?.ToLower();
            context.Worker = workerName;

            // extract control vars from password
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Setting static difficulty of {staticDiff.Value}");
            }

            //await connection.NotifyAsync(KaspaStratumMethods.SetExtranonce, xx);
            await connection.NotifyAsync(KaspaStratumMethods.SetDifficulty, new object[] { context.Difficulty });

            logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {workerValue}");
        }

        else
        {
            if(clusterConfig?.Banning?.BanOnLoginFailure is null or true)
            {
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {minerName} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                Disconnect(connection);
            }
        }
    }

    private object[] CreateWorkerJob(StratumConnection connection)
    {
        var context = connection.ContextAs<KaspaWorkerContext>();
        var job = new KaspaWorkerJob(context.Difficulty);
        var result = new object[] { };

        manager.PrepareWorkerJob(job, out var hash, out var jobs, out var timestamp);

        if(context.EthereumStratumVariant)
        {
            result = new object[]
            {
                job.Id,
                hash + "000000000000000000" // TODO
        };

        }
        else
        {
            result = new object[]
            {
                job.Id,
                jobs,
                timestamp
            };
        }


        // update context
        lock(context)
        {
            //context.AddJob(job);
        }

        return result;
    }

    private async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<KaspaWorkerContext>();

        try
        {
            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            // check age of submission (aged submissions are usually caused by high server load)
            var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

            if(requestAge > maxShareAge)
            {
                logger.Warn(() => $"[{connection.ConnectionId}] Dropping stale share submission request (server overloaded?)");
                return;
            }

            // validate worker
            if(!context.IsAuthorized)
                throw new StratumException(StratumError.UnauthorizedWorker, "unauthorized worker");
            if(!context.IsSubscribed)
                throw new StratumException(StratumError.NotSubscribed, "not subscribed");

            // check request
            var submitRequest = request.ParamsAs<string[]>();

            if(submitRequest.Length != 3 ||
               submitRequest.Any(string.IsNullOrEmpty))
                throw new StratumException(StratumError.MinusOne, "malformed PoW result");

            // recognize activity
            context.LastActivity = clock.Now;

            Share share = await manager.SubmitShareAsync(connection, submitRequest, ct);

            await connection.RespondAsync(true, request.Id);

            // publish
            messageBus.SendMessage(new StratumShare(connection, share));

            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

            //logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty / EthereumConstants.Pow2x32, 3)}");
            logger.Info(() => $"[{connection.ConnectionId}] Share accepted: FART");

            // update pool stats
            if(share.IsBlockCandidate)
                poolStats.LastPoolBlockTime = clock.Now;

            // update client stats
            context.Stats.ValidShares++;

            await UpdateVarDiffAsync(connection, false, ct);
        }

        catch(StratumException ex)
        {
            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, false);

            // update client stats
            context.Stats.InvalidShares++;
            logger.Info(() => $"[{connection.ConnectionId}] Share rejected: {ex.Message} [{context.UserAgent}]");

            // banning
            ConsiderBan(connection, context, poolConfig.Banning);

            throw;
        }
    }

    private string NextJobId()
    {
        return Interlocked.Increment(ref currentJobId).ToString(CultureInfo.InvariantCulture);
    }

    private async Task OnNewJobAsync()
    {
        logger.Info(() => "Broadcasting jobs");

        await Guard(() => ForEachMinerAsync(async (connection, ct) =>
        {
            // send job
            var job = CreateWorkerJob(connection);
            await connection.NotifyAsync(KaspaStratumMethods.MiningNotify, job);
        }));
    }

    #region Overrides

    protected override async Task SetupJobManager(CancellationToken ct)
    {
        manager = ctx.Resolve<KaspaJobManager>();
        manager.Configure(poolConfig, clusterConfig);

        await manager.StartAsync(ct);

        if(poolConfig.EnableInternalStratum == true)
        {
            disposables.Add(manager.Blocks
                .Select(_ => Observable.FromAsync(() =>
                    Guard(OnNewJobAsync,
                        ex=> logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
                .Concat()
                .Subscribe(_ => { }, ex =>
                {
                    logger.Debug(ex, nameof(OnNewJobAsync));
                }));

            // start with initial blocktemplate
            await manager.Blocks.Take(1).ToTask(ct);
        }

        else
        {
            // keep updating NetworkStats
            disposables.Add(manager.Blocks.Subscribe());
        }
    }

    protected override async Task InitStatsAsync(CancellationToken ct)
    {
        await base.InitStatsAsync(ct);

        blockchainStats = manager.BlockchainStats;
    }

    protected override WorkerContextBase CreateWorkerContext()
    {
        return new KaspaWorkerContext();
    }

    protected override async Task OnRequestAsync(StratumConnection connection,
        Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<KaspaWorkerContext>();

        try
        {
            switch(request.Method)
            {
                case KaspaStratumMethods.Subscribe:
                    await OnSubscribeAsync(connection, tsRequest);
                    break;
                case KaspaStratumMethods.Authorize:
                    await OnAuthorizeAsync(connection, tsRequest);
                    break;
                case KaspaStratumMethods.SubmitShare:
                    await OnSubmitAsync(connection, tsRequest, ct);
                    break;
                case KaspaStratumMethods.SubmitHashrate:
                    // TODO
                    //await OnSubmitHashrate(connection, tsRequest, ct);
                    await connection.RespondAsync(true, request.Id);
                    break;
                default:
                    logger.Debug(() => $"[{connection.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    await connection.RespondErrorAsync(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        catch(StratumException ex)
        {
            await connection.RespondErrorAsync(ex.Code, ex.Message, request.Id, false);
        }
    }

    public override double HashrateFromShares(double shares, double interval)
    {
        var result = shares / interval;
        return result;
    }

    public override double ShareMultiplier => 1;

    protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff, CancellationToken ct)
    {
        await base.OnVarDiffUpdateAsync(connection, newDiff, ct);

        if(connection.Context.ApplyPendingDifficulty())
        {
            // re-send job
            var job = CreateWorkerJob(connection);
            await connection.NotifyAsync(KaspaStratumMethods.MiningNotify, job);
        }
    }

    #endregion // Overrides
}
