using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dynex.StratumRequests;

public class DynexGetJobRequest
{
    [JsonProperty("id")]
    public string WorkerId { get; set; }
}
