using System.Numerics;
using Spot.Core;
using Spot.Scenes;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using AVec = nkast.Aether.Physics2D.Common.Vector2;

namespace Spot.Physics.Aether;

/// <summary>
/// An Aether.Physics2D backend for the engine's 2D physics. Owns a <see cref="World"/> and mirrors the
/// scene's physics entities into it each step: entities with a 2D collider (box/circle) become Aether bodies
/// (dynamic/kinematic/static), are simulated, and their results are written back onto the
/// <see cref="TransformComponent"/>. Mirrors <see cref="Bepu.BepuPhysics3D"/>.
/// </summary>
/// <remarks>
/// Contract: dynamic bodies are driven by velocity (set <see cref="PhysicsBody2DComponent.Velocity"/> from
/// scripts); their transform is owned by the simulation. Kinematic/static bodies are the reverse — move them
/// by their transform. Physics entities are treated as root-level for transform sync. Aether is metric (MKS):
/// keep collider sizes in a sane range (~0.1–10 units) for a stable solve.
/// </remarks>
internal sealed class AetherPhysics2D : IPhysics2D
{
    private const float DegToRad = MathF.PI / 180f;
    private const float RadToDeg = 180f / MathF.PI;

    private readonly World _world = new(new AVec(0f, -9.81f));
    private readonly Dictionary<int, Tracked> _tracked = new();
    private readonly HashSet<int> _seen = new();
    private readonly List<int> _toRemove = new();
    private readonly List<ContactPair> _contactPairs = new();

    /// <inheritdoc />
    public IReadOnlyList<ContactPair> Contacts => _contactPairs;

    public void Step(Scene scene, float deltaTime)
    {
        _contactPairs.Clear();
        if (deltaTime <= 0f) return;

        try
        {
            _world.Gravity = ToAether(PhysicsSettings.Gravity2D);
            Sync(scene, deltaTime);
            _world.Step(deltaTime);
            WriteBack(scene);
            CollectContacts(scene);
        }
        catch (Exception ex)
        {
            Log.CoreError("AetherPhysics2D step failed: {0}", ex.Message);
        }
    }

    // --- Sync scene -> simulation -------------------------------------------------------------

    private void Sync(Scene scene, float dt)
    {
        _seen.Clear();

        foreach (var entity in scene.View<TransformComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var transform = entity.GetComponent<TransformComponent>();
            if (!transform.Enabled) continue;

            // Local scale (not WorldScale) so residual float noise from matrix decomposition never thrashes
            // the ShapeKey. Physics entities are treated as root-level, so local scale is the right value.
            var scale = new Vector2(transform.Scale.X, transform.Scale.Y);
            if (!TryDescribeCollider(entity, scale, out ColliderDesc desc)) continue;

            bool hasBody = entity.TryGetComponent(out PhysicsBody2DComponent? body) && body!.Enabled;
            BodyType type = BodyTypeOf(hasBody ? body : null);

            var key = new ShapeKey(desc.Type, Q(desc.A), Q(desc.B), Q(desc.Offset.X), Q(desc.Offset.Y));
            _seen.Add(entity.Id);

            AVec position = ToAether(new Vector2(transform.WorldPosition.X, transform.WorldPosition.Y));
            float rotation = transform.WorldRotation.Z * DegToRad;

            if (!_tracked.TryGetValue(entity.Id, out Tracked tracked) || !tracked.Key.Equals(key))
            {
                if (_tracked.TryGetValue(entity.Id, out Tracked old)) DestroyTracked(old);
                tracked = CreateTracked(entity.Id, key, desc, type, position, rotation);
                _tracked[entity.Id] = tracked;
            }

            Fixture fixture = tracked.Fixture;
            fixture.IsSensor = desc.IsTrigger;
            fixture.CollisionCategories = CategoryForLayer(desc.Layer);
            fixture.CollidesWith = MaskForLayer(desc.Layer);

            Body b = tracked.Body;
            b.BodyType = type;

            if (type == BodyType.Dynamic && hasBody)
            {
                b.FixedRotation = body!.FreezeRotation;
                b.LinearDamping = MathF.Max(0f, body.LinearDrag);
                b.Mass = MathF.Max(0.0001f, body.Mass);
                b.LinearVelocity = ToAether(body.Velocity);

                // Gravity scale: let the world gravity act at scale 1; otherwise ignore it and apply our own
                // scaled gravity as a force so per-body weightlessness / heavy-fall still works.
                if (MathF.Abs(body.GravityScale - 1f) < 1e-4f)
                {
                    b.IgnoreGravity = false;
                }
                else
                {
                    b.IgnoreGravity = true;
                    if (body.GravityScale != 0f)
                        b.ApplyForce(_world.Gravity * (body.GravityScale * b.Mass));
                }

                b.Awake = true;
            }
            else if (type == BodyType.Kinematic)
            {
                b.Position = position;
                b.Rotation = rotation;
                b.LinearVelocity = hasBody ? ToAether(body!.Velocity) : AVec.Zero;
                b.Awake = true;
            }
            else // static
            {
                b.Position = position;
                b.Rotation = rotation;
            }
        }

        // Remove bodies whose entity or collider disappeared.
        _toRemove.Clear();
        foreach (var kvp in _tracked)
        {
            if (!_seen.Contains(kvp.Key)) _toRemove.Add(kvp.Key);
        }
        foreach (int id in _toRemove)
        {
            DestroyTracked(_tracked[id]);
            _tracked.Remove(id);
        }
    }

