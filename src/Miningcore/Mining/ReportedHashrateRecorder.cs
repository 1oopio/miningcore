using System.Collections;
using System.Data.Common;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.Extensions.Hosting;
using Miningcore.Blockchain;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NLog;
using Polly;
using static Miningcore.Util.ActionUtils;
using ReportedHashrate = Miningcore.Blockchain.ReportedHashrate;

namespace Miningcore.Mining;

public record StratumReportedHashrate(StratumConnection Connection, ReportedHashrate ReportedHashrate);

public class ReportedHashrateRecorder : BackgroundService
{
    public ReportedHashrateRecorder(IComponentContext ctx,
        IMasterClock clock,
        IConnectionFactory cf,
        IMapper mapper,
        IMessageBus messageBus,
        IStatsRepository statsRepo)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(cf);
        Contract.RequiresNonNull(mapper);
        Contract.RequiresNonNull(messageBus);
        Contract.RequiresNonNull(statsRepo);

        this.clock = clock;
        this.cf = cf;
        this.mapper = mapper;
        this.messageBus = messageBus;
        this.statsRepo = statsRepo;

        BuildFaultHandlingPolicy();
    }

    private readonly IMasterClock clock;
    private readonly IStatsRepository statsRepo;
    private readonly IConnectionFactory cf;
    private readonly IMessageBus messageBus;
    private const int RetryCount = 3;
    private const string PolicyContextKeyShares = "reportedHashrate";
    private IAsyncPolicy retryPolicy;
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private readonly IMapper mapper;

    private void BuildFaultHandlingPolicy()
    {
        var retry = Policy
            .Handle<DbException>()
            .Or<SocketException>()
            .Or<TimeoutException>()
            .RetryAsync(RetryCount, OnPolicyRetry);

        retryPolicy = retry;
    }

    private static void OnPolicyRetry(Exception ex, int retry, object context)
    {
        logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
    }

    private async Task PersistReportedHashratesAsync(IList<ReportedHashrate> reportedHashrates)
    {
        var context = new Dictionary<string, object> { { PolicyContextKeyShares, reportedHashrates } };

        await retryPolicy.ExecuteAsync(ctx => PersistReportedHashratesCoreAsync((IList<ReportedHashrate>) ctx[PolicyContextKeyShares]), context);
    }

    private async Task PersistReportedHashratesCoreAsync(IList<ReportedHashrate> reportedHashrates)
    {
        await cf.RunTx(async (con, tx) =>
        {
            var mapped = reportedHashrates.Select(mapper.Map<Persistence.Model.ReportedHashrate>).ToArray();

            await statsRepo.BatchInsertReportedHashrateAsync(con, tx, mapped, CancellationToken.None);
        });
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        logger.Info(() => "Online");

        logger.Info(() => "Offline");

        return messageBus.Listen<StratumReportedHashrate>()
            .ObserveOn(TaskPoolScheduler.Default)
            .Where(x => x.ReportedHashrate != null)
            .Select(x => x.ReportedHashrate)
            .Buffer(TimeSpan.FromSeconds(30), 250)
            .Where(reportedHashrates => reportedHashrates.Any())
            .Select(reportedHashrates => Observable.FromAsync(() =>
                Guard(() =>
                    PersistReportedHashratesAsync(reportedHashrates),
                    ex => logger.Error(ex.Message))))
            .Concat()
            .ToTask(ct)
            .ContinueWith(task =>
            {
                if(task.IsFaulted)
                    logger.Fatal(() => $"Terminated due to error {task.Exception?.InnerException ?? task.Exception}");
                else
                    logger.Info(() => "Offline");
            }, ct);
    }
}