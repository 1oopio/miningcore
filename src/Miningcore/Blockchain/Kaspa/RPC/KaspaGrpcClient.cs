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

        config = endPoint;
        this.messageBus = messageBus;
        this.poolId = poolId;
    }

    protected readonly DaemonEndpointConfig config;
    private readonly IMessageBus messageBus;
    private readonly string poolId;


    public async Task<KaspadMessage> ExecuteAsync(ILogger logger, KaspadMessage reqMessage, CancellationToken ct, bool throwOnError = false)
    {
        AsyncDuplexStreamingCall<KaspadMessage, KaspadMessage> stream = null;

        try
        {
            var protocol = config.Ssl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var requestUrl = $"{protocol}://{config.Host}:{config.Port}";
            var channel = GrpcChannel.ForAddress(requestUrl);
            var client = new RPC.RPCClient(channel);
            stream = client.MessageStream(null, null);

            logger.Trace(() => $"Sending gRPC request to {requestUrl}: {reqMessage}");

            await stream.RequestStream.WriteAsync(reqMessage, ct);

            await foreach(var response in stream.ResponseStream.ReadAllAsync())
            {
                logger.Trace(() => $"Received gRPC response: {response}");
                stream.Dispose();

                // messageBus.SendTelemetry(poolId, TelemetryCategory.RpcRequest, method, sw.Elapsed, response.IsSuccessStatusCode);
                return response;
            }

            return null;
        }
        catch(TaskCanceledException)
        {
            stream?.Dispose();
            return null;
        }
        catch(Exception ex)
        {
            stream?.Dispose();

            if(throwOnError)
                throw;

            return null;
        }
    }
}
