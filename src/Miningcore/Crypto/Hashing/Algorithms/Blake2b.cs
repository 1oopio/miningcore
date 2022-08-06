using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("blake2b")]
public unsafe class Blake2b : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.blake2b(input, output, (uint) data.Length, result.Length);
            }
        }
    }

    public IntPtr Init(int size, ReadOnlySpan<byte> key)
    {
        Contract.Requires<ArgumentException>(size >= 32);
        return Multihash.blake2b_init(size); ;
    }
    
    public IntPtr InitKey(int size, ReadOnlySpan<byte> key)
    {
        Contract.Requires<ArgumentException>(size >= 32);

        fixed(byte* input = key)
        {
            return Multihash.blake2b_init_key(size, input, (uint) key.Length); ;
        }
    }

    public void Update(IntPtr state, ReadOnlySpan<byte> data)
    {
        fixed(byte* input = data)
        {
            Multihash.blake2b_update(state, input, (uint) data.Length);
        }
    }

    public void Final(IntPtr state, Span<byte> result)
    {
        fixed(byte* output = result)
        {
            Multihash.blake2b_final(state, output, result.Length);
        }
    }
}
