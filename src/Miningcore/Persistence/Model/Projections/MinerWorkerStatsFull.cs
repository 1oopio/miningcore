namespace Miningcore.Persistence.Model.Projections;

public record MinerWorkerStatsFull
{
    public string PoolId { get; set; }
    public string Miner { get; set; }
    public string Worker { get; set; }
    public double Hashrate { get; set; }
    public double ReportedHashrate { get; set; }
    public double SharesPerSecond { get; set; }
    public DateTime Created { get; set; }
}
