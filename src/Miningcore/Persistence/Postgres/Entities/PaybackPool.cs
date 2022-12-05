namespace Miningcore.Persistence.Postgres.Entities;

public class PaybackPool
{
    public long Id { get; set; }
    public string PoolId { get; set; }
    public string PoolName { get; set; }
    public decimal Amount { get; set; }
    public decimal Available { get; set; }
    public double SharePercentage { get; set; }
    public DateTime ActiveFrom { get; set; }
    public DateTime Updated { get; set; }
    public DateTime Created { get; set; }
}
