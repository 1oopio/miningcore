namespace Miningcore.Blockchain.Dero.Configuration;

public class DeroPaymentProcessingConfigExtra
{
    /// <summary>
    /// Payout batch size (how many payouts are combined in one)
    /// </summary>
    public int PayoutBatchSize { get; set; } = 15;
}
