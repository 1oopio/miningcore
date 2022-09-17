using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Banning;

public class IntegratedBanManager : IBanManager
{
    private static readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions
    {
        ExpirationScanFrequency = TimeSpan.FromSeconds(10)
    });

    #region Implementation of IBanManager

    public async Task<bool> IsBanned(IPAddress address)
    {
        var result = await Task<object>.Run(() => cache.Get(address.ToString()));
        return result != null;
    }

    public async Task Ban(IPAddress address, string reason, TimeSpan duration)
    {
        Contract.RequiresNonNull(address);
        Contract.Requires<ArgumentException>(duration.TotalMilliseconds > 0);

        // don't ban loopback
        if(address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback))
            return;

        await Task.Run(() => cache.Set(address.ToString(), reason, duration));
    }

    public async Task Unban(IPAddress address)
    {
        await Task.Run(() => cache.Remove(address.ToString()));
    }

    #endregion
}
