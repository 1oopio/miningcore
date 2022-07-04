namespace Miningcore.Api.Responses;

public class WorkerStats 
{
    public DateTime Created { get; set; }
    public double Hashrate { get; set; }
    public double ReportedHashrate { get; set; }
    public double SharesPerSecond { get; set; }
}