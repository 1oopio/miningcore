using System;
using System.Linq;
using System.Text;
using Miningcore.Crypto.Hashing.Algorithms;
using Miningcore.Extensions;
using Xunit;
using static Miningcore.Native.Cryptonight.Algorithm;
using NexaPow = Miningcore.Native.NexaPow;

namespace Miningcore.Tests.Crypto;

public class NexaPowTests : TestBase
{
    [Fact]
    public unsafe void NexaPow_sign()
    {
        var ctx = NexaPow.schnorr_init();
        Assert.NotNull(ctx);

        var prvKey = "12b004fff7f4b69ef8650e767f18f11ede158148b425660723b9f9a66e61f747".HexToByteArray();

        var msg = Encoding.UTF8.GetBytes("Very deterministic message");
        var dsha = new Sha256D();
        var msgHash = new byte[32];
        dsha.Digest(msg, msgHash);

        Assert.Equal("5255683da567900bfd3e786ed8836a4e7763c221bf1ac20ece2a5171b9199e8a", msgHash.ToHexString());

        var sig = new byte[64];
        int success;
        fixed(byte* input = msgHash)
        {
            fixed(byte* output = sig)
            {
                // Use miningHash as a private key (POW fails if it is invalid)
                fixed(byte* key = prvKey)
                {
                    // Sign h1 resulting in sig
                    success = NexaPow.schnorr_sign(ctx, input, output, key, (uint) msgHash.Length);
                }
            }
        }

        Assert.Equal(1, success);
        Assert.Equal("2c56731ac2f7a7e7f11518fc7722a166b02438924ca9d8b4d111347b81d0717571846de67ad3d913a8fdf9d8f3f73161a4c48ae81cb183b214765feb86e255ce", sig.ToHexString());
    }
}
