using System;
using System.Collections.Generic;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities;
using BepuUtilities.Memory;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Physics.Bepu;

/// <summary>
/// A BepuPhysics v2 backend for the engine's 3D physics. Owns a <see cref="Simulation"/> and mirrors
/// the scene's physics entities into it each step: entities with a collider (box/sphere/capsule) become
/// Bepu bodies (dynamic/kinematic) or statics, are simulated, and their results are written back onto
/// the <see cref="TransformComponent"/>.
/// </summary>
/// <remarks>
/// Contract: dynamic bodies are driven by velocity (set <see cref="PhysicsBody3DComponent.Velocity"/>
/// from scripts); their transform is owned by the simulation. Kinematic/static bodies are the reverse —
/// move them by their transform. Physics entities are treated as root-level for transform sync.
/// </remarks>
internal sealed class BepuPhysics3D : IPhysics3D
{
    private const float DegToRad = MathF.PI / 180f;
    private const float GroundProbe = 0.12f;

    private readonly BufferPool _pool = new();
    private readonly ThreadDispatcher _dispatcher;
    private readonly Simulation _simulation;
    private readonly BepuMaterials _materials = new();
    private readonly BepuContacts _contacts = new();

    private readonly Dictionary<int, Tracked> _tracked = new();
    private readonly Dictionary<int, int> _bodyToEntity = new();
    private readonly Dictionary<int, int> _staticToEntity = new();
    private readonly HashSet<int> _seen = new();
    private readonly List<int> _toRemove = new();
    private readonly List<ContactPair> _contactPairs = new();

    public BepuPhysics3D()
    {
        _dispatcher = new ThreadDispatcher(Math.Max(1, Environment.ProcessorCount - 1));
        _contacts.Configure(_dispatcher.ThreadCount);
        _simulation = Simulation.Create(
            _pool,
            new BepuNarrowPhaseCallbacks(_materials, _contacts),
            new BepuPoseIntegratorCallbacks(),
            new SolveDescription(8, 1));
    }

    /// <inheritdoc />
    public IReadOnlyList<ContactPair> Contacts => _contactPairs;

    public void Step(Scene scene, float deltaTime)
    {
        _contactPairs.Clear();
        if (deltaTime <= 0f) return;

        try
        {
            _contacts.Reset();
            Sync(scene, deltaTime);
            _simulation.Timestep(deltaTime, _dispatcher);
            WriteBack(scene);
            CollectContacts(scene);
        }
        catch (Exception ex)
        {
            Log.CoreError("BepuPhysics3D step failed: {0}", ex.Message);
        }
    }

    // Resolves the narrow phase's raw touching pairs into engine-level contacts (entities + a world
    // contact point), consumed by the CollisionDispatcher to raise enter/stay/exit callbacks.
    private void CollectContacts(Scene scene)
    {
        _contacts.ForEach(raw =>
        {
            Entity? a = ResolveEntity(scene, raw.A);
            Entity? b = ResolveEntity(scene, raw.B);
            if (a is null || b is null) return;

            Vector3 point = PositionOf(raw.A) + raw.OffsetA;
            _contactPairs.Add(new ContactPair(a.Value, b.Value, raw.IsTrigger, raw.Normal, point));
        });
    }

    private Entity? ResolveEntity(Scene scene, CollidableReference collidable)
    {
        int id;
        bool found = collidable.Mobility == CollidableMobility.Static
            ? _staticToEntity.TryGetValue(collidable.StaticHandle.Value, out id)
            : _bodyToEntity.TryGetValue(collidable.BodyHandle.Value, out id);
        return found ? scene.EntityById(id) : null;
    }

    private Vector3 PositionOf(CollidableReference collidable) =>
        collidable.Mobility == CollidableMobility.Static
            ? _simulation.Statics[collidable.StaticHandle].Pose.Position
            : _simulation.Bodies[collidable.BodyHandle].Pose.Position;

    // --- Sync scene -> simulation -------------------------------------------------------------

