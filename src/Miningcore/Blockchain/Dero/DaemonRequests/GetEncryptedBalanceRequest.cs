using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonRequests;

public class GetEncryptedBalanceRequest
{
    /// <summary>
    /// Address to check
    /// </summary>
    [JsonProperty("address")]
    public string Address { get; set; }

    /// <summary>
    /// Height
    /// </summary>
    [JsonProperty("topoheight")]
    public int TopoHeight { get; set; } = - 1;
}
