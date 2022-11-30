using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Miningcore.ProxyProtocol.ProxyProtocolV2;
using Xunit;

namespace Miningcore.Tests.ProxyProtocolV2;

public class ProxyProtocolV2StreamTests : TestBase
{

    private CopyingMemoryStream memoryStream = new();

    [Fact]
    public async Task CanOpenProxyStream()
    {
        var stream = new MemoryStream(ProxyProtocolV2Tests.GetProxyHeaderClone());
        IPEndPoint ipEndPoint = null;
        var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

        await proxy.ReadAsync(new byte[200], CancellationToken.None);

        Assert.Equal("123.103.201.154", ipEndPoint.Address.ToString());
    }

    [Fact]
    public async Task CanOpenNonProxyStream()
    {
        var header = ProxyProtocolV2Tests.GetProxyHeaderClone();
        header[0] = 0xFF;
        var stream = new MemoryStream(header);
        IPEndPoint ipEndPoint = null;
        var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

        var result = new byte[200];
        var bytesRead = await proxy.ReadAsync(result, CancellationToken.None);

        Assert.Equal("192.0.2.1", ipEndPoint.Address.ToString());
        Assert.Equal(header.Length, bytesRead);
        for(var i = 0; i < header.Length; i++)
        {
            Assert.Equal(header[i], result[i]);
        }
    }

    [Fact]
    public async Task BytesReadIsCorrect()
    {
        var stream = new MemoryStream(ProxyProtocolV2Tests.GetProxyHeaderClone());
        IPEndPoint ipEndPoint = null;
        var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

        var bytesRead = await proxy.ReadAsync(new byte[200], CancellationToken.None);

        Assert.Equal(1, bytesRead);
    }

    [Fact]
    public async Task BytesReadIsCorrectForNonProxyStream()
    {
        var header = ProxyProtocolV2Tests.GetProxyHeaderClone();
        header[0] = 0xFF;
        var stream = new MemoryStream(header);
        IPEndPoint ipEndPoint = null;
        var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

        var bytesRead = await proxy.ReadAsync(new byte[200], CancellationToken.None);

        Assert.Equal(101, bytesRead);
    }

    [Fact]
    public async Task BytesReturnedAreCorrect()
    {
        var header = ProxyProtocolV2Tests.GetProxyHeaderClone();
        header[100] = 0xFF;
        var stream = new MemoryStream(header);
        IPEndPoint ipEndPoint = null;
        var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

        var read = new byte[200];
        await proxy.ReadAsync(read, CancellationToken.None);

        Assert.Equal(0xFF, read[0]);
    }

    [Fact]
    public async Task CanReadBytesAfterSignature()
    {
        var header = ProxyProtocolV2Tests.GetProxyHeaderClone();
        header[0] = 0xFF;
        var stream = new MemoryStream(header);
        IPEndPoint ipEndPoint = null;
        var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

        var bytesRead = await proxy.ReadAsync(new byte[16], CancellationToken.None);

        Assert.Equal(16, bytesRead);

        bytesRead = await proxy.ReadAsync(new byte[10], CancellationToken.None);

        Assert.Equal(10, bytesRead);
    }

    [Fact]
    public void CanReadSynchronously()
    {
        var stream = new MemoryStream(ProxyProtocolV2Tests.GetProxyHeaderClone());
        IPEndPoint ipEndPoint = null;
        var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), proxy => ipEndPoint = proxy.GetRemoteEndpoint().Result);

        var bytesRead = proxy.Read(new byte[200], 0, 200);

        Assert.Equal(1, bytesRead);
    }

    [Fact]
    public async Task ShouldWorkWithNonProxyConnections()
    {
        var sut = new ProxyProtocolStream(this.memoryStream, IPEndPoint.Parse("192.0.2.1"), _ => { });
        var reader = new StreamReader(sut);
        var writer = new StreamWriter(sut);
        writer.NewLine = "\r\n"; // ensure \r\n is used on all platforms

        await writer.WriteLineAsync("test");
        await writer.FlushAsync();
        Assert.Equal("test\r\n", await this.memoryStream.ReadLine());

        var result = reader.ReadLineAsync();
        this.memoryStream.SendLine("finished");

        Assert.Equal("finished", result.Result);
    }
}