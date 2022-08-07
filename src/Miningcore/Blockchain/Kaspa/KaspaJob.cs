using Miningcore.Extensions;
using System.Text;
using Miningcore.Crypto.Hashing.Algorithms;
using Contract = Miningcore.Contracts.Contract;
using Miningcore.Blockchain.Kaspa.RPC;
using Miningcore.Stratum;
using System.Collections.Concurrent;
using BigInteger = System.Numerics.BigInteger;
using NBitcoin;
using Miningcore.Util;
using Org.BouncyCastle.Crypto.Digests;

namespace Miningcore.Blockchain.Kaspa;

public class KaspaJob
{
    public KaspaJob(RpcBlock block, string jobId, string prevHash, int extraNonceSize)
    {
        Contract.RequiresNonNull(block);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId));

        Block = block;
        PrevHash = prevHash;
        JobId = jobId;
        this.extraNonceSize = extraNonceSize;
    }

    private static readonly Blake2b hasher = new Blake2b();
    private static readonly HeavyHashKaspa heavyHasher = new HeavyHashKaspa();

    protected bool RegisterSubmit( string nonce)
    {
        var key = new StringBuilder()
              .Append(nonce)
            .ToString();

        return submissions.TryAdd(key, true);
    }

    #region API-Surface

    public string PrevHash { get; }
    public RpcBlock Block { get; }
    private readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);
    public string JobId { get; protected set; }
    private int extraNonceSize;

    public void PrepareWorkerJob(KaspaWorkerJob workerJob, out string hash, out BigInteger[] jobs, out long timestamp)
    {
        workerJob.Id = JobId;
        hash = PrevHash;
        jobs = JobData(PrevHash);
        timestamp = Block.Header.Timestamp;
    }

    public static byte[] HashBlock(RpcBlock block, Boolean prePow)
    {
        IntPtr blakeState = hasher.InitKey(32, Encoding.ASCII.GetBytes("BlockHash"));

        {
            Span<byte> data = stackalloc byte[10];
            BitConverter.GetBytes(((ushort) block.Header.Version)).CopyTo(data[0..]);
            BitConverter.GetBytes((UInt64) block.Header.Parents.Count).CopyTo(data[2..]);
            hasher.Update(blakeState, data);
        }

        foreach(var parent in block.Header.Parents)
        {
            hasher.Update(blakeState, BitConverter.GetBytes((UInt64) parent.ParentHashes.Count));
            foreach(var parentHash in parent.ParentHashes)
            {
                hasher.Update(blakeState, parentHash.HexToByteArray());
            }
        }

        hasher.Update(blakeState, block.Header.HashMerkleRoot.HexToByteArray());
        hasher.Update(blakeState, block.Header.AcceptedIdMerkleRoot.HexToByteArray());
        hasher.Update(blakeState, block.Header.UtxoCommitment.HexToByteArray());

        {
            Span<byte> data = stackalloc byte[36];
            BitConverter.GetBytes(((UInt64) (prePow ? 0 : block.Header.Timestamp))).CopyTo(data[0..]);
            BitConverter.GetBytes((UInt32) block.Header.Bits).CopyTo(data[8..]);
            BitConverter.GetBytes(((UInt64) (prePow ? 0 : block.Header.Nonce))).CopyTo(data[12..]);
            BitConverter.GetBytes(((UInt64) block.Header.DaaScore)).CopyTo(data[20..]);
            BitConverter.GetBytes(((UInt64) block.Header.BlueScore)).CopyTo(data[28..]);
            hasher.Update(blakeState, data);
        }

        var blueWork = block.Header.BlueWork;
        var parsedBlueWork = blueWork.PadLeft(blueWork.Count() + blueWork.Count() % 2, '0').HexToByteArray();

        hasher.Update(blakeState, BitConverter.GetBytes((UInt64) parsedBlueWork.Count()));
        hasher.Update(blakeState, parsedBlueWork);
                
        hasher.Update(blakeState, block.Header.PruningPoint.HexToByteArray());

        Span<byte> hash = stackalloc byte[32];
        hasher.Final(blakeState, hash);

        return hash.ToArray();
    }

    public static byte[] PowHashBlock(RpcBlock block)
    {
        var preHash = HashBlock(block, true);

        // PRE_POW_HASH || TIME || 32 zero byte padding || NONCE
        var cShakeDigest = new CShakeDigest(256, null, Encoding.ASCII.GetBytes("ProofOfWorkHash"));
        cShakeDigest.BlockUpdate(preHash, 0, 32);
        cShakeDigest.BlockUpdate(BitConverter.GetBytes((Int64) (block.Header.Timestamp)), 0, 8);
        cShakeDigest.BlockUpdate("0000000000000000000000000000000000000000000000000000000000000000".HexToByteArray(), 0, 32);
        cShakeDigest.BlockUpdate(BitConverter.GetBytes((UInt64) (block.Header.Nonce)), 0, 8);
        var powHash = new byte[32];
        cShakeDigest.DoFinal(powHash, 0, 32);

        var heavyHash = new byte[32];
        heavyHasher.Digest(powHash, heavyHash, new object[] { preHash });

        var cShakeDigest2 = new CShakeDigest(256, null, Encoding.ASCII.GetBytes("HeavyHash"));
        cShakeDigest2.BlockUpdate(heavyHash, 0, 32);
        var hashFinal = new byte[32];
        cShakeDigest2.DoFinal(hashFinal, 0, 32);

        return hashFinal;
    }

    public static BigInteger[] JobData(string hash)
    {
        var hashData = hash.HexToByteArray().AsSpan();
        List<BigInteger> preHashU64s = new List<BigInteger>();

        for(var i = 0; i < 4; i++)
        {
            var data = hashData.Slice(i * 8, 8);
            preHashU64s.Add(data.ToBigInteger());
        }

        return preHashU64s.ToArray();
    }

    public BigInteger EncodeTarget()
    {
        var bits = new BigInteger(Block.Header.Bits);
        var mant = bits & 0xFFFFFF;
        Int32 expt = (Int32) (bits >> 24);

        if (expt <= new BigInteger(3))
        {
            mant = mant >> (8 * (3 - expt));
            expt = 0;
        } else
        {
            expt = (Int32) (8 * ((bits >> 24) - 3));
        }
        return (mant << expt);
    }

    public Double DifficultyFromTargetBits()
    {
        return (Math.Pow(2, 255) / ((double) EncodeTarget())) / Math.Pow(2, 31);
    }

    public (Share Share, RpcBlock block) ProcessShare(string nonce, StratumConnection worker)
    {
        Contract.RequiresNonNull(worker);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce));

        var context = worker.ContextAs<KaspaWorkerContext>();

        // validate nonce
        if(nonce.Length != context.ExtraNonce1.Length + extraNonceSize * 2)
            throw new StratumException(StratumError.Other, "incorrect size of nonce");

        if(!nonce.StartsWith(context.ExtraNonce1))
            throw new StratumException(StratumError.Other, $"incorrect extraNonce in nonce (expected {context.ExtraNonce1}, got {nonce.Substring(0, Math.Min(nonce.Length, context.ExtraNonce1.Length))})");

        // dupe check
        if(!RegisterSubmit(nonce))
            throw new StratumException(StratumError.DuplicateShare, $"duplicate share");

        var block = Block.Clone();
        block.Header.Nonce = BitConverter.ToUInt64(nonce.HexToReverseByteArray().AsSpan());
        var powHash = PowHashBlock(block);

        var headerValue = new uint256(powHash);
        var targetValue = new uint256(EncodeTarget().ToByteArray().ToNewReverseArray().PadFront(0x00, 32).ToHexString());

        var shareDiff = (double) new BigRational(KaspaConstants.Diff1, new BigInteger(powHash));
        var stratumDifficulty = context.Difficulty;
        var ratio = shareDiff / stratumDifficulty;

        //// check if the share meets the much harder block difficulty (block candidate)
        var isBlockCandidate = headerValue <= targetValue;

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

            throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
        }

        var result = new Share
        {
            BlockHeight = (long) block.Header.BlueScore,
            NetworkDifficulty = DifficultyFromTargetBits(),
            Difficulty = stratumDifficulty
        };


        if(isBlockCandidate)
        {
            // Fill in block-relevant fields
            result.IsBlockCandidate = true;
            result.BlockHash = HashBlock(block, false).ToHexString();
        }

        return (result, block);

    }

        #endregion // API-Surface
    }
