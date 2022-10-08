using System.Diagnostics;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Native;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("blake2b")]
public unsafe class Blake2b : IHashAlgorithm
{
    internal static IMessageBus messageBus;
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        var sw = Stopwatch.StartNew();

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.blake2b(input, output, (uint) data.Length, result.Length);

                messageBus?.SendTelemetry("Blake2b", TelemetryCategory.Hash, sw.Elapsed);
            }
        }
    }
}
