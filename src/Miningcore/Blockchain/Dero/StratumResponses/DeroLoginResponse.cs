using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.StratumResponses;

public class DeroJobParams
{
    [JsonProperty("job_id")]
    public string JobId { get; set; }

    [JsonProperty("blob")]
    public string Blob { get; set; }

    public long Height { get; set; }

    [JsonProperty("extra_nonce")]
    public string ExtraNonce { get; set; }

    [JsonProperty("pool_wallet")]
    public string PoolWallet { get; set; }

    [JsonProperty("target")]
    public string Target { get; set; }
}

public class DeroLoginResponse
{
    public string Id { get; set; }
    public DeroJobParams Job { get; set; }
}
