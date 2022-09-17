namespace Miningcore.Persistence.Postgres.Entities;

public class BanManager
{
    public string PoolId { get; set; }
    public string Address { get; set; }
    public string Reason { get; set; }
    public DateTime Expires { get; set; }
    public DateTime Created { get; set; }
}
