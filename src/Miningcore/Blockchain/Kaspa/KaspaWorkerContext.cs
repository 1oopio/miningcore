using Miningcore.Mining;

namespace Miningcore.Blockchain.Kaspa;

public class KaspaWorkerContext : WorkerContextBase
{
    /// <summary>
    /// Usually a wallet address
    /// </summary>
    public string Miner { get; set; }

    /// <summary>
    /// Arbitrary worker identififer for miners using multiple rigs
    /// </summary>
    public string Worker { get; set; }
}
