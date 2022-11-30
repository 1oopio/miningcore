using System.Buffers;
using System.Net;
using Miningcore.Extensions;
using Miningcore.Stratum;
using NBitcoin;
using NLog;

namespace Miningcore.ProxyProtocol;

public class Header
{
    /// <summary>
    /// The real source IP address of the client.
    /// </summary>
    public IPAddress RemoteAddress { get; set; }

    /// <summary>
    /// The real source port of the client.
    /// </summary>
    public int RemotePort { get; set; }

    /// <summary>
    /// True if the proxy header was UNKNOWN (v1) or if proto was set to LOCAL (v2)
    /// In which case Header.RemoteAddress and Header.RemotePort will both be null.
    /// </summary>
    public bool IsLocal { get; set; }

    /// <summary>
    /// The version of the proxy protocol parsed.
    /// </summary>
    public int Version { get; set; }
}

public static class ProxyProtocol
{
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private static readonly byte[] ProxyProtocolV2Signature = { 0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A };
    private static readonly byte[] ProxyProtocolV1Signature = { 0x50, 0x52, 0x4F, 0x58, 0x59, 0x20 };
    private static readonly byte[] ProxyProtocolV1UnknownSignature = { 0x55, 0x4E, 0x4B, 0x4E, 0x4F, 0x57, 0x4E, 0x20 };
    private static readonly byte[] ProxyProtocolV1TCP4Signature = { 0x54, 0x43, 0x50, 0x34, 0x20 };
    private static readonly byte[] ProxyProtocolV1TCP6Signature = { 0x54, 0x43, 0x50, 0x36, 0x20 };
    public static async Task<Header> ReadHeader(Stream stream)
    {

        /* var header = seq.ToArray();
        Console.WriteLine($"header hex: {header.ToHexString()}");
        Console.WriteLine($"header: {header}");
        Console.WriteLine($"header utf: {header.AsString(StratumConstants.Encoding)}");
        Console.WriteLine($"header take: {header.Take(6).ToHexString()}");
        if(header.Take(13).SequenceEqual(ProxyProtocolV1Signature))
            return ParseV1Header(header);

        if(header.Take(16).SequenceEqual(ProxyProtocolV2Signature))
            return ParseV2Header(header); */
        Console.WriteLine((await stream.ReadBytesAsync(6)).ToHexString());
        Console.WriteLine((await stream.ReadBytesAsync(6)).ToHexString());
        Console.WriteLine(ProxyProtocolV1Signature.ToHexString());
        if((await stream.ReadBytesAsync(6)).SequenceEqual(ProxyProtocolV1Signature))
            return await ParseV1Header(stream);

        if((await stream.ReadBytesAsync(12)).SequenceEqual(ProxyProtocolV2Signature))
            return await ParseV2Header(stream);

        return null;
    }

    private static async Task<Header> ParseV1Header(Stream stream)
    {
        stream.Position += 6;
        if((await stream.ReadBytesAsync(ProxyProtocolV1UnknownSignature.Length)).SequenceEqual(ProxyProtocolV1UnknownSignature))
            return new Header { IsLocal = true, Version = 1 };

        if((await stream.ReadBytesAsync(ProxyProtocolV1TCP4Signature.Length)).SequenceEqual(ProxyProtocolV1TCP4Signature))
            Console.WriteLine("v1 tcp4");
        //return null;

        if((await stream.ReadBytesAsync(ProxyProtocolV1TCP6Signature.Length)).SequenceEqual(ProxyProtocolV1TCP6Signature))
            Console.WriteLine("v1 tcp6");
        //return null;

        return null;
        /* var line = seq.AsString(StratumConstants.Encoding);
        //logger.Debug(() => $"[{ConnectionId}] Received Proxy-Protocol header: {line}");
        var parts = line.Split(" ");
        var proto = parts[1];

        if(proto == "UNKNOWN")
        {
            return new Header
            {
                IsLocal = true,
                Version = 1
            };
        }

        return new Header
        {
            RemoteAddress = IPAddress.Parse(parts[2]),
            RemotePort = int.Parse(parts[4]),
            Version = 1
        }; */

    }

    private static async Task<Header> ParseV2Header(Stream stream)
    {
        return null;
    }

}