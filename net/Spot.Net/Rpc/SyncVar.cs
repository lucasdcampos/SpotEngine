namespace Spot.Net;

// The non-generic surface the replication layer uses to serialize and dirty-track a synchronized variable
// without knowing its element type.
internal interface ISyncVar
{
    bool Dirty { get; }

    void ClearDirty();

    bool Write(NetWriter writer);

    bool Read(ref NetReader reader);
}

/// <summary>
/// A server-authoritative synchronized variable: set its <see cref="Value"/> on the server and every client
/// receives the change. Declare one inside a <see cref="NetworkBehaviour"/> via
/// <see cref="NetworkBehaviour.Sync{T}"/>. The element type must be one of the network-serializable values
/// (int, uint, float, bool, string, Vector3, Quaternion, NetworkId, byte, ushort).
/// </summary>
/// <typeparam name="T">The synchronized value type.</typeparam>
public sealed class SyncVar<T> : ISyncVar
{
    private T _value;
    private bool _dirty;

    internal SyncVar(T value)
    {
        _value = value;
        _dirty = false;
    }

    /// <summary>
    /// The current value. Setting it on the server marks it dirty and replicates the new value to clients on
    /// the next tick; setting it on a client is local only and will be overwritten by the next update.
    /// </summary>
    public T Value
    {
        get => _value;
        set
        {
            if (!EqualityComparer<T>.Default.Equals(_value, value))
            {
                _value = value;
                _dirty = true;
            }
        }
    }

    bool ISyncVar.Dirty => _dirty;

    void ISyncVar.ClearDirty() => _dirty = false;

    bool ISyncVar.Write(NetWriter writer) => RpcSerializer.WriteValue(writer, _value);

    bool ISyncVar.Read(ref NetReader reader)
    {
        if (RpcSerializer.ReadValue(ref reader, out object? boxed) && boxed is T typed)
        {
            _value = typed;
            return true;
        }

        return false;
    }

    /// <summary>Reads the value directly, so a <see cref="SyncVar{T}"/> can be used where a <typeparamref name="T"/> is expected.</summary>
    public static implicit operator T(SyncVar<T> syncVar) => syncVar._value;
}
