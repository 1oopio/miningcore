using Miningcore.Blockchain.Dero.DaemonRequests;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonResponses;

public class SplitIntegratedAddressResponse
{
    public string Address { get; set; }

    [JsonProperty("payload_rpc")]
    public PayloadRpc[] PayloadRpc { get; set; }
}