    private Tracked CreateTracked(int entityId, ShapeKey key, ColliderDesc desc, BodyType type, AVec position, float rotation)
    {
        Body body = _world.CreateBody(position, rotation, type);
        body.Tag = entityId;

        Fixture fixture = desc.Type == 0
            ? body.CreateRectangle(desc.A, desc.B, 1f, ToAether(desc.Offset))
            : body.CreateCircle(desc.A, 1f, ToAether(desc.Offset));

        return new Tracked { Key = key, Body = body, Fixture = fixture };
    }

    private void DestroyTracked(Tracked tracked) => _world.Remove(tracked.Body);

    // --- Write simulation -> scene ------------------------------------------------------------

    private void WriteBack(Scene scene)
    {
        foreach (var kvp in _tracked)
        {
            Body body = kvp.Value.Body;
            if (body.BodyType != BodyType.Dynamic) continue; // kinematic/static poses are authored, not simulated

            var entity = scene.EntityById(kvp.Key);
            if (entity is null) continue;
            if (!entity.Value.TryGetComponent(out TransformComponent? transform)) continue;
            if (!entity.Value.TryGetComponent(out PhysicsBody2DComponent? phys)) continue;

            Vector2 p = ToNumerics(body.Position);
            transform!.Position = new Vector3(p.X, p.Y, transform.Position.Z);
            phys!.Velocity = ToNumerics(body.LinearVelocity);
            if (!phys.FreezeRotation)
            {
                Vector3 r = transform.Rotation;
                transform.Rotation = new Vector3(r.X, r.Y, body.Rotation * RadToDeg);
            }
        }
    }

    // --- Contacts ------------------------------------------------------------------------------

    private void CollectContacts(Scene scene)
    {
        ContactListHead head = _world.ContactList;
        for (Contact c = head.Next; c != head; c = c.Next)
        {
            if (!c.IsTouching) continue;

            Entity? a = EntityOf(scene, c.FixtureA);
            Entity? b = EntityOf(scene, c.FixtureB);
            if (a is null || b is null) continue;

            c.GetWorldManifold(out AVec normal, out nkast.Aether.Physics2D.Common.FixedArray2<AVec> points);
            bool isTrigger = c.FixtureA.IsSensor || c.FixtureB.IsSensor;

            // Aether's manifold normal points from A toward B; the dispatcher expects it from B toward A.
            var storedNormal = new Vector3(-normal.X, -normal.Y, 0f);
            var point = new Vector3(points[0].X, points[0].Y, 0f);
            _contactPairs.Add(new ContactPair(a.Value, b.Value, isTrigger, storedNormal, point));
        }
    }

    private static Entity? EntityOf(Scene scene, Fixture fixture) =>
        fixture.Body.Tag is int id ? scene.EntityById(id) : null;

    // --- Raycast -------------------------------------------------------------------------------

