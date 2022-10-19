using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Newtonsoft.Json;

namespace Miningcore.PriceService;

public class CoinGeckoClient : IPriceService
{
    public CoinGeckoClient(ClusterConfig clusterConfig, IMessageBus messageBus)
    {
        config = clusterConfig?.PriceService;
        vsCurrency = config?.VSCurrency ?? "usd";
        client = new CoinGecko.Clients.CoinGeckoClient(httpClient, serializerSettings, config?.ApiKey);
        cacheTTL = TimeSpan.FromSeconds(config?.CacheTTL ?? 5);

        this.messageBus = messageBus;
    }
    private readonly IMessageBus messageBus;
    private readonly string vsCurrency;
    private readonly PriceServiceConfig config;
    private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings();
    private static HttpClient httpClient = new HttpClient();
    private static CoinGecko.Clients.CoinGeckoClient client;
    private readonly TimeSpan cacheTTL;
    private static readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions
    {
        ExpirationScanFrequency = TimeSpan.FromSeconds(2)
    });

    #region Implementation of IPriceService

    public async Task<decimal?> GetPrice(string symbol)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(symbol));

        if(config?.CacheTTL == 0)
            return await GetPriceInternal(symbol);

        var price = cache.Get<decimal?>(symbol);

        if(price.IsNullOrValue(0))
        {
            price = await GetPriceInternal(symbol);
            cache.Set(symbol, price, cacheTTL);
        }

        return price;
    }

    #endregion

    private async Task<decimal?> GetPriceInternal(string symbol)
    {
        var sw = Stopwatch.StartNew();

        var price = (await client.SimpleClient.GetSimplePrice(new[] { symbol }, new[] { vsCurrency }))[symbol][vsCurrency];

        messageBus?.SendTelemetry(symbol, TelemetryCategory.PriceServiceRequest, PriceServiceKind.CoinGecko.ToString(), sw.Elapsed, null, null, null);

        return price;
    }
}