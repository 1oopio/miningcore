using System.Data;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Dero.DaemonRequests;
using Miningcore.Blockchain.Dero.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Rpc;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Contract = Miningcore.Contracts.Contract;
using CNC = Miningcore.Blockchain.Dero.DeroCommands;

namespace Miningcore.Blockchain.Dero;

[CoinFamily(CoinFamily.Dero)]
public class DeroPayoutHandler : PayoutHandlerBase,
    IPayoutHandler
{
    public DeroPayoutHandler(
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

    private readonly IComponentContext ctx;
    private RpcClient rpcClient;
    private RpcClient rpcClientWallet;
    private DeroNetworkType? networkType;

    protected override string LogCategory => "Dero Payout Handler";

    private async Task<bool> HandleTransferResponseAsync(RpcResponse<TransferResponse> response, params Balance[] balances)
    {
        var coin = poolConfig.Template.As<DeroCoinTemplate>();

        if(response.Error == null)
        {
            var txHash = response.Response.TxId;

            logger.Info(() => $"[{LogCategory}] Payment transaction id: {txHash}");

            await PersistPaymentsAsync(balances, txHash);
            NotifyPayoutSuccess(poolConfig.Id, balances, new[] { txHash }, 0);
            return true;
        }

        else
        {
            logger.Error(() => $"[{LogCategory}] Daemon command '{DeroWalletCommands.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}");

            NotifyPayoutFailure(poolConfig.Id, balances, $"Daemon command '{DeroWalletCommands.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}", null);
            return false;
        }
    }

    private async Task UpdateNetworkTypeAsync(CancellationToken ct)
    {
        if(!networkType.HasValue)
        {
            var infoResponse = await rpcClient.ExecuteAsync(logger, CNC.GetInfo, ct, true);
            var info = infoResponse.Response.ToObject<GetInfoResponse>();

            if(info == null)
                throw new PoolStartupException($"{LogCategory}] Unable to determine network type", poolConfig.Id);

            networkType = info.IsTestnet ? DeroNetworkType.Test : DeroNetworkType.Main;
        }
    }

    private async Task<bool> EnsureBalance(decimal requiredAmount, DeroCoinTemplate coin, CancellationToken ct)
    {
        var response = await rpcClientWallet.ExecuteAsync<GetBalanceResponse>(logger, DeroWalletCommands.GetBalance, ct);

        if(response.Error != null)
        {
            logger.Error(() => $"[{LogCategory}] Daemon command '{DeroWalletCommands.GetBalance}' returned error: {response.Error.Message} code {response.Error.Code}");
            return false;
        }

        var unlockedBalance = Math.Floor(response.Response.UnlockedBalance / coin.SmallestUnit);
        var balance = Math.Floor(response.Response.Balance / coin.SmallestUnit);

        if(unlockedBalance < requiredAmount)
        {
            logger.Info(() => $"[{LogCategory}] {FormatAmount(requiredAmount)} unlocked balance required for payment, but only have {FormatAmount(unlockedBalance)} of {FormatAmount(balance)} available yet. Will try again.");
            return false;
        }

        logger.Info(() => $"[{LogCategory}] Current balance is {FormatAmount(unlockedBalance)}");
        return true;
    }

    private async Task<bool> PayoutBatch(Balance[] balances, CancellationToken ct)
    {
        var coin = poolConfig.Template.As<DeroCoinTemplate>();

        // ensure there's enough balance
        if(!await EnsureBalance(balances.Sum(x => x.Amount), coin, ct))
            return false;

        // build request
        var request = new TransferRequest
        {
            Transfers = balances
                .Where(x => x.Amount > 0)
                .Select(x =>
                {
                    return new TransferDestination
                    {
                        Destination = x.Address,
                        Amount = (ulong) Math.Floor(x.Amount * coin.SmallestUnit)
                    };
                }).ToArray(),
        };

        if(request.Transfers.Length == 0)
            return true;

        logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses:\n{string.Join("\n", balances.OrderByDescending(x => x.Amount).Select(x => $"{FormatAmount(x.Amount)} to {x.Address}"))}");

        // send command
        var transferResponse = await rpcClientWallet.ExecuteAsync<TransferResponse>(logger, DeroWalletCommands.Transfer, ct, request);

        return await HandleTransferResponseAsync(transferResponse, balances);
    }

    #region IPayoutHandler

    public async Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        Contract.RequiresNonNull(pc);

        poolConfig = pc;
        clusterConfig = cc;

        logger = LogUtil.GetPoolScopedLogger(typeof(DeroPayoutHandler), pc);

        // configure standard daemon
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

        var daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = DeroConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        rpcClient = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, pc.Id);

        // configure wallet daemon
        var walletDaemonEndpoints = pc.Daemons
            .Where(x => x.Category?.ToLower() == DeroConstants.WalletDaemonCategory)
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = DeroConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        rpcClientWallet = new RpcClient(walletDaemonEndpoints.First(), jsonSerializerSettings, messageBus, pc.Id);

        // detect network
        await UpdateNetworkTypeAsync(ct);
    }

    public async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(blocks);

        var coin = poolConfig.Template.As<DeroCoinTemplate>();
        var result = new List<Block>();

        var blocksByHeight = blocks.GroupBy(x => x.BlockHeight);

        foreach(var heightBlocks in blocksByHeight)
        {
            var blockHeight = heightBlocks.Key;

            var rpcResult = await rpcClient.ExecuteAsync<GetBlockHeaderResponse>(logger,
                CNC.GetBlockHeaderByHeight, ct,
                new GetBlockHeaderByTopoHeightRequest
                {
                    TopoHeight = blockHeight
                });

            if(rpcResult.Error != null)
            {
                logger.Debug(() => $"[{LogCategory}] Daemon reports error '{rpcResult.Error.Message}' (Code {rpcResult.Error.Code}) for block {blockHeight}");
                continue;
            }

            if(rpcResult.Response?.BlockHeader == null)
            {
                logger.Debug(() => $"[{LogCategory}] Daemon returned no header for block {blockHeight}");
                continue;
            }

            var blockHeader = rpcResult.Response.BlockHeader;

            decimal blockConfirmedReward = 0;

            if ((blockHeader.Depth >= DeroConstants.PayoutMinBlockConfirmations) && !blockHeader.IsOrphaned)
            {
                var request = new GetTransfersRequest
                {
                    MinHeight = blockHeight - 1,
                    MaxHeight = blockHeight + 1,
                    Coinbase = true,
                };

                var transfers = await rpcClientWallet.ExecuteAsync<GetTransfersResponse>(logger, DeroWalletCommands.GetTransfers, ct, request);

                if(transfers.Error != null)
                {
                    logger.Debug(() => $"[{LogCategory}]Wallet Daemon reports error '{rpcResult.Error.Message}' (Code {rpcResult.Error.Code}) for block {blockHeight}");
                    continue;
                }

                if(transfers.Response?.Entries != null)
                {
                    var foundTransfer = transfers.Response.Entries.Where(x => x.TopoHeight == blockHeader.TopoHeight && x.Coinbase);

                    if(foundTransfer.Count() == 1)
                    {
                        blockConfirmedReward = (foundTransfer.First().Amount / coin.SmallestUnit) * coin.BlockrewardMultiplier;

                        logger.Info(() => $"[{LogCategory}] Unlocked block {blockHeight} worth {FormatAmount(blockConfirmedReward)} needs to be split between {heightBlocks.Count()} mini blocks");

                        // Now we need to split the reward between all (mini)blocks
                        blockConfirmedReward /= heightBlocks.Count();
                    }
                    else
                    {
                        logger.Warn(() => $"[{LogCategory} Found {foundTransfer.Count()} matching transactions in wallet for block {blockHeight}, this should not be.");
                    }
                }
            }

            foreach(var block in heightBlocks)
            {
                // update progress
                block.ConfirmationProgress = Math.Min(1.0d, (double) blockHeader.Depth / DeroConstants.PayoutMinBlockConfirmations);

                // update infos
                block.NetworkDifficulty = blockHeader.Difficulty;

                if(block.TransactionConfirmationData.StartsWith("mb") && block.TransactionConfirmationData.Length == 66)
                {
                    block.Hash = blockHeader.Hash;
                }

                result.Add(block);

                messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);

                // orphaned?
                if(blockHeader.IsOrphaned)
                {
                    block.Status = BlockStatus.Orphaned;
                    block.Reward = 0;

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                    continue;
                }

                // matured and spendable?
                if(blockHeader.Depth >= DeroConstants.PayoutMinBlockConfirmations)
                {
                    // We got no reward for the block candidate so its possible a orphaned (mini)block
                    if (blockConfirmedReward == 0)
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.ConfirmationProgress = 0;
                        block.Reward = 0;
                    }
                    else
                    {
                        block.Status = BlockStatus.Confirmed;
                        block.ConfirmationProgress = 1;
                        block.Reward = blockConfirmedReward;
                    }

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                }
            }
        }

        return result.ToArray();
    }

    public Task CalculateBlockEffortAsync(IMiningPool pool, Block block, double accumulatedBlockShareDiff, CancellationToken ct)
    {
        block.Effort = accumulatedBlockShareDiff / block.NetworkDifficulty;

        return Task.FromResult(true);
    }

    public override async Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx,
        IMiningPool pool, Block block, CancellationToken ct)
    {
        var blockRewardRemaining = await base.UpdateBlockRewardBalancesAsync(con, tx, pool, block, ct);

        // Deduct static reserve for tx fees
        blockRewardRemaining -= DeroConstants.StaticTransactionFeeReserve;

        return blockRewardRemaining;
    }

    public async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        Contract.RequiresNonNull(balances);

        var coin = poolConfig.Template.As<DeroCoinTemplate>();

