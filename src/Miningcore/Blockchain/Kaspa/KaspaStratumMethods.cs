namespace Miningcore.Blockchain.Kaspa;

public class KaspaStratumMethods
{
    /// <summary>
    /// Used to subscribe to work from a server, required before all other communication.
    /// </summary>
    public const string Subscribe = "mining.subscribe";

    /// <summary>
    /// Used to authorize a worker, required before any shares can be submitted.
    /// </summary>
    public const string Authorize = "mining.authorize";

    /// <summary>
    /// Used to push new work to the miner.  Previous work should be aborted if Clean Jobs = true!
    /// </summary>
    public const string MiningNotify = "mining.notify";

    /// <summary>
    /// Used to submit shares
    /// </summary>
    public const string SubmitShare = "mining.submit";

    /// <summary>
    /// Used to signal the miner to stop submitting shares under the new difficulty.
    /// </summary>
    public const string SetDifficulty = "mining.set_difficulty";

    /// <summary>
    /// Set extra nonce parameter for miner
    /// </summary>
    public const string SetExtranonce = "mining.set_extranonce";
    
    /// <summary>
    /// Reported hashrate 
    /// </summary>
    public const string SubmitHashrate = "mining.submit_hashrate";
}
