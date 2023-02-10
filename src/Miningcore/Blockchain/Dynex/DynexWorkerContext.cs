using Miningcore.Mining;

namespace Miningcore.Blockchain.Dynex;

public class DynexWorkerContext : WorkerContextBase
{
    /// <summary>
    /// Usually a wallet address
    /// NOTE: May include paymentid (seperated by a dot .)
    /// </summary>
    public string Miner { get; set; }

    /// <summary>
    /// Arbitrary worker identififer for miners using multiple rigs
    /// </summary>
    public string Worker { get; set; }

    private List<DynexWorkerJob> validJobs { get; } = new();

    public void AddJob(DynexWorkerJob job)
    {
        validJobs.Insert(0, job);

        while(validJobs.Count > 4)
            validJobs.RemoveAt(validJobs.Count - 1);
    }

    public DynexWorkerJob FindJob(string jobId)
    {
        return validJobs.FirstOrDefault(x => x.Id == jobId);
    }
}
