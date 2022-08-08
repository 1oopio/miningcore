using Autofac;
using AutoMapper;
using Miningcore.Configuration;
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

        var result = new List<Block>();

        // TODO test if blocks found and gather amount

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
