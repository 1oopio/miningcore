using System.Threading.Tasks;
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
                VSCurrency = "usd",
                CacheTTL = 0
            }
        }, null);
        var price = await client.GetPrice("bitcoin");
        Assert.NotNull(price);
        Assert.True(price > 0);
    }
}