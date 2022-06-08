using Miningcore.Blockchain.Dero.DaemonResponses;
using Miningcore.Extensions;
using Miningcore.Util;
using Miningcore.Stratum;
using Org.BouncyCastle.Math;
using Miningcore.Crypto.Hashing.Algorithms;
using Contract = Miningcore.Contracts.Contract;
using System.Security.Cryptography;

namespace Miningcore.Blockchain.Dero;

public class DeroJob
{
    private int extraNonce;
    private readonly Astrobwt2 Astrobwt2 = new Astrobwt2();

    public DeroJob(GetBlockTemplateResponse blockTemplate)
    {
        Contract.RequiresNonNull(blockTemplate);
        BlockTemplate = blockTemplate;
        Height = blockTemplate.Height;

        byte tmp = blockTemplate.HashingBlob.HexToByteArray()[0];

        MHighDiff = (tmp & 0x10) == 0x10;
        MFinal = (tmp & 0x20) == 0x20;
        MVersion = (uint)(tmp & 0x0F);
        MPastCount = (uint)(tmp & 0xC0);
    }

    #region API-Surface

    public uint MVersion { get; }
    public bool MHighDiff { get; }
    public bool MFinal { get; }
    public uint MPastCount { get; }

    public uint Height { get; }
    public GetBlockTemplateResponse BlockTemplate { get; }

    private string EncodeTarget(double difficulty, int size = 8)
    {
        var diff = BigInteger.ValueOf((long) (difficulty * 255d));
        var quotient = DeroConstants.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
        var bytes = quotient.ToByteArray().AsSpan();
        Span<byte> padded = stackalloc byte[32];

        var padLength = padded.Length - bytes.Length;

        if(padLength > 0)
            bytes.CopyTo(padded.Slice(padLength, bytes.Length));

        padded = padded[..size];
        padded.Reverse();

        return padded.ToHexString();
    }

    private string EncodeBlob(uint workerExtraNonce)
    {
        Span<byte> blob = stackalloc byte[48];
        BlockTemplate.HashingBlob.HexToByteArray().AsSpan().Slice(0, 32).CopyTo(blob);

        var bytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
        bytes.CopyTo(blob[32..]);

        return blob.ToHexString();
    }


    public void PrepareWorkerJob(DeroWorkerJob workerJob, out string blob, out string target)
    {
        var diff = workerJob.Difficulty;

        if(MHighDiff)
        {
            diff *= 9;
        }

        workerJob.Height = BlockTemplate.Height;
        workerJob.JobId = BlockTemplate.JobId;
        workerJob.ExtraNonce = (uint) Interlocked.Increment(ref extraNonce);

        if(extraNonce < 0)
            extraNonce = 0;

        blob = EncodeBlob(workerJob.ExtraNonce);
        target = EncodeTarget(diff);

        workerJob.Blob = blob;
    }

    public (Share Share, string BlobHex) ProcessShare(DeroWorkerJob workerJob, string nonce, string workerHash, StratumConnection worker)
    {
        Contract.RequiresNonNull(workerJob);
        Contract.RequiresNonNull(nonce);
        Contract.RequiresNonNull(workerHash);

        var context = worker.ContextAs<DeroWorkerContext>();

        // validate nonce
        if(!DeroConstants.RegexValidNonce.IsMatch(nonce))
            throw new StratumException(StratumError.MinusOne, "malformed nonce");

        Span<byte> blob = stackalloc byte[48];
        workerJob.Blob.HexToByteArray().AsSpan().Slice(0, 36).CopyTo(blob);

        var bytes = nonce.HexToByteArray();

        Span<byte> padded = stackalloc byte[12];
        var padLength = padded.Length - bytes.Length;

        if(padLength > 0)
        {
            bytes.CopyTo(padded.Slice(padLength, bytes.Length));
        }
        else
        {
            bytes.CopyTo(padded);
        }

        padded = padded[..12];
        padded.CopyTo(blob[36..]);

        Span<byte> headerHash = stackalloc byte[32];
        Astrobwt2.Digest(blob, headerHash, 0);

        var blobString = blob.ToHexString();
        var headerHashString = headerHash.ToHexString();

        if (headerHashString != workerHash)
        {
            throw new StratumException(StratumError.MinusOne, "bad hash");
        }

        // check difficulty
        var blockDiff = BlockTemplate.Difficulty;
        if(MHighDiff)
        {
            blockDiff *= 9;
        }

        var headerValue = headerHash.ToBigInteger();
        var shareDiff = (double) new BigRational(DeroConstants.Diff1b, headerValue);
        var stratumDifficulty = context.Difficulty;
        var ratio = shareDiff / stratumDifficulty;
        var isBlockCandidate = shareDiff >= blockDiff;

        // test if share meets at least workers current difficulty
        if(!isBlockCandidate && ratio < 0.99)
        {
            // check if share matched the previous difficulty from before a vardiff retarget
            if(context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
            {
                ratio = shareDiff / context.PreviousDifficulty.Value;

                if(ratio < 0.99)
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                // use previous difficulty
                stratumDifficulty = context.PreviousDifficulty.Value;
            }

            else
                throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
        }

        var result = new Share
        {
            BlockHeight = BlockTemplate.Height,
        };

        if(isBlockCandidate)
        {
            result.IsBlockCandidate = true;

            // Probable block hash, will be overwritten, once submit was successful
            result.BlockHash = BlockTemplate.Blob.HexToByteArray().AsSpan().Slice(83, 32).ToHexString();
        }

        return (result, blobString);
    }

    #endregion
}
