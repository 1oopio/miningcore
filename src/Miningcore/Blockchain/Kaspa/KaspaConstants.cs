namespace Miningcore.Blockchain.Kaspa;

public enum KaspaNetworkType
{
    Main = 1,
    Test,
    Dev
}

public static class KaspaConstants
{
#if DEBUG
    public const int PayoutMinBlockConfirmations = 2;
#else
        public const int PayoutMinBlockConfirmations = 60;
#endif

}
