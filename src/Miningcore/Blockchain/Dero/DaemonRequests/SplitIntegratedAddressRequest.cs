using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonRequests;

public class SplitIntegratedAddressRequest
{
    /// <summary>
    /// Integrated Address to split
    /// </summary>
    [JsonProperty("integrated_address")]
    public string IntegratedAddress { get; set; }
}
