using System.Data;
using Miningcore.Persistence.Model;

namespace Miningcore.Persistence.Repositories;

public interface IPaybackPoolRepository
{
    //Task<int> AddPoolAsync(IDbConnection con, IDbTransaction tx, PaybackPool paybackPool);
    Task<PaybackPool[]> GetPoolsAsync(IDbConnection con, string poolId);
    Task UpdatePoolAvailableAsync(IDbConnection con, IDbTransaction tx, string poolId, string poolName, decimal available);
}
