using System.Data;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using MinerStats = Miningcore.Persistence.Model.Projections.MinerStats;

namespace Miningcore.Persistence.Repositories;

public interface IStatsRepository
{
    Task InsertPoolStatsAsync(IDbConnection con, IDbTransaction tx, PoolStats stats, CancellationToken ct);
    Task InsertMinerWorkerStatsAsync(IDbConnection con, IDbTransaction tx, MinerWorkerStats stats, CancellationToken ct);
    Task BatchInsertReportedHashrateAsync(IDbConnection con, IDbTransaction tx, IEnumerable<ReportedHashrate> stats, CancellationToken ct);
    Task<PoolStats> GetLastPoolStatsAsync(IDbConnection con, string poolId, CancellationToken ct);
    Task<decimal> GetTotalPoolPaymentsAsync(IDbConnection con, string poolId, CancellationToken ct);
    Task<PoolStats[]> GetPoolPerformanceBetweenAsync(IDbConnection con, string poolId, SampleInterval interval, DateTime start, DateTime end, CancellationToken ct);
    Task<MinerStats> GetMinerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct);
    Task<MinerWorkerHashrate[]> GetPoolMinerWorkerHashratesAsync(IDbConnection con, string poolId, CancellationToken ct);
    Task<MinerWorkerStats[]> PagePoolMinersByHashrateAsync(IDbConnection con, string poolId, DateTime from, int page, int pageSize, CancellationToken ct);

    Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenMinutelyAsync(IDbConnection con, string poolId, string miner,
        DateTime start, DateTime end, CancellationToken ct);
    Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenTenMinutelyAsync(IDbConnection con, string poolId, string miner,
        DateTime start, DateTime end, CancellationToken ct);
    Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenHourlyAsync(IDbConnection con, string poolId, string miner,
        DateTime start, DateTime end, CancellationToken ct);
    Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenDailyAsync(IDbConnection con, string poolId, string miner,
        DateTime start, DateTime end, CancellationToken ct);

    Task<int> DeletePoolStatsBeforeAsync(IDbConnection con, DateTime date, CancellationToken ct);
    Task<int> DeleteMinerStatsBeforeAsync(IDbConnection con, DateTime date, CancellationToken ct);
    Task<int> DeleteReportedHashrateBeforeAsync(IDbConnection con, DateTime date, CancellationToken ct);
}
