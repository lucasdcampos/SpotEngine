using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace Spot.Physics.Bepu;

/// <summary>
/// Per-body contact material data, indexed by <c>BodyHandle.Value</c>. Populated single-threaded by
/// <see cref="BepuPhysics3D"/> before each step and read (lock-free) by the narrow phase during the
/// step. Held as a class so the value-type callbacks stored inside the simulation see array growth.
/// </summary>
internal sealed class BepuMaterials
{
    public float[] Friction = new float[128];
    public float[] Restitution = new float[128];
    public float DefaultFriction = 1f;

    public void Ensure(int index)
    {
        if (index < Friction.Length) return;
        int n = Math.Max(index + 1, Friction.Length * 2);
        Array.Resize(ref Friction, n);
        Array.Resize(ref Restitution, n);
    }

    public float FrictionOf(CollidableReference r)
    {
        if (r.Mobility == CollidableMobility.Static) return DefaultFriction;
        int v = r.BodyHandle.Value;
        return v >= 0 && v < Friction.Length ? Friction[v] : DefaultFriction;
    }

    public float RestitutionOf(CollidableReference r)
    {
        if (r.Mobility == CollidableMobility.Static) return 0f;
        int v = r.BodyHandle.Value;
        return v >= 0 && v < Restitution.Length ? Restitution[v] : 0f;
    }
}

/// <summary>
/// Narrow-phase callbacks: allow contacts whenever a dynamic body is involved and resolve each pair's
/// material from the two bodies' <see cref="BepuMaterials"/> entries. Restitution is approximated via
/// contact spring damping and recovery velocity (Bepu has no direct restitution coefficient).
/// </summary>
internal struct BepuNarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    private readonly BepuMaterials _materials;
    private readonly BepuContacts _contacts;

    public BepuNarrowPhaseCallbacks(BepuMaterials materials, BepuContacts contacts)
    {
        _materials = materials;
        _contacts = contacts;
    }

    public void Initialize(Simulation simulation)
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        => (a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic)
           && PhysicsSettings.LayersCollide(_contacts.LayerOf(a), _contacts.LayerOf(b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        // Record the pair for collision/trigger callbacks when it is actually touching (a contact with
        // non-negative depth), so speculative near-misses within the margin don't fire events.
        bool isTrigger = _contacts.IsTrigger(pair.A) || _contacts.IsTrigger(pair.B);
        int count = manifold.Count;
        float bestDepth = float.NegativeInfinity;
        Vector3 bestNormal = default;
        Vector3 bestOffset = default;
        for (int i = 0; i < count; i++)
        {
            manifold.GetContact(i, out Vector3 offset, out Vector3 normal, out float depth, out int _);
            if (depth > bestDepth)
            {
                bestDepth = depth;
                bestNormal = normal;
                bestOffset = offset;
            }
        }

        if (bestDepth >= 0f)
        {
            _contacts.Record(workerIndex, new RawContact(pair.A, pair.B, isTrigger, bestNormal, bestOffset));
        }

        // A trigger reports overlap but generates no constraints, so bodies pass through it.
        if (isTrigger)
        {
            pairMaterial = default;
            return false;
        }

        float fa = _materials.FrictionOf(pair.A);
        float fb = _materials.FrictionOf(pair.B);
        pairMaterial.FrictionCoefficient = MathF.Sqrt(MathF.Max(0f, fa) * MathF.Max(0f, fb));

        float restitution = MathF.Max(_materials.RestitutionOf(pair.A), _materials.RestitutionOf(pair.B));
        if (restitution > 0f)
        {
            // Lower damping ratio + unbounded recovery velocity ≈ bouncier contacts.
            pairMaterial.MaximumRecoveryVelocity = float.MaxValue;
            pairMaterial.SpringSettings = new SpringSettings(30f, MathF.Max(0f, 1f - restitution));
        }
        else
        {
            pairMaterial.MaximumRecoveryVelocity = 2f;
            pairMaterial.SpringSettings = new SpringSettings(30f, 1f);
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;

    public void Dispose()
    {
    }
}

/// <summary>
/// Pose integrator callbacks. Applies uniform world gravity (read from <see cref="PhysicsSettings"/>)
/// and light angular damping. Per-body gravity scale and linear drag are applied by
/// <see cref="BepuPhysics3D"/> before stepping, so they are not handled here.
/// </summary>
internal struct BepuPoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    private Vector3Wide _gravityDt;
    private Vector<float> _angularDampingDt;

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    public void Initialize(Simulation simulation)
    {
    }

    public void PrepareForIntegration(float dt)
    {
        Vector3Wide.Broadcast(PhysicsSettings.Gravity * dt, out _gravityDt);
        _angularDampingDt = new Vector<float>(MathF.Pow(Math.Clamp(1f - 0.05f, 0f, 1f), dt));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        velocity.Linear.X += _gravityDt.X;
        velocity.Linear.Y += _gravityDt.Y;
        velocity.Linear.Z += _gravityDt.Z;
        velocity.Angular.X *= _angularDampingDt;
        velocity.Angular.Y *= _angularDampingDt;
        velocity.Angular.Z *= _angularDampingDt;
    }
}
