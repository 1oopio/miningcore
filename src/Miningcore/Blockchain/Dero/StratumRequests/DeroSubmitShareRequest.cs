using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.StratumRequests;

public class DeroSubmitShareRequest
{
    [JsonProperty("id")]
    public string WorkerId { get; set; }

    [JsonProperty("job_id")]
    public string JobId { get; set; }

    [JsonProperty("nonce")]
    public string Nonce { get; set; }

    [JsonProperty("result")]
    public string Result { get; set; }
}

