using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Miningcore.Blockchain.Kaspa.RPC.Messages;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using NLog;
using Contract = Miningcore.Contracts.Contract;


namespace Miningcore.Blockchain.Kaspa.RPC;
public class KaspaGrpcRPCClient
{
    public KaspaGrpcRPCClient(DaemonEndpointConfig endPoint, IMessageBus messageBus, string poolId)
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
        var sw = Stopwatch.StartNew();

        AsyncDuplexStreamingCall<KaspadMessage, KaspadMessage> stream = null;

        try
        {
            var method = reqMessage.PayloadCase.ToString();
            var protocol = config.Ssl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var requestUrl = $"{protocol}://{config.Host}:{config.Port}";
            var channel = GrpcChannel.ForAddress(requestUrl);
            var client = new Messages.RPC.RPCClient(channel);

            stream = client.MessageStream(null, null);

            logger.Trace(() => $"Sending gRPC request to {requestUrl}: {reqMessage}");

            await stream.RequestStream.WriteAsync(reqMessage, ct);

            await foreach(var response in stream.ResponseStream.ReadAllAsync(ct))
            {
                logger.Trace(() => $"Received gRPC response: {response}");
                stream.Dispose();

                messageBus.SendTelemetry(poolId, TelemetryCategory.RpcRequest, method, sw.Elapsed, response != null && response.PayloadCase != 0);
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
