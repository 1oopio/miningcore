using System.Data.Common;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Hosting;
using Miningcore.Blockchain;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NLog;
using Polly;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Mining;

public record StratumReportedHashrate(StratumConnection Connection, ReportedHashrate ReportedHashrate);

public class ReportedHashrateRecorder : BackgroundService
{
    public ReportedHashrateRecorder(IComponentContext ctx,
        IMasterClock clock,
        IConnectionFactory cf,
        IMessageBus messageBus,
        IStatsRepository statsRepo)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(cf);
        Contract.RequiresNonNull(messageBus);
        Contract.RequiresNonNull(statsRepo);

        this.clock = clock;
        this.cf = cf;
        this.messageBus = messageBus;
        this.statsRepo = statsRepo;

        BuildFaultHandlingPolicy();
    }

    private readonly IMasterClock clock;
    private readonly IStatsRepository statsRepo;
    private readonly IConnectionFactory cf;
    private readonly IMessageBus messageBus;
    private const int RetryCount = 4;
    private IAsyncPolicy readFaultPolicy;
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    private void BuildFaultHandlingPolicy()
    {
        var retry = Policy
            .Handle<DbException>()
            .Or<SocketException>()
            .Or<TimeoutException>()
            .RetryAsync(RetryCount, OnPolicyRetry);

        readFaultPolicy = retry;
    }

    private static void OnPolicyRetry(Exception ex, int retry, object context)
    {
        logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
    }

    private async Task OnMinerWorkerReportedHashrate(ReportedHashrate hashrate, CancellationToken ct)
    {
        var stats = new MinerWorkerPerformanceStats
        {
            Created = clock.Now,
            PoolId = hashrate.PoolId,
            Miner = hashrate.Miner,
            Worker = hashrate.Worker,
            Hashrate = hashrate.Hashrate,
            HashrateType = "reported"
        };

        // persist
        await cf.RunTx(async (con, tx) =>
            await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats, ct)
        );

        logger.Info(() => $"[{stats.PoolId}] Worker {stats.Miner}{(!string.IsNullOrEmpty(stats.Worker) ? $".{stats.Worker}" : string.Empty)}: Reported: {FormatUtil.FormatHashrate(stats.Hashrate)}");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.Info(() => "Online");

        var reportedHashrateEvents = messageBus.Listen<StratumReportedHashrate>()
            .ObserveOn(TaskPoolScheduler.Default)
            .Do(async msg => await Guard(async () => await (OnMinerWorkerReportedHashrate(msg.ReportedHashrate, ct)), ex => logger.Error(ex.Message)))
            .Select(_ => Unit.Default);

        await reportedHashrateEvents.ToTask(ct);

        logger.Info(() => "Offline");
    }
}