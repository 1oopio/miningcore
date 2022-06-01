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

            // NOTE: monerod does not support batch-requests
            for(var j = 0; j < page.Length; j++)
            {
                var block = page[j];

                var rpcResult = await rpcClient.ExecuteAsync<GetBlockHeaderResponse>(logger,
                    CNC.GetBlockHeaderByHash, ct,
                    new GetBlockHeaderByHashRequest
                    {
                        Hash = block.Hash
                    });

                if(rpcResult.Error != null)
                {
                    logger.Debug(() => $"[{LogCategory}] Daemon reports error '{rpcResult.Error.Message}' (Code {rpcResult.Error.Code}) for block {block.BlockHeight}");
                    continue;
                }

                if(rpcResult.Response?.BlockHeader == null)
                {
                    logger.Debug(() => $"[{LogCategory}] Daemon returned no header for block {block.BlockHeight}");
                    continue;
                }

                var blockHeader = rpcResult.Response.BlockHeader;

                // update progress
                block.ConfirmationProgress = Math.Min(1.0d, (double) blockHeader.Depth / DeroConstants.PayoutMinBlockConfirmations);

                // update infos
                block.NetworkDifficulty = blockHeader.Difficulty;
                block.BlockHeight = blockHeader.Height;

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
                    block.Status = BlockStatus.Confirmed;
                    block.ConfirmationProgress = 1;
                    block.Reward = (blockHeader.Reward / coin.SmallestUnit) * coin.BlockrewardMultiplier;

                    logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

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
