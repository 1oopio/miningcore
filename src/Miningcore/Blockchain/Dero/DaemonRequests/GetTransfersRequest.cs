using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonRequests;

public class GetTransfersRequest
{
    public bool In { get; set; }

    public bool Coinbase { get; set; }

    [JsonProperty("min_height")]
    public UInt64 MinHeight { get; set; }

    [JsonProperty("max_height")]
    public UInt64 MaxHeight { get; set; }
}
