using System.Net;
using Miningcore.Extensions;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Banning;

public class PersistentBanManager : IBanManager
{
    public PersistentBanManager(
        IConnectionFactory cf,
        IBanManagerRepository banManagerRepo,
        IMasterClock clock)
    {
        Contract.RequiresNonNull(cf);
        Contract.RequiresNonNull(banManagerRepo);

        this.cf = cf;
        this.banManagerRepo = banManagerRepo;
        this.clock = clock;
    }

    private readonly IBanManagerRepository banManagerRepo;
    private readonly IConnectionFactory cf;
    private readonly IMasterClock clock;

    #region Implementation of IBanManager

    public async Task<bool> IsBanned(IPAddress address)
    {
        Contract.RequiresNonNull(address);

        return await cf.Run(async con => await banManagerRepo.IsBanned(con, null, address.ToString()));

    }

    public async Task Ban(IPAddress address, string reason, TimeSpan duration)
    {
        Contract.RequiresNonNull(address);
        Contract.Requires<ArgumentException>(duration.TotalMilliseconds > 0);

        // don't ban loopback
        if(address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback))
            return;

        await cf.Run(async con => await banManagerRepo.BanAsync(con, null, address.ToString(), reason, clock.Now.Add(duration)));
    }

    public async Task Unban(IPAddress address)
    {
        Contract.RequiresNonNull(address);

        await cf.Run(async con => await banManagerRepo.RemoveBanAsync(con, null, address.ToString()));
    }

    #endregion
}
