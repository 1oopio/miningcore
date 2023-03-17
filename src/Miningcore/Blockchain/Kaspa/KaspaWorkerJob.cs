using System.Collections.Concurrent;

namespace Miningcore.Blockchain.Kaspa;

public class KaspaWorkerJob
{
    public KaspaWorkerJob(double difficulty)
    {
        Difficulty = difficulty;
    }
    public string Id { get; set; }
    private double Difficulty { get; }
}
