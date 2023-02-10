using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dynex.StratumRequests;

public class DynexLoginRequest
{
    [JsonProperty("login")]
    public string Login { get; set; }

    [JsonProperty("pass")]
    public string Password { get; set; }

    [JsonProperty("agent")]
    public string UserAgent { get; set; }
}
