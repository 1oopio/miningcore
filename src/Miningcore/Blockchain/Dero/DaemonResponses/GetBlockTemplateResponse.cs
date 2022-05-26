using Newtonsoft.Json;
namespace Miningcore.Blockchain.Dero.DaemonResponses;

public class GetBlockTemplateResponse
{
    [JsonProperty("jobid")]
    public string JobId { get; set; }

    [JsonProperty("blocktemplate_blob")]
    public string Blob { get; set; }

    [JsonProperty("blockhashing_blob")]
    public string HashingBlob { get; set; }

    public long Difficulty { get; set; }

    [JsonProperty("difficultyuint64")]
    public UInt64 DifficultyUInt64 { get; set; }

    public uint Height { get; set; }

    [JsonProperty("prev_hash")]
    public string PreviousBlockhash { get; set; }

    [JsonProperty("epochmilli")]
    public string EpochMilli { get; set; }

    [JsonProperty("blocks")]
    public int Blocks { get; set; }

    [JsonProperty("miniblocks")]
    public int MiniBlocks { get; set; }

    [JsonProperty("lasterror")]
    public string LastError { get; set; }
    public string Status { get; set; }
}
