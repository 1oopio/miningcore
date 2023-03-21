namespace Miningcore.Blockchain.Kaspa.Configuration;

public class KaspaPaymentProcessingConfigExtra
{
    /// <summary>
    /// Password for sending coins
    /// </summary>
    public string WalletPassword { get; set; } = $"{Environment.GetEnvironmentVariable("MC_KASPA_WALLET_PASSWORD")}";
}
