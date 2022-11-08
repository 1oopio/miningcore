using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

// ReSharper disable InconsistentNaming

namespace Miningcore.Blockchain.Ergo;

public static class ErgoConstants
{
    public const uint ShareMultiplier = 256;
    public const uint ShareEffortMultiplier = 16777216; // 256^3
    public static readonly double Pow2x32 = Math.Pow(2, 32);
    public static readonly BigInteger Diff1 = BigInteger.Parse("00ffff0000000000000000000000000000000000000000000000000000", NumberStyles.HexNumber);
    public const decimal SmallestUnit = 1000000000;
    public static readonly Regex RegexChain = new("ergo-([^-]+)-.+", RegexOptions.Compiled);

    public static readonly byte[] M = Enumerable.Range(0, 1024)
        .Select(x => BitConverter.GetBytes((ulong) x).Reverse())
        .SelectMany(byteArr => byteArr)
        .ToArray();
}
