using System.Data;

namespace Miningcore.Persistence.Repositories;

public interface IBanManagerRepository
{
    Task BanAsync(IDbConnection con, IDbTransaction tx, string ipaddress, string reason, DateTime expires);
    Task<bool> IsBanned(IDbConnection con, IDbTransaction tx, string ipaddress);
    Task RemoveBanAsync(IDbConnection con, IDbTransaction tx, string ipaddress);
}
