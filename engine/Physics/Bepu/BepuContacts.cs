using System;
using System.Collections.Generic;
using System.Numerics;
using BepuPhysics.Collidables;

namespace Spot.Physics.Bepu;

/// <summary>
/// Shared collision-collection state for the Bepu backend. Trigger flags are written single-threaded by
/// <see cref="BepuPhysics3D"/> before each step and read (lock-free) by the narrow phase during it. The
/// narrow phase records touching pairs into per-worker buffers (no cross-thread contention), which
/// <see cref="BepuPhysics3D"/> drains after the step into engine-level contacts. Held as a class so the
/// value-type callbacks stored inside the simulation observe array growth and buffered records.
/// </summary>
internal sealed class BepuContacts
{
    // Indexed by handle value; true when that body/static's collider is a trigger.
    public bool[] BodyTrigger = new bool[128];
    public bool[] StaticTrigger = new bool[128];

    // Indexed by handle value; the collision layer (0..31) of that body/static's collider.
    public byte[] BodyLayer = new byte[128];
    public byte[] StaticLayer = new byte[128];

    private List<RawContact>[] _perWorker = Array.Empty<List<RawContact>>();

    /// <summary>Allocates one record buffer per worker thread. Idempotent for a given worker count.</summary>
    public void Configure(int workerCount)
    {
        int n = Math.Max(1, workerCount);
        if (_perWorker.Length == n) return;

        _perWorker = new List<RawContact>[n];
        for (int i = 0; i < n; i++)
        {
            _perWorker[i] = new List<RawContact>();
        }
    }

    /// <summary>Clears the per-worker buffers ahead of a step.</summary>
    public void Reset()
    {
        for (int i = 0; i < _perWorker.Length; i++)
        {
            _perWorker[i].Clear();
        }
    }

    public void EnsureBody(int index)
    {
        Grow(ref BodyTrigger, index);
        Grow(ref BodyLayer, index);
    }

    public void EnsureStatic(int index)
    {
        Grow(ref StaticTrigger, index);
        Grow(ref StaticLayer, index);
    }

    private static void Grow<T>(ref T[] array, int index)
    {
        if (index < array.Length) return;
        Array.Resize(ref array, Math.Max(index + 1, array.Length * 2));
    }

    /// <summary>Whether the collidable's collider is flagged as a trigger.</summary>
    public bool IsTrigger(CollidableReference r)
    {
        if (r.Mobility == CollidableMobility.Static)
        {
            int sv = r.StaticHandle.Value;
            return sv >= 0 && sv < StaticTrigger.Length && StaticTrigger[sv];
        }

        int bv = r.BodyHandle.Value;
        return bv >= 0 && bv < BodyTrigger.Length && BodyTrigger[bv];
    }

    /// <summary>The collision layer of the collidable's collider (0 when unknown).</summary>
    public int LayerOf(CollidableReference r)
    {
        if (r.Mobility == CollidableMobility.Static)
        {
            int sv = r.StaticHandle.Value;
            return sv >= 0 && sv < StaticLayer.Length ? StaticLayer[sv] : 0;
        }

        int bv = r.BodyHandle.Value;
        return bv >= 0 && bv < BodyLayer.Length ? BodyLayer[bv] : 0;
    }

    /// <summary>Records a touching pair from a narrow-phase worker into that worker's buffer.</summary>
    public void Record(int workerIndex, in RawContact contact)
    {
        if ((uint)workerIndex < (uint)_perWorker.Length)
        {
            _perWorker[workerIndex].Add(contact);
        }
    }

    /// <summary>Invokes <paramref name="action"/> for every recorded pair across all worker buffers.</summary>
    public void ForEach(Action<RawContact> action)
    {
        foreach (List<RawContact> buffer in _perWorker)
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                action(buffer[i]);
            }
        }
    }
}

/// <summary>A raw touching pair captured in the narrow phase, resolved to entities after the step.</summary>
internal readonly struct RawContact
{
    public readonly CollidableReference A;
    public readonly CollidableReference B;
    public readonly bool IsTrigger;

    /// <summary>Contact normal from the narrow phase (Bepu convention: pointing from B toward A).</summary>
    public readonly Vector3 Normal;

    /// <summary>Contact offset relative to A's center, resolved to a world point after the step.</summary>
    public readonly Vector3 OffsetA;

    public RawContact(CollidableReference a, CollidableReference b, bool isTrigger, Vector3 normal, Vector3 offsetA)
    {
        A = a;
        B = b;
        IsTrigger = isTrigger;
        Normal = normal;
        OffsetA = offsetA;
    }
}