    private void Sync(Scene scene, float dt)
    {
        _seen.Clear();

        foreach (var entity in scene.View<TransformComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var transform = entity.GetComponent<TransformComponent>();
            if (!transform.Enabled) continue;

            // Use the authored local scale, not WorldScale: the latter is decomposed from the world
            // matrix and its basis-vector lengths jitter by ~1e-7 every frame once the body rotates,
            // which would make the ShapeKey differ each frame and recreate the body (wiping its angular
            // velocity). Physics entities are treated as root-level, so local scale is the right value.
            if (!TryDescribeCollider(entity, transform.Scale, out ColliderDesc desc)) continue;

            bool hasBody = entity.TryGetComponent(out PhysicsBody3DComponent? body) && body.Enabled;
            int kind = !hasBody ? 0 : body!.IsKinematic ? 1 : body.IsDynamic ? 2 : 0;
            bool freeze = hasBody && body!.FreezeRotation;
            float mass = hasBody ? MathF.Max(0.0001f, body!.Mass) : 1f;

            // Quantize so residual float noise (or tiny scripted changes) never thrashes recreation,
            // while genuine resizes (e.g. crouch) still cross a bucket and rebuild the shape.
            var key = new ShapeKey(kind, desc.Type, Q(desc.A), Q(desc.B), Q(desc.C), freeze, kind == 2 ? Q(mass) : 0f);
            _seen.Add(entity.Id);

            Quaternion orientation = freeze ? Quaternion.Identity : EulerToQuaternion(transform.WorldRotation);
            Vector3 worldOffset = freeze ? desc.Offset : Vector3.Transform(desc.Offset, orientation);
            Vector3 shapeCenter = transform.WorldPosition + worldOffset;

            if (!_tracked.TryGetValue(entity.Id, out Tracked tracked) || !tracked.Key.Equals(key))
            {
                if (_tracked.TryGetValue(entity.Id, out Tracked old)) DestroyTracked(entity.Id, old);
                tracked = CreateTracked(entity.Id, key, desc, mass, freeze, shapeCenter, orientation, hasBody ? body!.Velocity : Vector3.Zero);
                _tracked[entity.Id] = tracked;
            }

            // Publish this collidable's trigger flag for the narrow phase (re-set each step so a runtime
            // toggle takes effect, and so a recycled handle never inherits a stale flag).
            byte layer = (byte)Math.Clamp(desc.Layer, 0, PhysicsSettings.LayerCount - 1);
            if (tracked.IsStatic)
            {
                _contacts.EnsureStatic(tracked.Static.Value);
                _contacts.StaticTrigger[tracked.Static.Value] = desc.IsTrigger;
                _contacts.StaticLayer[tracked.Static.Value] = layer;
            }
            else
            {
                _contacts.EnsureBody(tracked.Body.Value);
                _contacts.BodyTrigger[tracked.Body.Value] = desc.IsTrigger;
                _contacts.BodyLayer[tracked.Body.Value] = layer;
            }

            // Push authoring state into the simulation.
            if (kind == 2 && hasBody)
            {
                var b = _simulation.Bodies[tracked.Body];
                Vector3 v = body!.Velocity;
                v += PhysicsSettings.Gravity * (body.GravityScale - 1f) * dt; // integrator applies the base gravity
                if (body.LinearDrag > 0f) v *= MathF.Max(0f, 1f - body.LinearDrag * dt);
                b.Velocity.Linear = v;
                b.Awake = true;

                int hv = tracked.Body.Value;
                _materials.Ensure(hv);
                _materials.Friction[hv] = body.Friction;
                _materials.Restitution[hv] = body.Restitution;
            }
            else if (kind == 1)
            {
                var b = _simulation.Bodies[tracked.Body];
                b.Pose.Position = shapeCenter;
                b.Pose.Orientation = orientation;
                b.Velocity.Linear = Vector3.Zero;
                b.Velocity.Angular = Vector3.Zero;
                b.Awake = true;
            }
            else // static
            {
                var s = _simulation.Statics[tracked.Static];
                s.Pose.Position = shapeCenter;
                s.Pose.Orientation = orientation;
                s.UpdateBounds();
            }
        }

        // Remove bodies whose entity or components disappeared.
        _toRemove.Clear();
        foreach (var kvp in _tracked)
        {
            if (!_seen.Contains(kvp.Key)) _toRemove.Add(kvp.Key);
        }
        foreach (int id in _toRemove)
        {
            DestroyTracked(id, _tracked[id]);
            _tracked.Remove(id);
        }
    }

