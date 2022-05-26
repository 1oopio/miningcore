using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonRequests;

public class GetBlockTemplateRequest
{
    /// <summary>
    /// Address of wallet to receive coinbase transactions if block is successfully mined.
    /// </summary>
    [JsonProperty("wallet_address")]
    public string WalletAddress { get; set; }

    /// <summary>
    /// Include block optional
    /// </summary>
    [JsonProperty("block")]
    public bool Block { get; set; }

    /// <summary>
    /// Miner in JobId optional
    /// </summary>
    [JsonProperty("miner")]
    public string Miner { get; set; }
}
