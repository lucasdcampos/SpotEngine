namespace Spot.Net;

/// <summary>
/// Marks a <see cref="NetworkBehaviour"/> method as a server RPC: it runs on the server, invoked from a
/// client (the object's owner) via <see cref="NetworkBehaviour.InvokeServerRpc"/>. The server rejects the
/// call if the sender does not own the target object.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ServerRpcAttribute : Attribute
{
}

/// <summary>
/// Marks a <see cref="NetworkBehaviour"/> method as a client RPC: it runs on every client, invoked from the
/// server via <see cref="NetworkBehaviour.InvokeClientRpc"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClientRpcAttribute : Attribute
{
}
