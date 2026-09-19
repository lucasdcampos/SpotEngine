using System.Numerics;
using Spot.Scenes;

namespace Spot.Net;

/// <summary>
/// Replicates an entity's <see cref="TransformComponent"/> across the network. The owner's transform is
/// sampled and sent each tick; on other peers it is smoothed toward the latest received value so movement
/// looks continuous despite the discrete snapshot rate. Attach it alongside a <see cref="NetworkObject"/>.
/// </summary>
public sealed class NetworkTransform : Component
{
    /// <summary>Whether to replicate position. Defaults to <see langword="true"/>.</summary>
    public bool SyncPosition { get; set; } = true;

    /// <summary>Whether to replicate rotation (Euler degrees). Defaults to <see langword="true"/>.</summary>
    public bool SyncRotation { get; set; } = true;

    // The latest received transform this peer smooths toward (non-owners only). Set by the replication
    // layer from inbound snapshots; consumed by its interpolation step.
    internal bool HasTarget { get; set; }

    internal Vector3 TargetPosition { get; set; }

    internal Vector3 TargetRotation { get; set; }
}
