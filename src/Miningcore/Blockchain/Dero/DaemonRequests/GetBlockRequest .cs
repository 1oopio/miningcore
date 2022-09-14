using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonRequests;

internal class GetBlockRequest
{
    [JsonProperty("height")]
    public UInt64 Height { get; set; }
}
