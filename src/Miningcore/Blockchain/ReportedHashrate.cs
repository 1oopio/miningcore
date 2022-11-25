using ProtoBuf;

namespace Miningcore.Blockchain;

[ProtoContract]
public class ReportedHashrate
{
    /// <summary>
    /// The pool originating this hashrate is from
    /// </summary>
    [ProtoMember(1)]
    public string PoolId { get; set; }

    /// <summary>
    /// Who reported it (wallet address)
    /// </summary>
    [ProtoMember(2)]
    public string Miner { get; set; }

    /// <summary>
    /// Who reported it (worker name)
    /// </summary>
    [ProtoMember(3)]
    public string Worker { get; set; }


    /// <summary>
    /// Hashrate reported by the miner
    /// </summary>
    [ProtoMember(4)]
    public ulong Hashrate { get; set; }


    /// <summary>
    /// When the hashrate was reported
    /// </summary>
    [ProtoMember(5)]
    public DateTime Created { get; set; }

}
