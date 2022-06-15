using System.Diagnostics;
using System.Runtime.InteropServices;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;

namespace Miningcore.Native;

public static unsafe class Astrobwt
{
    [DllImport("libastrobwt", EntryPoint = "Hash", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern bool astrobwt(byte* input, int inputLength, void* output);

    internal static IMessageBus messageBus;

    public static void AstrobwtHash(ReadOnlySpan<byte> data, Span<byte> result)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        var sw = Stopwatch.StartNew();

        fixed(byte* input = data)
        {
            fixed(byte* output = result)
            {
                // const uint8_t * input, size_t input_length, void* output

                var success = astrobwt(input, data.Length, output);
                Debug.Assert(success);

                messageBus?.SendTelemetry("Astrobwt", TelemetryCategory.Hash, sw.Elapsed, true);
            }
        }
    }
}
