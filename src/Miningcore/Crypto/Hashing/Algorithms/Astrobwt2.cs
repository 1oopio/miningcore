using Miningcore.Native;
using static Miningcore.Native.Cryptonight.Algorithm;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("astrobwt2")]
public class Astrobwt2 : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Cryptonight.AstrobwtHash(data, result, ASTROBWT_DERO_2, 0);
    }
}