#if !DEBUG // ensure we have peers
            var requiredConnections = 4;
            if (networkType == DeroNetworkType.Test)
               requiredConnections = 1;

            var infoResponse = await rpcClient.ExecuteAsync<GetInfoResponse>(logger, CNC.GetInfo, ct);
            if (infoResponse.Error != null || infoResponse.Response == null ||
                infoResponse.Response.IncomingConnectionsCount + infoResponse.Response.OutgoingConnectionsCount < requiredConnections)
            {
                logger.Warn(() => $"[{LogCategory}] Payout aborted. Not enough peers ({requiredConnections} required)");
                return;
            }
#endif
        // validate addresses
        balances = balances
            .Where(x =>
            {
                 switch(networkType)
                {
                    case DeroNetworkType.Main:
                        if(!x.Address.ToLower().StartsWith("dero"))
                        {
                            logger.Warn(() => $"[{LogCategory}] Excluding payment to invalid address: {x.Address}");
                            return false;
                        }

                        break;

                    case DeroNetworkType.Test:
                        if(!x.Address.ToLower().StartsWith("deto"))
                        {
                            logger.Warn(() => $"[{LogCategory}] Excluding payment to invalid address: {x.Address}");
                            return false;
                        }

                        break;
                }

                return true;
            })
            .ToArray();

        if(balances.Length > 0)
        {
            var maxBatchSize = 15;
            var pageSize = maxBatchSize;
            var pageCount = (int) Math.Ceiling((double) balances.Length / pageSize);

            for(var i = 0; i < pageCount; i++)
            {
                var page = balances
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                if(!await PayoutBatch(page, ct))
                    break;
            }
        }
    }

    #endregion // IPayoutHandler
}
