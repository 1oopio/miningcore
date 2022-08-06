using Miningcore.Extensions;
using System.Text;
using Miningcore.Crypto.Hashing.Algorithms;
using Contract = Miningcore.Contracts.Contract;
using Miningcore.Blockchain.Kaspa.RPC;
using Miningcore.Stratum;
using BigInteger = System.Numerics.BigInteger;

namespace Miningcore.Blockchain.Kaspa;

public class KaspaJob
{
    public KaspaJob(RpcBlock block, string jobId, string prevHash)
    {
        Contract.RequiresNonNull(block);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId));

        Block = block;
        PrevHash = prevHash;
        JobId = jobId;
    }

    private static readonly Blake2b hasher = new Blake2b();

    #region API-Surface


    public string PrevHash { get; }
    public RpcBlock Block { get; }

    public string JobId { get; protected set; }

    public void PrepareWorkerJob(KaspaWorkerJob workerJob, out string hash, out BigInteger[] jobs, out long timestamp)
    {
        workerJob.Id = JobId;
        hash = PrevHash;
        jobs = JobData(PrevHash);
        timestamp = Block.Header.Timestamp;
    }

    public static string HashBlock(RpcBlock block, Boolean prePow)
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

        return hash.ToHexString();
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

    public Double EncodeTarget()
    {
        var bits = new BigInteger(Block.Header.Bits);
        Int32 unshiftedExpt = (Int32) (bits >> 24);
        var mant = bits & 1850408; //FFFFFF
        Int32 expt = 0;

        if (unshiftedExpt <= new BigInteger(3))
        {
            mant = mant >> (8 * (3 - unshiftedExpt));
            expt = 0;
        } else
        {
            expt = (Int32) (8 * ((bits >> 24) - 3));
        }
        return (Math.Pow(2, 255) / ((double)(mant << expt))) / Math.Pow(2, 31);
    }

    public (Share Share, RpcBlock block) ProcessShare(string nonce, StratumConnection worker)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce));

        var block = Block.Clone();
        block.Header.Nonce = (ulong) nonce.HexToByteArray().AsSpan().ToBigInteger();

        var isBlockCandidate = true; // TODO

        var result = new Share
        {
            BlockHeight = (long)block.Header.BlueScore,
            Difficulty = 1, // TODO
        };

        if(isBlockCandidate)
        {
            // Fill in block-relevant fields
            result.IsBlockCandidate = true;
            result.BlockHash = HashBlock(block, false);
        }

        return (result, block);

    }

        #endregion // API-Surface
    }
