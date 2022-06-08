using System.Collections.Concurrent;

namespace Miningcore.Blockchain.Dero;

// FIXME
public class DeroWorkerJob
{
    public DeroWorkerJob(string jobId, double difficulty)
    {
        Id = jobId;
        Difficulty = difficulty;
    }

    public string Id { get; }

    public uint Height { get; set; }

    public double Difficulty { get; set; }

    public string Blob { get; set; }

    public string Target { get; set; }

    public uint ExtraNonce { get; set; }

    public string JobId { get; set; }

    public readonly ConcurrentDictionary<string, bool> Submissions = new(StringComparer.OrdinalIgnoreCase);
}