    private Tracked CreateTracked(int entityId, ShapeKey key, ColliderDesc desc, float mass, bool freeze, Vector3 center, Quaternion orientation, Vector3 velocity)
    {
        TypedIndex shape = AddShape(desc);
        var pose = new RigidPose(center, orientation);

        var tracked = new Tracked { Key = key, Shape = shape, Offset = desc.Offset, HalfHeightY = desc.HalfHeightY, Freeze = freeze };

        if (key.Kind == 0)
        {
            var handle = _simulation.Statics.Add(new StaticDescription(pose.Position, pose.Orientation, shape));
            tracked.IsStatic = true;
            tracked.Static = handle;
            _staticToEntity[handle.Value] = entityId;
        }
        else if (key.Kind == 1)
        {
            var handle = _simulation.Bodies.Add(BodyDescription.CreateKinematic(pose, new CollidableDescription(shape, 0.1f), new BodyActivityDescription(0.01f, 32)));
            tracked.Body = handle;
            _bodyToEntity[handle.Value] = entityId;
        }
        else
        {
            BodyInertia inertia = ComputeInertia(desc, mass);
            if (freeze) inertia.InverseInertiaTensor = default;
            var body = BodyDescription.CreateDynamic(pose, new BodyVelocity(velocity), inertia, new CollidableDescription(shape, 0.1f), new BodyActivityDescription(0.01f, 32));
            var handle = _simulation.Bodies.Add(body);
            tracked.Body = handle;
            _bodyToEntity[handle.Value] = entityId;
        }
        return tracked;
    }

    private void DestroyTracked(int entityId, Tracked tracked)
    {
        if (tracked.IsStatic)
        {
            if (_simulation.Statics.StaticExists(tracked.Static))
            {
                _staticToEntity.Remove(tracked.Static.Value);
                _simulation.Statics.Remove(tracked.Static);
            }
        }
        else if (_simulation.Bodies.BodyExists(tracked.Body))
        {
            _bodyToEntity.Remove(tracked.Body.Value);
            _simulation.Bodies.Remove(tracked.Body);
        }
        if (tracked.Shape.Exists) _simulation.Shapes.RemoveAndDispose(tracked.Shape, _pool);
    }

    // --- Write simulation -> scene ------------------------------------------------------------

    private void WriteBack(Scene scene)
    {
        foreach (var kvp in _tracked)
        {
            Tracked tracked = kvp.Value;
            if (tracked.IsStatic) continue;
            if (!_simulation.Bodies.BodyExists(tracked.Body)) continue;

            var entity = scene.EntityById(kvp.Key);
            if (entity is null) continue;
            if (!entity.Value.TryGetComponent(out TransformComponent? transform)) continue;
            if (!entity.Value.TryGetComponent(out PhysicsBody3DComponent? body)) continue;
            if (body!.IsKinematic) continue; // kinematic pose is authored, not simulated

            var b = _simulation.Bodies[tracked.Body];
            RigidPose pose = b.Pose;
            Vector3 worldOffset = tracked.Freeze ? tracked.Offset : Vector3.Transform(tracked.Offset, pose.Orientation);

            transform!.Position = pose.Position - worldOffset;
            body.Velocity = b.Velocity.Linear;
            if (!tracked.Freeze) transform.Rotation = QuaternionToEuler(pose.Orientation);

            body.Grounded = ProbeGround(tracked, pose.Position);
        }
    }

    private bool ProbeGround(Tracked tracked, Vector3 center)
    {
        // Short downward ray from the body's center; grounded if it hits anything but itself.
        var handler = new GroundRayHandler(tracked.Body.Value);
        float distance = tracked.HalfHeightY + GroundProbe;
        Vector3 origin = center;
        Vector3 direction = new Vector3(0f, -1f, 0f);
        _simulation.RayCast(origin, direction, distance, ref handler, 0);
        return handler.Hit;
    }

    // --- Raycast -------------------------------------------------------------------------------

