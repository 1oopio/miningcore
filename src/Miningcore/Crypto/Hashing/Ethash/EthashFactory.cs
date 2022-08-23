using System.Collections.Concurrent;
using Autofac;
using Newtonsoft.Json.Linq;

namespace Miningcore.Crypto.Hashing.Ethash;

public static class EthashFactory
{
    private static readonly ConcurrentDictionary<string, IEthashDag> cacheDag = new();
    private static readonly ConcurrentDictionary<string, IEthashFull> cacheFull = new();

    public static IEthashDag GetEthashDag(IComponentContext ctx, string name)
    {
        if(name == "")
            return null;

        // check cache
        if(cacheDag.TryGetValue(name, out var result))
            return result;

        result = ctx.ResolveNamed<IEthashDag>(name);

        cacheDag.TryAdd(name, result);
        
        return result;
    }

    public static IEthashFull GetEthashFull(IComponentContext ctx, string name)
    {
        if(name == "")
            return null;

        // check cache
        if(cacheFull.TryGetValue(name, out var result))
            return result;

        result = ctx.ResolveNamed<IEthashFull>(name);

        cacheFull.TryAdd(name, result);
        
        return result;
    }
}
