using System.Globalization;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Math;

namespace Miningcore.Blockchain.Dero;

public enum DeroNetworkType
{
    Main = 1,
    Test
}

public static class DeroConstants
{
    public const string WalletDaemonCategory = "wallet";

    public const string DaemonRpcLocation = "json_rpc";

    public static readonly Regex RegexValidNonce = new("^[0-9a-f]{8,24}$", RegexOptions.Compiled);

    public const decimal StaticTransactionFeeReserve = 0.0025m;

    public static readonly BigInteger Diff1 = new("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 16);

    public static readonly System.Numerics.BigInteger Diff1b = System.Numerics.BigInteger.Parse("00FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);

#if DEBUG
    public const int PayoutMinBlockConfirmations = 2;
#else
        public const int PayoutMinBlockConfirmations = 20;
#endif
}

public static class DeroCommands
{
    public const string GetInfo = "DERO.GetInfo";
    public const string GetBlockTemplate = "DERO.GetBlockTemplate";
    public const string SubmitBlock = "DERO.SubmitBlock";
    public const string GetBlockHeaderByHash = "DERO.GetBlockHeaderByHash";
    public const string GetBlockHeaderByHeight = "DERO.GetBlockHeaderByTopoHeight";
    public const string GetEncryptedBalance = "DERO.GetEncryptedBalance";
}

public static class DeroWalletCommands
{
    public const string GetBalance = "GetBalance";
    public const string GetAddress = "GetAddress";
    public const string GetTransfers = "GetTransfers";
    public const string GetTransferbyTXID = "GetTransferbyTXID";
    public const string Transfer = "transfer";
}
