using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonResponses;

public class GetBalanceResponse
{
    public decimal Balance { get; set; }

    [JsonProperty("unlocked_balance")]
    public decimal UnlockedBalance { get; set; }
}
