using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("heavyhash_kaspa")]
public unsafe class HeavyHashKaspa : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);
        Contract.Requires<ArgumentException>(extra.Length >= 1);

        byte[] seedData = (byte[]) extra[0];

        fixed(byte* input = data)
        {
            fixed(byte* output = result)
            {
                fixed(byte* seed = seedData)
                {
                    Multihash.heavyhash_kaspa(input, seed, output, (uint) data.Length, (uint) seedData.Length);
                }
            }
        }
    }
}