    public bool Raycast(Scene scene, Vector2 origin, Vector2 direction, float maxDistance, out RaycastHit2D hit)
    {
        hit = default;
        if (direction.LengthSquared() < 1e-12f || maxDistance <= 0f) return false;
        direction = Vector2.Normalize(direction);

        Fixture? bestFixture = null;
        AVec bestPoint = default;
        AVec bestNormal = default;
        float bestFraction = float.MaxValue;

        try
        {
            _world.RayCast((fixture, point, normal, fraction) =>
            {
                bestFixture = fixture;
                bestPoint = point;
                bestNormal = normal;
                bestFraction = fraction;
                return fraction; // clip the ray to the closest hit so far
            }, ToAether(origin), ToAether(origin + direction * maxDistance));
        }
        catch (Exception ex)
        {
            Log.CoreError("AetherPhysics2D raycast failed: {0}", ex.Message);
            return false;
        }

        if (bestFixture is null) return false;
        Entity? entity = EntityOf(scene, bestFixture);
        if (entity is null) return false;

        hit = new RaycastHit2D(
            entity.Value,
            ToNumerics(bestPoint),
            ToNumerics(bestNormal),
            bestFraction * maxDistance);
        return true;
    }

    // --- Shapes --------------------------------------------------------------------------------

    private static bool TryDescribeCollider(Entity entity, Vector2 scale, out ColliderDesc desc)
    {
        if (entity.TryGetComponent(out BoxCollider2DComponent? box) && box!.Enabled)
        {
            float w = MathF.Abs(box.Size.X * scale.X);
            float h = MathF.Abs(box.Size.Y * scale.Y);
            desc = new ColliderDesc(0, w, h, box.Offset * scale, box.IsTrigger, box.Layer);
            return true;
        }
        if (entity.TryGetComponent(out CircleCollider2DComponent? circle) && circle!.Enabled)
        {
            float r = MathF.Abs(circle.Radius * scale.X);
            desc = new ColliderDesc(1, r, 0f, circle.Offset * scale, circle.IsTrigger, circle.Layer);
            return true;
        }
        desc = default;
        return false;
    }

    private static BodyType BodyTypeOf(PhysicsBody2DComponent? body)
    {
        if (body is null) return BodyType.Static;
        if (body.IsKinematic) return BodyType.Kinematic;
        return body.IsDynamic ? BodyType.Dynamic : BodyType.Static;
    }

    // --- Collision layers -> Aether categories -------------------------------------------------

    // Aether exposes 31 collision categories (Cat1..Cat31); the engine's layer matrix uses 32 layers, so
    // layers are clamped to 0..30 for the mapping. Each collider advertises its own layer bit and collides
    // with the bitmask of layers PhysicsSettings says it should.
    private static Category CategoryForLayer(int layer) => (Category)(1 << Math.Clamp(layer, 0, 30));

    private static Category MaskForLayer(int layer)
    {
        int clamped = Math.Clamp(layer, 0, 30);
        int mask = 0;
        for (int j = 0; j <= 30; j++)
        {
            if (PhysicsSettings.LayersCollide(clamped, j)) mask |= 1 << j;
        }
        return (Category)mask;
    }

    // --- Conversions & helpers -----------------------------------------------------------------

    private static AVec ToAether(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToNumerics(AVec v) => new(v.X, v.Y);

    /// <summary>Rounds to 0.1 mm buckets so float noise doesn't trigger needless body recreation.</summary>
    private static float Q(float v) => MathF.Round(v, 4);

    public void Dispose()
    {
        try
        {
            _world.Clear();
        }
        catch (Exception ex)
        {
            Log.CoreError("AetherPhysics2D dispose failed: {0}", ex.Message);
        }
    }

    private struct Tracked
    {
        public ShapeKey Key;
        public Body Body;
        public Fixture Fixture;
    }

    // Shape identity only: a change forces body+fixture recreation. Body type, material, velocity, mass, and
    // layers are all applied to the existing body each step, so they don't belong here.
    private readonly record struct ShapeKey(int Type, float A, float B, float OffsetX, float OffsetY);

    private readonly record struct ColliderDesc(int Type, float A, float B, Vector2 Offset, bool IsTrigger, int Layer);
}
