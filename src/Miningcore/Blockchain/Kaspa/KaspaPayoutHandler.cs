using Autofac;
using AutoMapper;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using Miningcore.Util;
using Miningcore.Blockchain.Kaspa.RPC.Wallet;
using Miningcore.Blockchain.Kaspa.RPC.Messages;
using Miningcore.Blockchain.Kaspa.RPC;
using Miningcore.Blockchain.Kaspa.Configuration;
using System.Collections;
using Block = Miningcore.Persistence.Model.Block;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Kaspa;

[CoinFamily(CoinFamily.Kaspa)]
public class KaspaPayoutHandler : PayoutHandlerBase,
    IPayoutHandler
{
    public KaspaPayoutHandler(
        IComponentContext ctx,
        IConnectionFactory cf,
        IMapper mapper,
        IShareRepository shareRepo,
        IBlockRepository blockRepo,
        IBalanceRepository balanceRepo,
        IPaymentRepository paymentRepo,
        IMasterClock clock,
        IMessageBus messageBus) :
        base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(balanceRepo);
        Contract.RequiresNonNull(paymentRepo);

        this.ctx = ctx;
    }

    protected readonly IComponentContext ctx;
    private KaspaGrpcRPCClient grpc;
    private KaspaGrpcWalletClient grpcWallet;
    private KaspaNetworkType? networkType;
    private KaspaPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;
    private List<String> usedChilds = new();
    protected readonly object childLock = new();

    protected override string LogCategory => "Kaspa Payout Handler";

    private string ConvertToAddress(string prefix, byte[] bytes, uint version)
    {
        string charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        int checksumLength = 8;
        long[] generator = { 0x98f2bc8e61, 0x79b76d99e2, 0xf33e5fb3c4, 0xae2eabe2a8, 0x1e4f43e470 };

        byte[] data = new byte[1 + bytes.Length];
        data[0] = (byte) version;
        bytes.CopyTo(data, 1);

        // Convert source bytes
        ArrayList regrouped = new ArrayList();
        byte toBits = 5;
        byte nextByte = 0;
        byte filledBits = 0;
        foreach(var dataByte in data)
        {
            byte b = dataByte;
            byte remainingFromBits = 8;
            while(remainingFromBits > 0)
            {
                byte remainingToBits = (byte) (toBits - filledBits);
                byte toExtract = remainingFromBits;
                if(remainingToBits < toExtract)
                {
                    toExtract = remainingToBits;
                }

                nextByte = (byte) ((nextByte << toExtract) | (b >> (8 - toExtract)));

                b = (byte) (b << toExtract);

                remainingFromBits -= toExtract;

                filledBits += toExtract;

                if(filledBits == toBits)
                {
                    regrouped.Add(nextByte);
                    filledBits = 0;
                    nextByte = 0;
                }
            };
        }

        if(filledBits > 0)
        {
            nextByte = (byte) (nextByte << (toBits - filledBits));
            regrouped.Add(nextByte);
        }

        // Create checksum
        int[] prefixLower5Bits = new int[prefix.Length];
        for(var i = 0; i < prefix.Length; i++)
        {
            char c = prefix[i];
            prefixLower5Bits[i] = (int) (c & 31);
        }
        int[] templateZeroes = { 0, 0, 0, 0, 0, 0, 0, 0 };

        ArrayList payloadInts = new ArrayList();
        foreach(var b in regrouped.OfType<byte>())
        {
            payloadInts.Add((int) b);
        }
        ArrayList concat = new ArrayList();
        concat.AddRange(prefixLower5Bits);
        concat.Add(0);
        concat.AddRange(payloadInts);
        concat.AddRange(templateZeroes);

        long polyModResult = 1;
        foreach(var value in concat)
        {
            long topBits = polyModResult >> 35;
            polyModResult = (((polyModResult & 0x07ffffffff) << 5) ^ (int) (value));
            for(var i = 0; i < generator.Length; i++)
            {
                if(((topBits >> (int) (i)) & 1) == 1)
                {
                    polyModResult ^= generator[i];
                }
            }
        }
        polyModResult ^= 1;

        ArrayList res = new ArrayList();
        for(var i = 0; i < checksumLength; i++)
        {
            res.Add((byte) ((polyModResult >> (int) ((uint) (5 * (checksumLength - 1 - i)))) & 31));
        }

        // Build Address
        ArrayList addressData = new ArrayList();
        addressData.AddRange(regrouped);
        addressData.AddRange(res);
        string address = "";
        for(var i = 0; i < addressData.Count; i++)
        {
            if((byte) addressData[i] >= charset.Length)
            {
                return "";
            }
            address += charset[(byte) addressData[i]];
        }

        return prefix + ":" + address;
    }

    private string ConvertToAddress(byte[] bytes)
    {
        var prefix = "";
        if(networkType == KaspaNetworkType.Main)
        {
            prefix = "kaspa";
        }
        else if(networkType == KaspaNetworkType.Dev)
        {
            prefix = "kaspadev";
        }
        else if(networkType == KaspaNetworkType.Test)
        {
            prefix = "kaspatest";
        }
        if(bytes[0] == 0xaa && bytes[1] <= 0x76)
        {
            return ConvertToAddress(prefix, bytes[2..(2 + bytes[1])], 0x08);
        }
        else if(bytes[0] < 0x76)
        {
            return ConvertToAddress(prefix, bytes[1..(1 + bytes[0])], 0x00);
        }
        return "";
    }

    private async Task UpdateNetworkTypeAsync(CancellationToken ct)
    {
        if(!networkType.HasValue)
        {
            var request = new KaspadMessage();
            request.GetCurrentNetworkRequest = new GetCurrentNetworkRequestMessage();

            var response = await grpc.ExecuteAsync(logger, request, ct);

            if(response == null || response.GetCurrentNetworkResponse == null || response.GetCurrentNetworkResponse.CurrentNetwork == null)
                throw new PoolStartupException($"{LogCategory}] Unable to determine network type", poolConfig.Id);

            // chain detection
            if(!string.IsNullOrEmpty(response.GetCurrentNetworkResponse.CurrentNetwork))
            {
                switch(response.GetCurrentNetworkResponse.CurrentNetwork.ToLower())
                {
                    case "mainnet":
                        networkType = KaspaNetworkType.Main;
                        break;
                    case "devnet":
                        networkType = KaspaNetworkType.Dev;
                        break;
                    case "testnet":
                        networkType = KaspaNetworkType.Test;
                        break;
                    default:
                        throw new PoolStartupException($"Unsupport net type '{response.GetCurrentNetworkResponse.CurrentNetwork}'", poolConfig.Id);
                }
            }
        }
    }

    private async Task<KaspadMessage> GetBlockAsync(NLog.ILogger logger, string hash, CancellationToken ct)
    {
        var request = new KaspadMessage();
        request.GetBlockRequest = new GetBlockRequestMessage();
        request.GetBlockRequest.Hash = hash;
        request.GetBlockRequest.IncludeTransactions = true;
        var response = await grpc.ExecuteAsync(logger, request, ct);

        if (response == null || response.GetBlockResponse == null)
        {
            throw new Exception($"No result from node for get block: {hash}");
        }

        if (response.GetBlockResponse.Error != null && response.GetBlockResponse.Error.Message != null)
        {
            throw new Exception($"Got error from node: {response.GetBlockResponse.Error.Message}");
        }

        return response;
    }
    
    private async Task<KaspadMessage> GetBlocksAsync(NLog.ILogger logger, string hash, CancellationToken ct)
    {
        var request = new KaspadMessage();
        request.GetBlocksRequest = new GetBlocksRequestMessage();
        request.GetBlocksRequest.LowHash = hash;
        request.GetBlocksRequest.IncludeBlocks = false;
        request.GetBlocksRequest.IncludeTransactions = false;
        var response = await grpc.ExecuteAsync(logger, request, ct);

        if (response == null || response.GetBlocksResponse == null)
        {
            throw new Exception($"No result from node for get blocks: {hash}");
        }

        if (response.GetBlocksResponse.Error != null && response.GetBlocksResponse.Error.Message != null)
        {
            throw new Exception($"Got error from node: {response.GetBlocksResponse.Error.Message}");
        }

        return response;
    }


    #region IPayoutHandler

    public virtual async Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        Contract.RequiresNonNull(pc);

        logger = LogUtil.GetPoolScopedLogger(typeof(KaspaPayoutHandler), pc);

        poolConfig = pc;
        clusterConfig = cc;
        extraPoolPaymentProcessingConfig = pc.PaymentProcessing.Extra.SafeExtensionDataAs<KaspaPaymentProcessingConfigExtra>();

        var daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .ToArray();

        grpc = new KaspaGrpcRPCClient(daemonEndpoints.First(), messageBus, poolConfig.Id);

        // configure wallet daemon
        var walletDaemonEndpoints = pc.Daemons
            .Where(x => x.Category?.ToLower() == KaspaConstants.WalletDaemonCategory)
            .ToArray();

        grpcWallet = new KaspaGrpcWalletClient(walletDaemonEndpoints.First(), messageBus, poolConfig.Id);

        // detect network
        await UpdateNetworkTypeAsync(ct);

    }

    public virtual async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(blocks);

        if(blocks.Length == 0)
            return blocks;

        var coin = poolConfig.Template.As<KaspaCoinTemplate>();
        var pageSize = 100;
        var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
        var result = new List<Block>();

        for(var i = 0; i < pageCount; i++)
        {
            // get a page full of blocks
            var page = blocks
                .Skip(i * pageSize)
                .Take(pageSize)
                .ToArray();

            // fetch full blocks for blocks in page
            var blockBatch = page.Select(block => GetBlockAsync(logger, block.Hash, ct)).ToArray();

            await Guard(() => Task.WhenAll(blockBatch),
                ex => logger.Debug(ex));

            for(var j = 0; j < page.Length; j++)
            {
                var block = page[j];
                var blockTask = blockBatch[j];

                if(!blockTask.IsCompletedSuccessfully)
                {
                    if(blockTask.IsFaulted)
                        logger.Warn(() => $"Failed to fetch block {block.BlockHeight}: {blockTask.Exception?.InnerException?.Message ?? blockTask.Exception?.Message}");
                    else
                        logger.Warn(() => $"Failed to fetch block {block.BlockHeight}: {blockTask.Status.ToString().ToLower()}");

                    continue;
                }

                var fullBlock = blockTask.Result.GetBlockResponse.Block;
                var childBlocks = await GetBlocksAsync(logger, block.Hash, ct);
                var confirms = childBlocks.GetBlocksResponse.BlockHashes.Count() - 1;

                // update progress
                block.ConfirmationProgress = Math.Min(1.0d, (double) confirms / KaspaConstants.PayoutMinBlockConfirmations);

                // reset block reward
                block.Reward = 0;
                decimal blockReward = 0;
                var foundChild = false;

                foreach(var childBlockHash in childBlocks.GetBlocksResponse.BlockHashes)
                {
                    var alreadyUsed = false;
                    lock(childLock)
                    {
                        alreadyUsed = usedChilds.Contains(childBlockHash);
                    }

                    if(childBlockHash != block.Hash && !alreadyUsed)
                    {
                        lock(childLock)
                        {
                            usedChilds.Insert(0, childBlockHash);

                            // trim active jobs
                            while(usedChilds.Count > KaspaConstants.PayoutMaxRewardCheckChilds)
                                usedChilds.RemoveAt(usedChilds.Count - 1);
                        }

                        var childBlockResult = await GetBlockAsync(logger, childBlockHash, ct);
                        var childBlock = childBlockResult.GetBlockResponse.Block;

                        if(childBlock.VerboseData.Hash == childBlockHash)
                        {
                            if(childBlock.VerboseData.IsChainBlock)
                            {
                                if(childBlock.Transactions.Count() > 0)
                                {
                                    var tx = childBlock.Transactions[0];
                                    foreach(var output in tx.Outputs)
                                    {
                                        if(output.VerboseData != null && output.VerboseData.ScriptPublicKeyAddress.ToLower() == poolConfig?.Address.ToLower())
                                        {
                                            blockReward += output.Amount;
                                        }
                                        else if(output.ScriptPublicKey != null && ConvertToAddress(output.ScriptPublicKey.ScriptPublicKey.HexToByteArray()).ToLower() == poolConfig?.Address.ToLower())
                                        {
                                            blockReward += output.Amount;
                                        }
                                    }
                                    if(blockReward > 0)
                                    {
                                        foundChild = true;
                                        break;
                                    }
                                }
                                else
                                {
                                    throw new Exception($"Expecting at least one transaction within block {childBlockHash}, but got none (pruned?)");
                                }
                            }
                        }
                    }
                    if(foundChild)
                    {
                        break;
                    }
                }


                block.Reward = blockReward / KaspaConstants.SmallestUnit;

                if(confirms >= KaspaConstants.PayoutMinBlockConfirmations)
                {
                    block.Status = BlockStatus.Orphaned;

                    // We only mark the block as confirmed, if the block has an reward
                    if (block.Reward > 0)
                    {
                        block.Status = BlockStatus.Confirmed;
                        block.ConfirmationProgress = 1;
                    }
                }

                result.Add(block);

                if(block.Status == BlockStatus.Confirmed)
                {
                    logger.Info(() => $"[{LogCategory}] Unlocked block {block.Hash} worth {FormatAmount(block.Reward)}");

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                }

                else
                    messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);
            }
        }

        return result.ToArray();
    }

    public virtual async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        Contract.RequiresNonNull(balances);

        // build args
        var amounts = balances
            .Where(x => x.Amount > 0)
            .ToDictionary(x => x.Address, x => Math.Round(x.Amount, 4));

        if(amounts.Count == 0)
            return;

        var balancesTotal = amounts.Sum(x => x.Value);
        var walletTotalResponse = await grpcWallet.GetBalanceAsync(logger, ct);
        var balanceAvailable = (walletTotalResponse?.Available ?? 0) / KaspaConstants.SmallestUnit;
        if (walletTotalResponse == null || balanceAvailable < balancesTotal)
        {
            NotifyPayoutFailure(poolConfig.Id, balances, $"Error with wallet balance {balanceAvailable} vs requested {balancesTotal}", null);
            return;
        }

        logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

        foreach(var balance in balances)
        {
            var balanceAsArray = new List<Balance> { balance }.ToArray();
            var req = new SendRequest();
            req.ToAddress = balance.Address;
            req.Amount = (ulong)(balance.Amount * KaspaConstants.SmallestUnit);
            req.Password = extraPoolPaymentProcessingConfig.WalletPassword ?? String.Empty;

            try
            {
                var sendResponse = await grpcWallet.SendAsync(logger, req, ct, true);
                if(sendResponse == null)
                {
                    NotifyPayoutFailure(poolConfig.Id, balanceAsArray, $"Error sending {balance.Amount} to {balance.Address}", null);
                }
                else
                {
                    // payment successful
                    logger.Info(() => $"[{LogCategory}] Payment transaction id: {sendResponse.TxIDs.ToArray<String>()}");

                    await PersistPaymentsAsync(balanceAsArray, string.Join(",", sendResponse.TxIDs.ToArray<String>()));

                    NotifyPayoutSuccess(poolConfig.Id, balanceAsArray, sendResponse.TxIDs.ToArray<String>(), 0);
                }
            }
            catch(Exception ex)
            {
                NotifyPayoutFailure(poolConfig.Id, balanceAsArray, $"Error sending {balance.Amount} to {balance.Address}", ex);
            }
        }
    }

    #endregion // IPayoutHandler
}
