namespace Spot.Net;

/// <summary>
/// A stable, session-scoped identifier for a networked object, assigned by the server when the object is
/// spawned and used by every peer to refer to the same object. <c>0</c> is the invalid/none value.
/// </summary>
public readonly struct NetworkId : IEquatable<NetworkId>
{
    /// <summary>The unassigned id.</summary>
    public static readonly NetworkId None = default;

    /// <summary>Wraps a raw id value.</summary>
    /// <param name="value">The underlying id; <c>0</c> means none.</param>
    public NetworkId(uint value) => Value = value;

    /// <summary>The underlying id value.</summary>
    public uint Value { get; }

    /// <summary>Whether this id refers to an object (non-zero).</summary>
    public bool IsValid => Value != 0;

    /// <inheritdoc />
    public bool Equals(NetworkId other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NetworkId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)Value;

    /// <inheritdoc />
    public override string ToString() => Value.ToString();

    /// <summary>Compares two ids for equality.</summary>
    public static bool operator ==(NetworkId left, NetworkId right) => left.Equals(right);

    /// <summary>Compares two ids for inequality.</summary>
    public static bool operator !=(NetworkId left, NetworkId right) => !left.Equals(right);
}
