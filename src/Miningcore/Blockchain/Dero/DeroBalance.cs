using Miningcore.Blockchain.Dero.DaemonRequests;
using Miningcore.Persistence.Model;

namespace Miningcore.Blockchain.Dero;

public class DeroBalance
{
    public string Address { get; init; }
    public PayloadRpc[] PayloadRpc { get; set; }
    public Balance Balance { get; set; }

}
