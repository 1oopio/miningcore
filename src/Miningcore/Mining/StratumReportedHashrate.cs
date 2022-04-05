using Miningcore.Stratum;

namespace Miningcore.Mining;

public record StratumReportedHashrate(StratumConnection Connection, string PoolId, string Miner, string Worker, ulong Hashrate);
