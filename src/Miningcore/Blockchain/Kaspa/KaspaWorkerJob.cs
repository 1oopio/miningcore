using System.Collections.Concurrent;

namespace Miningcore.Blockchain.Kaspa;

public class KaspaWorkerJob
{
    public KaspaWorkerJob(double difficulty)
    {
        Difficulty = difficulty;
    }

    public string Id { get; set; }
    public uint ExtraNonce { get; set; }
    public double Difficulty { get; set; }
    public string SeedHash { get; set; }

    public readonly ConcurrentDictionary<string, bool> Submissions = new(StringComparer.OrdinalIgnoreCase);
}
