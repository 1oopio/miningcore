using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonRequests;

internal class GetBlockHeaderByTopoHeightRequest
{
    [JsonProperty("topoheight")]
    public UInt64 TopoHeight { get; set; }
}
