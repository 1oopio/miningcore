namespace Miningcore.Blockchain.Dero.Configuration;

public class DeroPoolConfigExtra
{
    /// <summary>
    /// List of addresses which are not allowed to mine
    /// </summary>
    public string[] BlacklistedAddresses { get; set; }

    /// <summary>
    /// Maximum number of tracked jobs.
    /// Default: 12 - you should increase this value if your blockrefreshinterval is higher than 300ms
    /// </summary>
    public int? MaxActiveJobs { get; set; }
}