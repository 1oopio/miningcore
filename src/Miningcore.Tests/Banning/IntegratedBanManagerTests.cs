using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Banning;
using Miningcore.Configuration;
using Xunit;

namespace Miningcore.Tests.Banning;

public class IntegratedBanManagerTests : TestBase
{
    private static readonly IPAddress address = IPAddress.Parse("192.168.1.1");

    [Fact]
    public async Task Ban_Valid_Address()
    {
        var manager = ModuleInitializer.Container.ResolveKeyed<IBanManager>(BanManagerKind.Integrated);

        Assert.False(await manager.IsBanned(address));
        await manager.Ban(address, "test ban", TimeSpan.FromSeconds(1));
        Assert.True(await manager.IsBanned(address));

        // let it expire
        Thread.Sleep(TimeSpan.FromSeconds(2));
        Assert.False(await manager.IsBanned(address));
    }

    [Fact]
    public async Task Throw_Invalid_Duration()
    {
        var manager = ModuleInitializer.Container.ResolveKeyed<IBanManager>(BanManagerKind.Integrated);

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await manager.Ban(address, "test ban", TimeSpan.Zero));
    }

    [Fact]
    public async Task Dont_Ban_Loopback()
    {
        var manager = ModuleInitializer.Container.ResolveKeyed<IBanManager>(BanManagerKind.Integrated);

        await manager.Ban(IPAddress.Loopback, "test ban", TimeSpan.FromSeconds(1));
        Assert.False(await manager.IsBanned(address));

        await manager.Ban(IPAddress.IPv6Loopback, "test ban", TimeSpan.FromSeconds(1));
        Assert.False(await manager.IsBanned(address));
    }
}
