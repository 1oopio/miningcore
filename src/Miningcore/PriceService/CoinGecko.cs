using Miningcore.Configuration;
using Miningcore.Contracts;
using Newtonsoft.Json;

namespace Miningcore.PriceService;

public class CoinGeckoClient : IPriceService
{
    public CoinGeckoClient(ClusterConfig clusterConfig)
    {
        config = clusterConfig?.PriceService;
        vsCurrency = config?.VSCurrency ?? "usd";
        client = new CoinGecko.Clients.CoinGeckoClient(httpClient, serializerSettings, config?.ApiKey);
    }
    private readonly string vsCurrency;
    private readonly PriceServiceConfig config;
    private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings();
    private static HttpClient httpClient = new HttpClient();
    private static CoinGecko.Clients.CoinGeckoClient client;

    #region Implementation of IPriceService

    public async Task<decimal?> GetPrice(string symbol)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(symbol));
        return (await client.SimpleClient.GetSimplePrice(new[] { symbol }, new[] { vsCurrency }))[symbol][vsCurrency];
    }

    #endregion
}