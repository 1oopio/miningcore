using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Hosting;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using ProtoBuf;
using ZeroMQ;

namespace Miningcore.Mining;

/// <summary>
/// Receives external shares or other data from relays and re-publishes for consumption
/// </summary>
public class RelayReceiver : BackgroundService
{
    public RelayReceiver(
        ClusterConfig clusterConfig,
        IMasterClock clock,
        IMessageBus messageBus)
    {
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);

        this.clusterConfig = clusterConfig;
        this.clock = clock;
        this.messageBus = messageBus;
    }

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private readonly IMasterClock clock;
    private readonly IMessageBus messageBus;
    private readonly ClusterConfig clusterConfig;
    private readonly CompositeDisposable disposables = new();
    private readonly ConcurrentDictionary<string, PoolContext> pools = new();
    private readonly BufferBlock<(string Url, ZMessage Message)> queue = new();

    readonly JsonSerializer serializer = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    private class PoolContext
    {
        public PoolContext(IMiningPool pool, ILogger logger)
        {
            Pool = pool;
            Logger = logger;
        }

        public IMiningPool Pool { get; }
        public ILogger Logger { get; }
        public DateTime? LastBlock { get; set; }
        public long BlockHeight { get; set; }
    }

    private void AttachPool(IMiningPool pool)
    {
        var ctx = new PoolContext(pool, LogUtil.GetPoolScopedLogger(typeof(ShareRecorder), pool.Config));
        pools.TryAdd(pool.Config.Id, ctx);
    }

    private void OnPoolStatusNotification(PoolStatusNotification notification)
    {
        if(notification.Status == PoolStatus.Online)
            AttachPool(notification.Pool);
    }

    private Task StartMessageReceiver(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            Thread.CurrentThread.Name = "RelayReceiver Socket Poller";
            var timeout = TimeSpan.FromMilliseconds(5000);
            var reconnectTimeout = TimeSpan.FromSeconds(60);

            var relays = clusterConfig.ShareRelays
                .DistinctBy(x => $"{x.Url}:{x.SharedEncryptionKey}")
                .ToArray();

            while(!ct.IsCancellationRequested)
            {
                // track last message received per endpoint
                var lastMessageReceived = relays.Select(_ => clock.Now).ToArray();

                try
                {
                    // setup sockets
                    var sockets = relays.Select(x=> SetupSubSocket(x)).ToArray();

                    using(new CompositeDisposable(sockets))
                    {
                        var pollItems = sockets.Select(_ => ZPollItem.CreateReceiver()).ToArray();

                        while(!ct.IsCancellationRequested)
                        {
                            if(sockets.PollIn(pollItems, out var messages, out var error, timeout))
                            {
                                for(var i = 0; i < messages.Length; i++)
                                {
                                    var msg = messages[i];

                                    if(msg != null)
                                    {
                                        lastMessageReceived[i] = clock.Now;

                                        queue.Post((relays[i].Url, msg));
                                    }

                                    else if(clock.Now - lastMessageReceived[i] > reconnectTimeout)
                                    {
                                        // re-create socket
                                        sockets[i].Dispose();
                                        sockets[i] = SetupSubSocket(relays[i], true);

                                        // reset clock
                                        lastMessageReceived[i] = clock.Now;

                                        logger.Info(() => $"Receive timeout of {reconnectTimeout.TotalSeconds} seconds exceeded. Re-connecting to {relays[i].Url} ...");
                                    }
                                }

                                if(error != null)
                                    logger.Error(() => $"{nameof(RelayReceiver)}: {error.Name} [{error.Name}] during receive");
                            }

                            else
                            {
                                // check for timeouts
                                for(var i = 0; i < messages.Length; i++)
                                {
                                    if(clock.Now - lastMessageReceived[i] > reconnectTimeout)
                                    {
                                        // re-create socket
                                        sockets[i].Dispose();
                                        sockets[i] = SetupSubSocket(relays[i], true);

                                        // reset clock
                                        lastMessageReceived[i] = clock.Now;

                                        logger.Info(() => $"Receive timeout of {reconnectTimeout.TotalSeconds} seconds exceeded. Re-connecting to {relays[i].Url} ...");
                                    }
                                }
                            }
                        }
                    }
                }

                catch(Exception ex)
                {
                    logger.Error(() => $"{nameof(RelayReceiver)}: {ex}");

                    if(!ct.IsCancellationRequested)
                        Thread.Sleep(5000);
                }
            }
        }, ct);
    }

    private static ZSocket SetupSubSocket(ShareRelayEndpointConfig relay, bool silent = false)
    {
        var subSocket = new ZSocket(ZSocketType.SUB);
        subSocket.SetupCurveTlsClient(relay.SharedEncryptionKey, logger);
        subSocket.Connect(relay.Url);
        subSocket.SubscribeAll();

        if(!silent)
        {
            if(subSocket.CurveServerKey != null)
                logger.Info($"Monitoring external stratum {relay.Url} using key {subSocket.CurveServerKey.ToHexString()}");
            else
                logger.Info($"Monitoring external stratum {relay.Url}");
        }

        return subSocket;
    }

    private Task StartMessageProcessors(CancellationToken ct)
    {
        var tasks = Enumerable.Repeat(ProcessMessages(ct), Environment.ProcessorCount);

        return Task.WhenAll(tasks);
    }

    private async Task ProcessMessages(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                var (url, msg) = await queue.ReceiveAsync(ct);

                using(msg)
                {
                    ProcessMessage(url, msg);
                }
            }

            catch(Exception ex)
            {
                logger.Error(ex);
            }
        }
    }

    private void ProcessMessage(string url, ZMessage msg)
    {
        // extract frames
        var topic = msg[0].ToString(Encoding.UTF8);
        var flags = msg[1].ReadUInt32();
        var type = msg[2].ReadUInt32();
        var data = msg[3].Read();

        // validate
        if(string.IsNullOrEmpty(topic) || !pools.TryGetValue(topic, out var poolContext))
        {
            logger.Warn(() => $"Received relayed data for pool '{topic}' which is not known locally. Ignoring ...");
            return;
        }

        if(data?.Length == 0)
        {
            logger.Warn(() => $"Received empty data from {url}/{topic}. Ignoring ...");
            return;
        }

        // TMP FIX
        if((flags & RelayPublisher.WireFormatMask) == 0)
            flags = BitConverter.ToUInt32(BitConverter.GetBytes(flags).ToNewReverseArray());

        // deserialize
        var wireFormat = (RelayPublisher.WireFormat) (flags & RelayPublisher.WireFormatMask);
        var dataType = (RelayPublisher.DataType) (type & RelayPublisher.DataTypeMask);

        switch(dataType)
        {
            case RelayPublisher.DataType.Share:
                ProcessMessageShare(poolContext, url, topic, wireFormat, data);
                break;
            case RelayPublisher.DataType.ReportedHashrate:
                ProcessMessageReportedHashrate(poolContext, url, topic, wireFormat, data);
                break;
            default:
                logger.Error(() => $"Unsupported datatype {dataType} received from {url}/{topic} ");
                break;
        }
    }

    private void ProcessMessageShare(PoolContext poolContext, string url, string topic, RelayPublisher.WireFormat wireFormat, byte[] data)
    {
        Share share = null;

        switch(wireFormat)
        {
            case RelayPublisher.WireFormat.Json:
                using(var stream = new MemoryStream(data))
                {
                    using(var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        using(var jreader = new JsonTextReader(reader))
                        {
                            share = serializer.Deserialize<Share>(jreader);
                        }
                    }
                }

                break;

            case RelayPublisher.WireFormat.ProtocolBuffers:
                using(var stream = new MemoryStream(data))
                {
                    share = Serializer.Deserialize<Share>(stream);
                    share.BlockReward = (decimal) share.BlockRewardDouble;
                }

                break;

            default:
                logger.Error(() => $"Unsupported wire format {wireFormat} of share received from {url}/{topic} ");
                break;
        }

        if(share == null)
        {
            logger.Error(() => $"Unable to deserialize share received from {url}/{topic}");
            return;
        }

        // store
        share.PoolId = topic;
        share.Created = clock.Now;
        messageBus.SendMessage(new StratumShare(null, share));

        // update poolstats from shares
        if(poolContext != null)
        {
            var pool = poolContext.Pool;
            var shareMultiplier = poolContext.Pool.ShareMultiplier;

            poolContext.Logger.Info(() => $"External {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}share accepted: D={Math.Round(share.Difficulty * shareMultiplier, 4)}");

            messageBus.SendTelemetry(share.PoolId, TelemetryCategory.Share, TimeSpan.Zero, true);

            if(pool.NetworkStats != null)
            {
                pool.NetworkStats.BlockHeight = (ulong) share.BlockHeight;
                pool.NetworkStats.NetworkDifficulty = share.NetworkDifficulty;

                if(poolContext.BlockHeight != share.BlockHeight)
                {
                    pool.NetworkStats.LastNetworkBlockTime = clock.Now;
                    poolContext.BlockHeight = share.BlockHeight;
                    poolContext.LastBlock = clock.Now;
                }

                else
                    pool.NetworkStats.LastNetworkBlockTime = poolContext.LastBlock;
            }
        }

        else
            logger.Info(() => $"External {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}share accepted: D={Math.Round(share.Difficulty, 4)}");
    }


    private void ProcessMessageReportedHashrate(PoolContext poolContext, string url, string topic, RelayPublisher.WireFormat wireFormat, byte[] data)
    {
        ReportedHashrate hashrate = null;

        switch(wireFormat)
        {
            case RelayPublisher.WireFormat.Json:
                using(var stream = new MemoryStream(data))
                {
                    using(var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        using(var jreader = new JsonTextReader(reader))
                        {
                            hashrate = serializer.Deserialize<ReportedHashrate>(jreader);
                        }
                    }
                }

                break;

            case RelayPublisher.WireFormat.ProtocolBuffers:
                using(var stream = new MemoryStream(data))
                {
                    hashrate = Serializer.Deserialize<ReportedHashrate>(stream);
                }

                break;

            default:
                logger.Error(() => $"Unsupported wire format {wireFormat} of reported hashrate received from {url}/{topic} ");
                break;
        }

        if(hashrate == null)
        {
            logger.Error(() => $"Unable to deserialize reported hashrate received from {url}/{topic}");
            return;
        }

        messageBus.SendMessage(new StratumReportedHashrate(null, hashrate));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if(clusterConfig.ShareRelays != null)
        {
            try
            {
                // monitor pool lifetime
                disposables.Add(messageBus.Listen<PoolStatusNotification>()
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Subscribe(OnPoolStatusNotification));

                // process messages
                await Task.WhenAll(
                    StartMessageReceiver(ct),
                    StartMessageProcessors(ct));
            }

            finally
            {
                disposables.Dispose();
            }
        }
        
    }
}
