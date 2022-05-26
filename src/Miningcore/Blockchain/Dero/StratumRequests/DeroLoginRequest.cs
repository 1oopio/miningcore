using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.StratumRequests;

public class DeroLoginRequest
{
    [JsonProperty("login")]
    public string Login { get; set; }

    [JsonProperty("pass")]
    public string Password { get; set; }

    [JsonProperty("agent")]
    public string UserAgent { get; set; }
}
