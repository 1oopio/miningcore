using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.IO;
using Miningcore.Blockchain.Dero.StratumRequests;
using Miningcore.Blockchain.Dero.StratumResponses;
using Miningcore.Configuration;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Nicehash;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Dero;

[CoinFamily(CoinFamily.Dero)]
public class DeroPool : PoolBase
{
    public DeroPool(IComponentContext ctx,
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

    private DeroJobManager manager;

    private async Task OnLoginAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<DeroWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var loginRequest = request.ParamsAs<DeroLoginRequest>();

        if(string.IsNullOrEmpty(loginRequest?.Login))
            throw new StratumException(StratumError.MinusOne, "missing login");

        // extract worker/miner
        var split = loginRequest.Login.Split('.');
        context.Miner = split[0].Trim();
        context.Worker = split.Length > 1 ? split[1].Trim() : null;
        context.UserAgent = loginRequest.UserAgent?.Trim();

        var addressToValidate = context.Miner;

        var blacklisted = manager.IsAddressBlacklisted(addressToValidate);
        if (blacklisted)
        {
            await connection.RespondErrorAsync(StratumError.MinusOne, "address is blacklisted", request.Id);

            logger.Info(() => $"[{connection.ConnectionId}] Banning blacklisted worker {context.Miner} for {loginFailureBanTimeout.TotalSeconds} sec");

            banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

            Disconnect(connection);
            return;
        }

        // validate login
        var result = await manager.ValidateAddress(addressToValidate);

        context.IsSubscribed = result;
        context.IsAuthorized = result;

        if(context.IsAuthorized)
        {
            // extract control vars from password
            var passParts = loginRequest.Password?.Split(PasswordControlVarsSeparator);
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Nicehash support
            var nicehashDiff = await GetNicehashStaticMinDiff(context, manager.Coin.Name, manager.Coin.GetAlgorithmName());

            if(nicehashDiff.HasValue)
            {
                if(!staticDiff.HasValue || nicehashDiff > staticDiff)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

                    staticDiff = nicehashDiff;
                }

                else
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using miner supplied difficulty of {staticDiff.Value}");
            }

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Static difficulty set to {staticDiff.Value}");
            }

            // respond
            var loginResponse = new DeroLoginResponse
            {
                Id = connection.ConnectionId,
                Job = CreateWorkerJob(connection)
            };

            await connection.RespondAsync(loginResponse, request.Id);

            // log association
            if(!string.IsNullOrEmpty(context.Worker))
                logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {context.Worker}@{context.Miner}");
            else
                logger.Info(() => $"[{connection.ConnectionId}] Authorized miner {context.Miner}");
        }

        else
        {
            await connection.RespondErrorAsync(StratumError.MinusOne, "invalid login", request.Id);

            logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {context.Miner} for {loginFailureBanTimeout.TotalSeconds} sec");

            banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

            Disconnect(connection);
        }
    }

    private DeroJobParams CreateWorkerJob(StratumConnection connection)
    {
        var context = connection.ContextAs<DeroWorkerContext>();
        var job = new DeroWorkerJob(NextJobId(), context.Difficulty);

        manager.PrepareWorkerJob(job, out var blob, out var target);

        // should never happen
        if(string.IsNullOrEmpty(blob) || string.IsNullOrEmpty(blob))
            return null;

        var result = new DeroJobParams
        {
            JobId = job.Id,
            Height = job.Height,
            Blob = blob,
            Target = target,
            ExtraNonce = "",
            PoolWallet = poolConfig.Address,
        };

        // update context
        lock(context)
        {
            context.AddJob(job);
        }

        return result;
    }

    private async Task OnSubmitHashrate(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<DeroWorkerContext>();

        // validate worker
        if(!context.IsAuthorized)
            throw new StratumException(StratumError.UnauthorizedWorker, "unauthorized worker");
        if(!context.IsSubscribed)
            throw new StratumException(StratumError.NotSubscribed, "not subscribed");

        // recognize activity
        context.LastActivity = clock.Now;

        await connection.RespondAsync(true, request.Id);

        var hashrateRequest = request.ParamsAs<DeroSubmitHashrateRequest>();

        var lastAge = clock.Now - context.Stats.LastReportedHashrate;
        context.Stats.ReportedHashrate = hashrateRequest.Hashrate;

        if(lastAge > reportedHashrateInterval)
        {
            context.Stats.LastReportedHashrate = clock.Now;
            ReportedHashrate reported = new ReportedHashrate
            {
                PoolId = poolConfig.Id,
                Miner = context.Miner,
                Worker = context.Worker,
                Hashrate = hashrateRequest.Hashrate
            };
            messageBus.SendMessage(new StratumReportedHashrate(connection, reported));
        }
    }

    private async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<DeroWorkerContext>();

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

            // check request
            var submitRequest = request.ParamsAs<DeroSubmitShareRequest>();

            // validate worker
            if(connection.ConnectionId != submitRequest?.WorkerId || !context.IsAuthorized)
                throw new StratumException(StratumError.MinusOne, "unauthorized");

            // recognize activity
            context.LastActivity = clock.Now;

            DeroWorkerJob job;

            lock(context)
            {
                var jobId = submitRequest?.JobId;

                if((job = context.FindJob(jobId)) == null)
                    throw new StratumException(StratumError.MinusOne, "invalid jobid");
            }

            // dupe check
            if(!job.Submissions.TryAdd(job.Blob + submitRequest.Nonce, true))
                throw new StratumException(StratumError.MinusOne, "duplicate share");

            // submit
            var share = await manager.SubmitShareAsync(connection, submitRequest, job, ct);
            await connection.RespondAsync(new DeroSubmitShareResponse(), request.Id);

            // publish
            messageBus.SendMessage(new StratumShare(connection, share));

            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

            logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty, 3)}");

            // update pool stats
            if(share.TransactionConfirmationData != null)
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
        logger.Debug(() => "Broadcasting jobs");

        await Guard(() => ForEachMinerAsync(async (connection, ct) =>
        {
            // send job
            var job = CreateWorkerJob(connection);
            await connection.NotifyAsync(DeroStratumMethods.JobNotify, job);
        }));
    }

    #region Overrides

    protected override async Task SetupJobManager(CancellationToken ct)
    {
        manager = ctx.Resolve<DeroJobManager>();
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
        return new DeroWorkerContext();
    }

    protected override async Task OnRequestAsync(StratumConnection connection,
        Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<DeroWorkerContext>();

        try
        {
            switch(request.Method)
            {
                case DeroStratumMethods.Login:
                    await OnLoginAsync(connection, tsRequest);
                    break;

                case DeroStratumMethods.Submit:
                    await OnSubmitAsync(connection, tsRequest, ct);
                    break;

                case DeroStratumMethods.ReportHashrate:
                    await OnSubmitHashrate(connection, tsRequest, ct);
                    break;

                case DeroStratumMethods.KeepAlive:
                    // recognize activity
                    context.LastActivity = clock.Now;
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
            await connection.NotifyAsync(DeroStratumMethods.JobNotify, job);
        }
    }

    #endregion // Overrides
}
