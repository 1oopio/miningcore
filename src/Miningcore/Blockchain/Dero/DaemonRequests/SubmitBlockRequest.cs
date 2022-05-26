using Newtonsoft.Json;

namespace Miningcore.Blockchain.Dero.DaemonRequests;

public class SubmitBlockRequest
{
    /// <summary>
    /// initial job id
    /// </summary>
    [JsonProperty("jobid")]
    public string JobId { get; set; }

    /// <summary>
    /// Include hashing blob + nonce from miner 
    /// </summary>
    [JsonProperty("mbl_blob")]
    public string HashingBlob { get; set; }
}
