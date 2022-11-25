using Miningcore.Mining;

namespace Miningcore.Blockchain.Dero;

public class DeroWorkerContext : WorkerContextBase
{
    public DeroWorkerContext(int maxJobCount)
    {
        this.maxJobCount = maxJobCount;
    }

    /// <summary>
    /// Maximum number of tracked jobs.
    /// </summary>
    private readonly int maxJobCount;

    /// <summary>
    /// Usually a wallet address
    /// NOTE: May include paymentid (seperated by a dot .)
    /// </summary>
    public string Miner { get; set; }

    /// <summary>
    /// Arbitrary worker identififer for miners using multiple rigs
    /// </summary>
    public string Worker { get; set; }

    private List<DeroWorkerJob> validJobs { get; } = new();

    public void AddJob(DeroWorkerJob job)
    {
        validJobs.Insert(0, job);

        while(validJobs.Count > maxJobCount)
            validJobs.RemoveAt(validJobs.Count - 1);
    }

    public DeroWorkerJob FindJob(string jobId)
    {
        return validJobs.FirstOrDefault(x => x.Id == jobId);
    }
}