    public bool Raycast(Scene scene, Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
    {
        hit = default;
        if (direction.LengthSquared() < 1e-12f) return false;
        direction = Vector3.Normalize(direction);

        var handler = new ClosestRayHandler();
        try
        {
            _simulation.RayCast(origin, direction, maxDistance, ref handler, 0);
        }
        catch (Exception ex)
        {
            Log.CoreError("BepuPhysics3D raycast failed: {0}", ex.Message);
            return false;
        }
        if (!handler.Hit) return false;

        int entityId;
        bool found = handler.Collidable.Mobility == CollidableMobility.Static
            ? _staticToEntity.TryGetValue(handler.Collidable.StaticHandle.Value, out entityId)
            : _bodyToEntity.TryGetValue(handler.Collidable.BodyHandle.Value, out entityId);
        if (!found) return false;

        Entity? entity = scene.EntityById(entityId);
        if (entity is null) return false;

        hit = new RaycastHit(entity.Value, origin + direction * handler.T, handler.Normal, handler.T);
        return true;
    }

    // --- Shapes --------------------------------------------------------------------------------

    private static bool TryDescribeCollider(Entity entity, Vector3 scale, out ColliderDesc desc)
    {
        if (entity.TryGetComponent(out BoxCollider3DComponent? box) && box!.Enabled)
        {
            Vector3 s = box.Size * scale;
            desc = new ColliderDesc(0, s.X, s.Y, s.Z, box.Offset * scale, s.Y * 0.5f, box.IsTrigger, box.Layer);
            return true;
        }
        if (entity.TryGetComponent(out SphereCollider3DComponent? sphere) && sphere!.Enabled)
        {
            float r = sphere.Radius * scale.X;
            desc = new ColliderDesc(1, r, 0f, 0f, sphere.Offset * scale, r, sphere.IsTrigger, sphere.Layer);
            return true;
        }
        if (entity.TryGetComponent(out CapsuleCollider3DComponent? capsule) && capsule!.Enabled)
        {
            float r = capsule.Radius * scale.X;
            float len = capsule.Length * scale.Y;
            desc = new ColliderDesc(2, r, len, 0f, capsule.Offset * scale, len * 0.5f + r, capsule.IsTrigger, capsule.Layer);
            return true;
        }
        desc = default;
        return false;
    }

    private TypedIndex AddShape(ColliderDesc desc) => desc.Type switch
    {
        0 => _simulation.Shapes.Add(new Box(desc.A, desc.B, desc.C)),
        1 => _simulation.Shapes.Add(new Sphere(desc.A)),
        _ => _simulation.Shapes.Add(new Capsule(desc.A, desc.B)),
    };

    private static BodyInertia ComputeInertia(ColliderDesc desc, float mass) => desc.Type switch
    {
        0 => new Box(desc.A, desc.B, desc.C).ComputeInertia(mass),
        1 => new Sphere(desc.A).ComputeInertia(mass),
        _ => new Capsule(desc.A, desc.B).ComputeInertia(mass),
    };

    // --- Euler <-> quaternion (matches Matrix4x4.CreateFromYawPitchRoll: Y=yaw, X=pitch, Z=roll) ---

    /// <summary>Rounds to 0.1 mm buckets so float noise doesn't trigger needless body recreation.</summary>
    private static float Q(float v) => MathF.Round(v, 4);

    private static Quaternion EulerToQuaternion(Vector3 eulerDegrees)
    {
        Vector3 r = eulerDegrees * DegToRad;
        return Quaternion.CreateFromYawPitchRoll(r.Y, r.X, r.Z);
    }

    internal static Vector3 QuaternionToEuler(Quaternion q)
    {
        float sinPitch = 2f * (q.W * q.X - q.Y * q.Z);
        float pitch = MathF.Abs(sinPitch) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinPitch) : MathF.Asin(sinPitch);
        float yaw = MathF.Atan2(2f * (q.W * q.Y + q.X * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y));
        float roll = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.X * q.X + q.Z * q.Z));
        return new Vector3(pitch, yaw, roll) * (180f / MathF.PI);
    }

    public void Dispose()
    {
        try
        {
            _simulation.Dispose();
            _dispatcher.Dispose();
            _pool.Clear();
        }
        catch (Exception ex)
        {
            Log.CoreError("BepuPhysics3D dispose failed: {0}", ex.Message);
        }
    }

    private struct Tracked
    {
        public ShapeKey Key;
        public TypedIndex Shape;
        public bool IsStatic;
        public BodyHandle Body;
        public StaticHandle Static;
        public Vector3 Offset;
        public float HalfHeightY;
        public bool Freeze;
    }

    private readonly record struct ShapeKey(int Kind, int Type, float A, float B, float C, bool Freeze, float Mass);

    private readonly record struct ColliderDesc(int Type, float A, float B, float C, Vector3 Offset, float HalfHeightY, bool IsTrigger, int Layer);
}
