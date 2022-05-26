using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonResponses;

public class TransferResponse
{
    [JsonProperty("txid")]
    public string TxId { get; set; }
}
