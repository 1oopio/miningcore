using System;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Core;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using ZeroMQ;
using Contract = Miningcore.Contracts.Contract;


namespace Miningcore.Blockchain.Kaspa.RPC;
public class KaspaGrpcClient
{
    public KaspaGrpcClient(DaemonEndpointConfig endPoint, IMessageBus messageBus, string poolId)
    {
        Contract.RequiresNonNull(messageBus);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(poolId));

        var protocol = endPoint.Ssl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var requestUrl = $"{protocol}://{endPoint.Host}:{endPoint.Port}";

        config = endPoint;
        this.messageBus = messageBus;
        this.poolId = poolId;
        this.channel = GrpcChannel.ForAddress(requestUrl);
        this.client = new RPC.RPCClient(this.channel);
        this.stream = client.MessageStream(null, null);
    }

    protected readonly DaemonEndpointConfig config;
    private readonly IMessageBus messageBus;
    private readonly string poolId;
    private readonly GrpcChannel channel;
    private readonly RPC.RPCClient client;
    private AsyncDuplexStreamingCall<KaspadMessage, KaspadMessage> stream;

    public async Task<KaspadMessage> ExecuteAsync(ILogger logger, KaspadMessage reqMessage, CancellationToken ct, bool throwOnError = false)
    {
        try
        {
            var protocol = config.Ssl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var requestUrl = $"{protocol}://{config.Host}:{config.Port}";

            logger.Trace(() => $"Sending gRPC request to {requestUrl}: {reqMessage}");

            await stream.RequestStream.WriteAsync(reqMessage, ct);

            await foreach(var response in stream.ResponseStream.ReadAllAsync())
            {
                logger.Trace(() => $"Received gRPC response: {response}");

                // messageBus.SendTelemetry(poolId, TelemetryCategory.RpcRequest, method, sw.Elapsed, response.IsSuccessStatusCode);
                return response;
            }

            return null;
        }
        catch(TaskCanceledException)
        {
            return null;
        }
        catch(Exception ex)
        {
            this.stream.Dispose();
            this.stream = client.MessageStream(null, null);

            if(throwOnError)
                throw;

            return null;
        }
    }
}
