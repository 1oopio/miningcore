namespace Miningcore.Blockchain.Dero;

public class DeroStratumMethods
{
    /// <summary>
    /// Used to subscribe to work
    /// </summary>
    public const string Login = "login";

    /// <summary>
    /// New job notification
    /// </summary>
    public const string JobNotify = "job";

    /// <summary>
    /// Submit share request
    /// </summary>
    public const string Submit = "submit";

    /// <summary>
    /// Keep alive request
    /// </summary>
    public const string KeepAlive = "keepalived";

    /// <summary>
    /// Miners reported hashrate
    /// </summary>
    public const string ReportHashrate = "reported_hashrate";
}
