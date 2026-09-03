namespace Spot.Net;

/// <summary>
/// The role the local peer plays in a networking session.
/// </summary>
public enum NetworkRole
{
    /// <summary>Not connected to anything.</summary>
    None,

    /// <summary>A dedicated server: authoritative, hosts connections, runs no local player of its own.</summary>
    Server,

    /// <summary>A client connected to a remote server.</summary>
    Client,

    /// <summary>A listen server: authoritative server <em>and</em> a local client in one process.</summary>
    Host,
}
