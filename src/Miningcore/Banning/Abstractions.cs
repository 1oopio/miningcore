using System.Net;

namespace Miningcore.Banning;

public static class BanReason
{
    public static readonly string Junk = "Banned for sending junk";
    public static readonly string InvalidShares = "Banned for sending invalid shares";
    public static readonly string InvalidTLSHandshake = "Banned for invalid TLS handshake";
    public static readonly string Unauthorized = "Banned unauthorized worker";
}

public interface IBanManager
{
    Task<bool> IsBanned(IPAddress address);
    Task Ban(IPAddress address, string reason, TimeSpan duration);
    Task Unban(IPAddress address);
}
