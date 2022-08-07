using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("heavyhash")]
public unsafe class HeavyHash : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        byte[] seedData = null;
        if(extra.Length > 0)
        {
            seedData = (byte[]) extra[0];
        }

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                if(seedData != null)
                {
                    fixed(byte* seed = seedData)
                    {
                        Multihash.heavyhash_seed(input, seed, output, (uint) data.Length, (uint) seedData.Length);
                    }
                }
                else
                {
                    Multihash.heavyhash(input, output, (uint) data.Length);
                }
            }
        }
    }
}
