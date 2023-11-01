using System.Globalization;
using System.Numerics;

namespace Miningcore.Blockchain.Kaspa;

public enum KaspaNetworkType
{
    Main = 1,
    Test,
    Dev
}

public static class KaspaConstants
{
    public const string WalletDaemonCategory = "wallet";
    public static readonly double Pow2x32 = Math.Pow(2, 32);
    public static readonly BigInteger Diff1 = BigInteger.Parse("00ffff0000000000000000000000000000000000000000000000000000", NumberStyles.HexNumber);
    public const decimal SmallestUnit = 100000000;
    public const int PayoutMinBlockConfirmations = 60;
    public const int PayoutMaxRewardCheckChilds = 500;
    public const int MaxActiveJobs = 99;
}
