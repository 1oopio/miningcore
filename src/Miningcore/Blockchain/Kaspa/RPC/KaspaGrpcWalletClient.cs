using System.Diagnostics;
using Grpc.Net.Client;
using Miningcore.Blockchain.Kaspa.RPC.Wallet;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using NLog;
using Contract = Miningcore.Contracts.Contract;


namespace Miningcore.Blockchain.Kaspa.RPC;
public class KaspaGrpcWalletClient
{
    public KaspaGrpcWalletClient(DaemonEndpointConfig endPoint, IMessageBus messageBus, string poolId)
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

    public async Task<GetBalanceResponse> GetBalanceAsync(ILogger logger, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var protocol = config.Ssl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var requestUrl = $"{protocol}://{config.Host}:{config.Port}";
            using var channel = GrpcChannel.ForAddress(requestUrl);
            var client =  new kaspawalletd.kaspawalletdClient(channel);

            var req = new GetBalanceRequest();

            logger.Trace(() => $"Sending gRPC request to wallet: {req}");

            var response = await client.GetBalanceAsync(req, null, null, ct);

            logger.Trace(() => $"Received gRPC response: {response}");

            messageBus.SendTelemetry(poolId, TelemetryCategory.RpcRequest, "GetBalanceAsync", sw.Elapsed, response != null);
            return response;
        }
        catch(TaskCanceledException)
        {
            return null;
        }
        catch(Exception ex)
        {
            return null;
        }
    }

    public async Task<ShowAddressesResponse> ShowAddressesAsync(ILogger logger, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var protocol = config.Ssl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var requestUrl = $"{protocol}://{config.Host}:{config.Port}";
            using var channel = GrpcChannel.ForAddress(requestUrl);
            var client = new kaspawalletd.kaspawalletdClient(channel);

            var req = new ShowAddressesRequest();

            logger.Trace(() => $"Sending gRPC request to wallet: {req}");

            var response = await client.ShowAddressesAsync(req, null, null, ct);

            logger.Trace(() => $"Received gRPC response: {response}");

            messageBus.SendTelemetry(poolId, TelemetryCategory.RpcRequest, "ShowAddressesAsync", sw.Elapsed, response != null);
            return response;
        }
        catch(TaskCanceledException)
        {
            return null;
        }
        catch(Exception ex)
        {
            return null;
        }
    }

    public async Task<SendResponse> SendAsync(ILogger logger, SendRequest req, CancellationToken ct, bool throwErrors = false)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var protocol = config.Ssl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var requestUrl = $"{protocol}://{config.Host}:{config.Port}";
            using var channel = GrpcChannel.ForAddress(requestUrl);
            var client = new kaspawalletd.kaspawalletdClient(channel);

            logger.Trace(() => $"Sending gRPC request to wallet: {req}");

            var response = await client.SendAsync(req, null, null, ct);

            logger.Trace(() => $"Received gRPC response: {response}");

            messageBus.SendTelemetry(poolId, TelemetryCategory.RpcRequest, "SendAsync", sw.Elapsed, response != null);
            return response;
        }
        catch(TaskCanceledException)
        {
            return null;
        }
        catch(Exception ex)
        {
            if(throwErrors)
            {
                throw ex;
            }
            return null;
        }
    }
}
