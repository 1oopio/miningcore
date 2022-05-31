using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonResponses;

public class SubmitResponse
{
    /// <summary>
    /// initial job id
    /// </summary>
    [JsonProperty("jobid")]
    public string JobId { get; set; }

    /// <summary>
    /// Mini block id
    /// </summary>
    [JsonProperty("mblid")]
    public string MiniBlockId { get; set; }

    /// <summary>
    /// Block id
    /// </summary>
    [JsonProperty("blid")]
    public string BlockId { get; set; }

    /// <summary>
    /// Is a mini or a real block
    /// </summary>
    public bool mini { get; set; }

    public string Status { get; set; }
}
