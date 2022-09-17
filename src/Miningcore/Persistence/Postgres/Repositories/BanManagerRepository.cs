using System.Data;
using AutoMapper;
using Dapper;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;


namespace Miningcore.Persistence.Postgres.Repositories;

public class BanManagerRepository : IBanManagerRepository
{
    public BanManagerRepository(IMapper mapper, IMasterClock clock)
    {
        this.mapper = mapper;
        this.clock = clock;
    }

    private readonly IMapper mapper;
    private readonly IMasterClock clock;

    public Task RemoveBanAsync(IDbConnection con, IDbTransaction tx, string ipaddress)
    {
        const string query = @"DELETE FROM ban_manager WHERE ipaddress = @ipaddress";

        return con.ExecuteAsync(query, new { ipaddress }, tx);
    }

    public Task BanAsync(IDbConnection con, IDbTransaction tx, string ipaddress, string reason, DateTime expires)
    {
        const string query = @"
        INSERT INTO ban_manager 
            (ipaddress, reason, expires, created) 
        VALUES 
            (@ipaddress, @reason, @expires, @created)
        ON CONFLICT ON CONSTRAINT ban_manager_pkey
        DO UPDATE SET reason = @reason, expires = @expires, created = @created";

        return con.ExecuteAsync(query, new { ipaddress, reason, expires, created = clock.Now }, tx);
    }

    public async Task<bool> IsBanned(IDbConnection con, IDbTransaction tx, string ipaddress)
    {
        const string query = @"SELECT * FROM ban_manager WHERE ipaddress = @ipaddress";

        var entity = await con.QuerySingleOrDefaultAsync<Entities.BanManager>(query, new { ipaddress }, tx);

        if(entity == null)
            return false;

        // check if ban is expired
        if(entity.Expires < clock.Now)
        {
            await RemoveBanAsync(con, tx, ipaddress);
            return false;
        }

        return true;
    }
}
