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
using Miningcore.Blockchain.Kaspa.RPC.Messages;
using Miningcore.Blockchain.Kaspa.RPC;
using Block = Miningcore.Persistence.Model.Block;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;
using BigInteger = System.Numerics.BigInteger;

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

    protected override string LogCategory => "Kaspa Payout Handler";

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
        var response = await grpc.ExecuteAsync(logger, request, ct, true);

        if (response == null || response.GetBlockResponse == null)
        {
            throw new Exception($"No result from node");
        }

        if (response.GetBlockResponse.Error != null && response.GetBlockResponse.Error.Message != null)
        {
            throw new Exception($"Got error from node: {response.GetBlockResponse.Error.Message}");
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

                // reset block reward
                block.Reward = 0;
                decimal blockReward = 0;

                if(fullBlock.Transactions.Count >= 1)
                {
                    // Miner rewards are always first transaction
                    var tx = fullBlock.Transactions[0];
                    if(tx?.VerboseData?.TransactionId != null)
                    {
                        block.TransactionConfirmationData = tx.VerboseData.TransactionId;
                    }
                    foreach(var output in tx.Outputs)
                    {
                        if(output.VerboseData.ScriptPublicKeyAddress.ToLower() == poolConfig?.Address.ToLower())
                        {
                            blockReward += output.Amount;
                        }
                    }
                }

                block.Reward = blockReward / KaspaConstants.SmallestUnit;
                block.Status = BlockStatus.Confirmed;
                block.ConfirmationProgress = 1;

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

        // TODO
        // * check balance
        // * issue payouts

        NotifyPayoutFailure(poolConfig.Id, balances, "TODO", null);
    }

    #endregion // IPayoutHandler
}
