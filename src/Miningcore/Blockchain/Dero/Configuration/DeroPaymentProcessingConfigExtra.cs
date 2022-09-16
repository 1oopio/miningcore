namespace Miningcore.Blockchain.Dero.Configuration;

public class DeroPaymentProcessingConfigExtra
{
    /// <summary>
    /// Payout batch size (how many payouts are combined in one)
    /// </summary>
    public int PayoutBatchSize { get; set; } = 15;

    /// <summary>
    /// PayoutDelay (in milliseconds)
    /// </summary>
    public int PayoutDelay { get; set; } = 20000;
}
