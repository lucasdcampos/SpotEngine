using System.Collections.Concurrent;
using System.Reflection;
using Spot.Core;

namespace Spot.Net;

/// <summary>Whether an RPC runs on the server (called by a client) or on clients (called by the server).</summary>
internal enum RpcKind
{
    Server,
    Client,
}

/// <summary>A resolved RPC handler: the method to invoke and where it is allowed to run.</summary>
internal readonly record struct RpcMethod(MethodInfo Method, RpcKind Kind);

/// <summary>
/// Resolves and caches the <c>[ServerRpc]</c>/<c>[ClientRpc]</c> methods of a <see cref="NetworkBehaviour"/>
/// type by name, so dispatch is a dictionary lookup after the first reflection pass per type.
/// </summary>
/// <remarks>
/// This uses reflection over the behaviour's own methods. That is fine on desktop and on a normal
/// (untrimmed) browser build; an aggressively trimmed/AOT wasm publish should preserve these methods (see
/// the networking docs). A source generator is the future hardening for that case.
/// </remarks>
internal static class RpcRegistry
{
    private static readonly ConcurrentDictionary<Type, Dictionary<string, RpcMethod>> s_cache = new();

    /// <summary>Looks up an RPC handler by method name on the given behaviour type.</summary>
    public static bool TryGet(Type type, string name, out RpcMethod rpc)
    {
        Dictionary<string, RpcMethod> map = s_cache.GetOrAdd(type, Build);
        return map.TryGetValue(name, out rpc);
    }

    private static Dictionary<string, RpcMethod> Build(Type type)
    {
        var map = new Dictionary<string, RpcMethod>(StringComparer.Ordinal);
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            bool server = method.GetCustomAttribute<ServerRpcAttribute>() is not null;
            bool client = method.GetCustomAttribute<ClientRpcAttribute>() is not null;
            if (!server && !client)
            {
                continue;
            }

            if (server && client)
            {
                Log.CoreError("Spot.Net: '{0}.{1}' is marked both [ServerRpc] and [ClientRpc]; ignoring.", type.Name, method.Name);
                continue;
            }

            map[method.Name] = new RpcMethod(method, server ? RpcKind.Server : RpcKind.Client);
        }

        return map;
    }
}
