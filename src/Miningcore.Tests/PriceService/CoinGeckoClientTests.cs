using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Configuration;
using Miningcore.PriceService;
using Xunit;


namespace Miningcore.Tests.PriceService;

public class CoinGeckoClientTests : TestBase
{
    [Fact]
    public async Task GetPrice()
    {
        IPriceService client = new CoinGeckoClient(new ClusterConfig
        {
            PriceService = new PriceServiceConfig
            {
                Enabled = true,
                Service = PriceServiceKind.CoinGecko,
                VSCurrency = "usd"
            }
        });
        var price = await client.GetPrice("bitcoin");
        Assert.True(price > 0);
    }
}