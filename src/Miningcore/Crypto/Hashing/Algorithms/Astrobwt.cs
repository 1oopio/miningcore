namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("astrobwt")]
public class Astrobwt : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Native.Astrobwt.AstrobwtHash(data, result);
    }
}
