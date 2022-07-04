namespace Miningcore.Persistence.Model;

public record WorkerStats 
{
    public DateTime Created { get; init; }
    public double Hashrate { get; init; }
    public double ReportedHashrate { get; init; }
    public double SharesPerSecond { get; init; }
}