using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Microsoft.Extensions.Hosting;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using NLog;
using ProtoBuf;
using ZeroMQ;

namespace Miningcore.Mining;

public class RelayPublisher : IHostedService
{
    public RelayPublisher(ClusterConfig clusterConfig, IMessageBus messageBus)
    {
        Contract.RequiresNonNull(messageBus);

        this.clusterConfig = clusterConfig;
        this.messageBus = messageBus;
    }

    private readonly IMessageBus messageBus;
    private readonly ClusterConfig clusterConfig;
    private readonly BlockingCollection<Share> shareQueue = new();
    private readonly BlockingCollection<ReportedHashrate> hashrateQueue = new();
    private IDisposable shareQueueSub;
    private IDisposable hashrateQueueSub;
    private readonly int QueueSizeWarningThreshold = 1024;
    private bool hasWarnedAboutBacklogSize;
    private ZSocket pubSocket;

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    [Flags]
    public enum WireFormat
    {
        Json = 1,
        ProtocolBuffers = 2
    }

    [Flags]
    public enum DataType
    {
        Share = 1,
        ReportedHashrate = 2
    }

    public const int WireFormatMask = 0xF;
    public const int DataTypeMask = 0xF;

    private void InitializeQueues()
    {
        shareQueueSub = shareQueue.GetConsumingEnumerable()
            .ToObservable(TaskPoolScheduler.Default)
            .Do(_ => CheckQueueBacklog())
            .Subscribe(share =>
            {
                share.Source = clusterConfig.ClusterName;
                share.BlockRewardDouble = (double) share.BlockReward;

                try
                {
                    const int flags = (int) WireFormat.ProtocolBuffers;

                    using(var msg = new ZMessage())
                    {
                        // Topic frame
                        msg.Add(new ZFrame(share.PoolId));

                        // Frame 2: flags
                        msg.Add(new ZFrame(flags));

                        // Frame 3: data type
                        msg.Add(new ZFrame((int) DataType.Share));

                        // Frame 4: payload
                        using(var stream = new MemoryStream())
                        {
                            Serializer.Serialize(stream, share);
                            msg.Add(new ZFrame(stream.ToArray()));
                        }

                        logger.Info(() => $"Relay share");
                        pubSocket.SendMessage(msg);
                    }
                }

                catch(Exception ex)
                {
                    logger.Error(ex);
                }
            });
        hashrateQueueSub = hashrateQueue.GetConsumingEnumerable()
            .ToObservable(TaskPoolScheduler.Default)
            .Do(_ => CheckQueueBacklog())
            .Subscribe(hashrate =>
            {

                try
                {
                    const int flags = (int) WireFormat.ProtocolBuffers;

                    using(var msg = new ZMessage())
                    {
                        // Topic frame
                        msg.Add(new ZFrame(hashrate.PoolId));

                        // Frame 2: flags
                        msg.Add(new ZFrame(flags));

                        // Frame 3: data type
                        msg.Add(new ZFrame((int) DataType.ReportedHashrate));

                        // Frame 4: payload
                        using(var stream = new MemoryStream())
                        {
                            Serializer.Serialize(stream, hashrate);
                            msg.Add(new ZFrame(stream.ToArray()));
                        }

                        logger.Info(() => $"Relay reported hashrate");
                        pubSocket.SendMessage(msg);
                    }
                }

                catch(Exception ex)
                {
                    logger.Error(ex);
                }
            });
    }

    private void CheckQueueBacklog()
    {
        if(shareQueue.Count > QueueSizeWarningThreshold || hashrateQueue.Count > QueueSizeWarningThreshold)
        {
            if(!hasWarnedAboutBacklogSize)
            {
                logger.Warn(() => $"Relay queue backlog has crossed {QueueSizeWarningThreshold}");
                hasWarnedAboutBacklogSize = true;
            }
        }

        else if(hasWarnedAboutBacklogSize && shareQueue.Count <= QueueSizeWarningThreshold / 2 && hashrateQueue.Count <= QueueSizeWarningThreshold / 2)
        {
            hasWarnedAboutBacklogSize = false;
        }
    }

    public Task StartAsync(CancellationToken ct)
    {
        messageBus.Listen<StratumShare>().Subscribe(x => shareQueue.Add(x.Share, ct));
        messageBus.Listen<StratumReportedHashrate>().Subscribe(x => hashrateQueue.Add(x.ReportedHashrate, ct));

        pubSocket = new ZSocket(ZSocketType.PUB);

        if(!clusterConfig.ShareRelay.Connect)
        {
            pubSocket.SetupCurveTlsServer(clusterConfig.ShareRelay.SharedEncryptionKey, logger);

            pubSocket.Bind(clusterConfig.ShareRelay.PublishUrl);

            if(pubSocket.CurveServer)
                logger.Info(() => $"Bound to {clusterConfig.ShareRelay.PublishUrl} using key {pubSocket.CurvePublicKey.ToHexString()}");
            else
                logger.Info(() => $"Bound to {clusterConfig.ShareRelay.PublishUrl}");
        }

        else
        {
            if(!string.IsNullOrEmpty(clusterConfig.ShareRelay.SharedEncryptionKey?.Trim()))
                throw new PoolStartupException("ZeroMQ Curve is not supported in ShareRelay Connect-Mode");

            pubSocket.Connect(clusterConfig.ShareRelay.PublishUrl);

            logger.Info(() => $"Connected to {clusterConfig.ShareRelay.PublishUrl}");
        }

        InitializeQueues();

        logger.Info(() => "Online");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        pubSocket.Dispose();

        shareQueueSub?.Dispose();
        shareQueueSub = null;

        hashrateQueueSub?.Dispose();
        hashrateQueueSub = null;

        return Task.CompletedTask;
    }
}
