using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dynex.StratumResponses;

public class DynexJobParams
{
    [JsonProperty("job_id")]
    public string JobId { get; set; }

    public string Blob { get; set; }
    public string Target { get; set; }

    [JsonProperty("seed_hash")]
    public string SeedHash { get; set; }

    [JsonProperty("algo")]
    public string Algorithm { get; set; }

    /// <summary>
    /// Introduced for CNv4 (aka CryptonightR)
    /// </summary>
    public ulong Height { get; set; }
}

public class DynexLoginResponse : DynexResponseBase
{
    public string Id { get; set; }
    public DynexJobParams Job { get; set; }
}
