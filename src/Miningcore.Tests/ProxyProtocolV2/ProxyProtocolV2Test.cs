using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace Miningcore.Tests.ProxyProtocolV2;

public class ProxyProtocolV2Tests : TestBase
{
    private static readonly byte[] ExampleHeader =
    {
            0x0d, 0x0a, 0x0d, 0x0a, 0x00, 0x0d, 0x0a, 0x51, 0x55, 0x49, 0x54, 0x0a, 0x21, 0x11, 0x00, 0x54, 0x7b, 0x67,
            0xc9, 0x9a, 0xac, 0x1f, 0x0c, 0x37, 0xb1, 0x16, 0x02, 0x4b, 0x03, 0x00, 0x04, 0x08, 0x94, 0x22, 0x07, 0x04,
            0x00, 0x3e, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

    internal static byte[] GetProxyHeaderClone() => (byte[]) ExampleHeader.Clone();

    [Fact]
    public void ShouldRecogniseSignature()
    {
        Assert.True(ProxyProtocol.ProxyProtocolV2.ProxyProtocol.HasProxyProtocolSignature(ExampleHeader));
    }

    [Fact]
    public void ShouldRecogniseInvalidSignature()
    {
        var header = GetProxyHeaderClone();
        header[2] = 0x0b;
        Assert.False(ProxyProtocol.ProxyProtocolV2.ProxyProtocol.HasProxyProtocolSignature(header));
    }

    [Fact]
    public void ShouldIdentifyV2()
    {
        Assert.True(ProxyProtocol.ProxyProtocolV2.ProxyProtocol.IsProtocolV2(ExampleHeader));
    }

    [Fact]
    public void ShouldIdentifyV1()
    {
        var header = GetProxyHeaderClone();
        header[12] = 0x10;
        Assert.False(ProxyProtocol.ProxyProtocolV2.ProxyProtocol.IsProtocolV2(header));
    }

    [Fact]
    public void ShouldDetectProxyVLocal()
    {
        Assert.Equal("PROXY", ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetCommand(ExampleHeader));

        var header = GetProxyHeaderClone();
        header[12] = 0x22;
        Assert.Equal("LOCAL", ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetCommand(header));
    }

    [Fact]
    public void ShouldDetectAddressFamily()
    {
        Assert.Equal(AddressFamily.InterNetwork, ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetAddressFamily(ExampleHeader));
    }

    [Fact]
    public void ShouldReadLength()
    {
        Assert.Equal(84, ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetLength(ExampleHeader));
    }

    [Fact]
    public void ShouldReadStreamToEndOfHeader()
    {
        var stream = new MemoryStream(GetProxyHeaderClone());
        var proxy = new ProxyProtocol.ProxyProtocolV2.ProxyProtocol(stream, null);
        proxy.GetProxyProtocolHeader().Wait();

        Assert.Equal(101, stream.Position);
    }

    [Fact]
    public void CanGetSourceIP()
    {
        Assert.Equal("123.103.201.154", ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetSourceAddressIpv4(ExampleHeader).ToString());
    }

    [Fact]
    public void CanGetDestinationIP()
    {
        Assert.Equal("172.31.12.55", ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetDestinationAddressIpv4(ExampleHeader).ToString());
    }

    [Fact]
    public void CanGetSourcePort()
    {
        Assert.Equal(45334, ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetSourcePortIpv4(ExampleHeader));
    }

    [Fact]
    public void CanGetDestinationPort()
    {
        Assert.Equal(587, ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetDestinationPortIpv4(ExampleHeader));
    }

    [Fact]
    public void CanGetSourceIPv6()
    {
        var ip = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
        var headeripv6 = GetProxyHeaderClone().Take(16).Concat(ip.GetAddressBytes()).ToArray();
        Assert.True(ip.Equals(ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetSourceAddressIpv6(headeripv6)));
    }

    [Fact]
    public void CanGetDestinationIPv6()
    {
        var source = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
        var destination = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7335");
        var headeripv6 = GetProxyHeaderClone().Take(16).Concat(source.GetAddressBytes()).Concat(destination.GetAddressBytes()).ToArray();
        Assert.True(destination.Equals(ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetDestinationAddressIpv6(headeripv6)));
    }

    [Fact]
    public async Task CanGetSourcePortIPv6()
    {
        var source = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
        var destination = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7335");
        var headeripv6 = GetProxyHeaderClone()
            .Take(16)
            .Concat(source.GetAddressBytes())
            .Concat(destination.GetAddressBytes())
            .Concat(IntTo2Bytes(1000))
            .Concat(IntTo2Bytes(1001))
            .ToArray();
        headeripv6[13] = 0x21;
        var stream = new MemoryStream(headeripv6);
        var proxy = new ProxyProtocol.ProxyProtocolV2.ProxyProtocol(stream, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 123));

        var actual = await proxy.GetRemoteEndpoint();

        Assert.Equal(1000, actual.Port);
    }

    [Fact]
    public void CanGetDestinationPortIPv6()
    {
        var source = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
        var destination = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7335");
        var headeripv6 = GetProxyHeaderClone()
            .Take(16)
            .Concat(source.GetAddressBytes())
            .Concat(destination.GetAddressBytes())
            .Concat(IntTo2Bytes(1000))
            .Concat(IntTo2Bytes(1001))
            .ToArray();
        Assert.Equal(1001, ProxyProtocol.ProxyProtocolV2.ProxyProtocol.GetDestinationPortIpv6(headeripv6));
    }

    [Fact]
    public void ShouldGetSourceIpAndPort()
    {
        var stream = new MemoryStream(GetProxyHeaderClone());
        var proxy = new ProxyProtocol.ProxyProtocolV2.ProxyProtocol(stream, null);
        var endpoint = proxy.GetRemoteEndpoint().Result;

        Assert.Equal("123.103.201.154", endpoint.Address.ToString());
        Assert.Equal(45334, endpoint.Port);
    }

    [Fact]
    public void ShouldGetSourceIpAndPortForNonProxyProtocolConnections()
    {
        var header = GetProxyHeaderClone();
        header[0] = 0x00;
        var stream = new MemoryStream(header);
        var proxy = new ProxyProtocol.ProxyProtocolV2.ProxyProtocol(stream, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 123));

        var endpoint = proxy.GetRemoteEndpoint().Result;

        Assert.Equal("10.0.0.1", endpoint.Address.ToString());
        Assert.Equal(123, endpoint.Port);
    }

    [Fact]
    public void ShouldGetCorrectHeaderLength()
    {
        var stream = new MemoryStream(GetProxyHeaderClone());
        var proxy = new ProxyProtocol.ProxyProtocolV2.ProxyProtocol(stream, null);
        var length = proxy.GetProxyHeaderLength().Result;

        Assert.Equal(100, length);
    }

    private static byte[] IntTo2Bytes(int i)
    {
        return BitConverter.GetBytes(i).Take(2).Reverse().ToArray();
    }

}