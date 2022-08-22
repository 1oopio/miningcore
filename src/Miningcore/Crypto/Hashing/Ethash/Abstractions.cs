using NLog;

namespace Miningcore.Crypto.Hashing.Ethash;

public interface IEthashDag : IDisposable
{
    ulong Epoch { get; set; }
    DateTime LastUsed { get; set; }
    Task GenerateAsync(string dagDir, ILogger logger, CancellationToken ct);
    bool Compute(ILogger logger, byte[] hash, ulong nonce, out byte[] mixDigest, out byte[] result);
}

public interface IEthashFull : IDisposable
{
    string GetDefaultDagDirectory();
    void Setup(int numCaches, string dagDir, ILogger logger);
    Task<IEthashDag> GetDagAsync(ulong block, ILogger logger, CancellationToken ct);

    string AlgoName { get; }
}