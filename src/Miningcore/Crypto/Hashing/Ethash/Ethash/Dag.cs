using System.Diagnostics;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Contracts;
using Miningcore.Crypto.Hashing.Ethash;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using NLog;

namespace Miningcore.Crypto.Hashing.Ethash.Ethash;

[Identifier("ethash")]
public class Dag : IEthashDag
{
    public Dag(ulong epoch)
    {
        Epoch = epoch;
    }

    public ulong Epoch { get; set; }

    private IntPtr handle = IntPtr.Zero;
    private static readonly Semaphore sem = new(1, 1);

    internal static IMessageBus messageBus;

    public DateTime LastUsed { get; set; }

    public void Dispose()
    {
        if(handle != IntPtr.Zero)
        {
            Miningcore.Native.Ethash.ethash_full_delete(handle);
            handle = IntPtr.Zero;
        }
    }

    public async Task GenerateAsync(string dagDir, ILogger logger, CancellationToken ct)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(dagDir));

        if(handle == IntPtr.Zero)
        {
            await Task.Run(() =>
            {
                try
                {
                    sem.WaitOne();

                    // re-check after obtaining lock
                    if(handle != IntPtr.Zero)
                        return;

                    logger.Info(() => $"Generating DAG for epoch {Epoch}");

                    var started = DateTime.Now;
                    var block = Epoch * EthereumConstants.EpochLength;

                    // Generate a temporary cache
                    var light = Miningcore.Native.Ethash.ethash_light_new(block);

                    try
                    {
                        // Generate the actual DAG
                        handle = Miningcore.Native.Ethash.ethash_full_new(dagDir, light, progress =>
                        {
                            logger.Info(() => $"Generating DAG for epoch {Epoch}: {progress}%");

                            return !ct.IsCancellationRequested ? 0 : 1;
                        });

                        if(handle == IntPtr.Zero)
                            throw new OutOfMemoryException("ethash_full_new IO or memory error");

                        logger.Info(() => $"Done generating DAG for epoch {Epoch} after {DateTime.Now - started}");
                    }

                    finally
                    {
                        if(light != IntPtr.Zero)
                            Miningcore.Native.Ethash.ethash_light_delete(light);
                    }
                }

                finally
                {
                    sem.Release();
                }
            }, ct);
        }
    }

    public unsafe bool Compute(ILogger logger, byte[] hash, ulong nonce, out byte[] mixDigest, out byte[] result)
    {
        Contract.RequiresNonNull(hash);

        var sw = Stopwatch.StartNew();

        mixDigest = null;
        result = null;

        var value = new Miningcore.Native.Ethash.ethash_return_value();

        fixed(byte* input = hash)
        {
            Miningcore.Native.Ethash.ethash_full_compute(handle, input, nonce, ref value);
        }

        if(value.success)
        {
            mixDigest = value.mix_hash.value;
            result = value.result.value;
        }

        messageBus?.SendTelemetry("Ethash", TelemetryCategory.Hash, sw.Elapsed, value.success);

        return value.success;
    }
}
