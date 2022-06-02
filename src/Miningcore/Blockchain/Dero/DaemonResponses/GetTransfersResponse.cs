using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonResponses;

public class GetTransfersEntry
{
    public UInt64 Height { get; set; }

    public uint Status { get; set; }


    [JsonProperty("blockhash")]
    public string BlockHash { get; set; }

    [JsonProperty("minerreward")]
    public ulong MinerReward { get; set; }

    [JsonProperty("amount")]
    public ulong Amount { get; set; }
    
    public bool Coinbase { get; set; }
}

public class GetTransfersResponse
{
    [JsonProperty("entries")]
    public GetTransfersEntry[] Entries { get; set; }
}

