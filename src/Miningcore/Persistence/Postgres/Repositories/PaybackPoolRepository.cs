using System.Data;
using AutoMapper;
using Dapper;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;

namespace Miningcore.Persistence.Postgres.Repositories;

public class PaybackPoolRepository : IPaybackPoolRepository
{
    public PaybackPoolRepository(IMapper mapper)
    {
        this.mapper = mapper;
    }

    private readonly IMapper mapper;

    /* public async Task<int> AddPoolAsync(IDbConnection con, IDbTransaction tx, PaybackPool paybackPool)
    {
        return 0;
    } */

    public async Task<PaybackPool[]> GetPoolsAsync(IDbConnection con, string poolId)
    {
        var now = DateTime.UtcNow;

        const string query = @"
        SELECT p.* FROM payback_pools p
        WHERE 
            p.poolid = @poolId AND
            p.available > 0 AND
            p.activefrom <= @now
        ";

        return (await con.QueryAsync<Entities.PaybackPool>(query, new { poolId, now }))
            .Select(mapper.Map<PaybackPool>)
            .ToArray();
    }

    public async Task UpdatePoolAvailableAsync(IDbConnection con, IDbTransaction tx, string poolId, string poolName, decimal available)
    {
        var now = DateTime.UtcNow;

        const string query = @"
        UPDATE payback_pools 
        SET 
            available = @available, 
            updated = @now
        WHERE 
            poolid = @poolId AND 
            poolname = @poolName
        ";

        await con.ExecuteAsync(query, new { poolId, poolName, available, now }, tx);
    }
}
