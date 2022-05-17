using Miningcore.Blockchain;
using Miningcore.Stratum;

namespace Miningcore.Mining;

public record StratumReportedHashrate(StratumConnection Connection, ReportedHashrate ReportedHashrate);
