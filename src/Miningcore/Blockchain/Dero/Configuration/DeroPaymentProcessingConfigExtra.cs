namespace Miningcore.Blockchain.Dero.Configuration;

public class DeroPaymentProcessingConfigExtra
{
    /// <summary>
    /// Payout batch size (how many payouts are combined in one)
    /// </summary>
    public int PayoutBatchSize { get; set; } = 15;

    /// <summary>
    /// Ringsize to use for transfers
    /// </summary>
    public uint RingSize { get; set; } = 16;

    /// <summary>
    /// PayoutDelay (in milliseconds)
    /// </summary>
    public int PayoutDelay { get; set; } = 20000;
}
