namespace Miningcore.PriceService;


public interface IPriceService
{
    Task<decimal?> GetPrice(string symbol);
}