using Microsoft.Extensions.Caching.Memory;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Newtonsoft.Json;
using Org.BouncyCastle.Security;

namespace Miningcore.PriceService;

public class CoinGeckoClient : IPriceService
{
    public CoinGeckoClient(ClusterConfig clusterConfig)
    {
        config = clusterConfig?.PriceService;
        vsCurrency = config?.VSCurrency ?? "usd";
        client = new CoinGecko.Clients.CoinGeckoClient(httpClient, serializerSettings, config?.ApiKey);
        cacheTTL = TimeSpan.FromSeconds(config?.CacheTTL ?? 5);

    }
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

    private async Task<decimal?> GetPriceInternal(string symbol)
    {
        return (await client.SimpleClient.GetSimplePrice(new[] { symbol }, new[] { vsCurrency }))[symbol][vsCurrency];
    }

    #endregion
}