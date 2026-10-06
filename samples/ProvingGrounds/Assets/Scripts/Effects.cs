using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine.Graphics;
using Spot.Engine.Scenes;
using Spot.Engine;
using Spot.Engine;
using Spot.Engine.Graphics;

namespace ProvingGrounds;

/// <summary>What a bullet hit, which picks its impact effect.</summary>
public enum Surface
{
    Concrete,
    Metal,
    Wood,
}

/// <summary>
/// Every transient effect in the game, from one entity. Two custom render passes draw what the engine has no
/// component for, with the framework's <see cref="BillboardBatch"/>: bullet holes right after the opaque pass, and
/// tracers, velocity-stretched sparks, muzzle flashes and shockwaves inside the HDR capture so they bloom. Fire,
/// smoke and dust use the engine's own <see cref="ParticleSystemComponent"/> through small pools of emitters, and
/// short-lived point lights light the scene for muzzle flashes and explosions.
/// </summary>
public sealed class Effects : Component
{
    /// <summary>Gets or sets how many bullet holes stay on surfaces before the oldest is recycled.</summary>
    public int MaxDecals { get; set; } = 160;

    /// <summary>Gets or sets how long a bullet hole lasts, in seconds.</summary>
    public float DecalLifetime { get; set; } = 30.0f;

    /// <summary>Gets the effects of the playing scene.</summary>
    public static Effects? Current { get; private set; }

    private readonly List<TracerState> _tracers = new();
    private readonly List<Spark> _sparks = new();
    private readonly List<Flash> _flashes = new();
    private readonly List<Shockwave> _rings = new();
    private readonly List<Decal> _decals = new();

    private FxTextures? _textures;
    private DelegateRenderPass? _decalPass;
    private DelegateRenderPass? _glowPass;
    private EmitterPool? _fire;
    private EmitterPool? _smoke;
    private EmitterPool? _dust;
    private LightPool? _lights;

    public override void OnStart()
    {
        Current = this;
        _textures = new FxTextures();
        _decalPass = new DelegateRenderPass(RenderStage.AfterOpaque, DrawDecals, name: "Bullet Holes");
        _glowPass = new DelegateRenderPass(RenderStage.AfterTransparent, DrawGlow, name: "Weapon Effects");
        Scene.AddRenderPass(_decalPass);
        Scene.AddRenderPass(_glowPass);

        _fire = new EmitterPool(Scene, "Fire", 4, () => new ParticleSystemComponent
        {
            Shape = ParticleEmitterShape.Sphere, Radius = 0.5f, MaxParticles = 160, StartLifetime = 0.6f, StartSpeed = 7.5f,
            StartSize = 2.2f, EndSize = 0.5f, StartColor = new Vector4(3.2f, 1.5f, 0.4f, 1.0f), EndColor = new Vector4(0.8f, 0.12f, 0.02f, 0.0f),
            Damping = 4.5f, Gravity = -3.0f, Randomness = 0.6f, SpinSpeed = 90.0f, Blend = ParticleBlend.Additive,
        });
        _smoke = new EmitterPool(Scene, "Smoke", 4, () => new ParticleSystemComponent
        {
            Shape = ParticleEmitterShape.Sphere, Radius = 0.8f, MaxParticles = 120, StartLifetime = 3.0f, StartSpeed = 3.2f,
            StartSize = 1.6f, EndSize = 4.8f, StartColor = new Vector4(0.2f, 0.2f, 0.22f, 0.6f), EndColor = new Vector4(0.42f, 0.42f, 0.45f, 0.0f),
            Damping = 1.6f, Gravity = -1.1f, Randomness = 0.55f, SpinSpeed = 25.0f, Blend = ParticleBlend.Alpha,
        });
        _dust = new EmitterPool(Scene, "Dust", 6, () => new ParticleSystemComponent
        {
            Shape = ParticleEmitterShape.Cone, ConeAngle = 40.0f, Radius = 0.03f, MaxParticles = 60, StartLifetime = 0.7f,
            StartSpeed = 1.8f, StartSize = 0.14f, EndSize = 0.55f, StartColor = new Vector4(0.78f, 0.76f, 0.72f, 0.5f),
            EndColor = new Vector4(0.8f, 0.79f, 0.76f, 0.0f), Damping = 3.5f, Gravity = 0.6f, Randomness = 0.5f, Blend = ParticleBlend.Alpha,
        });
        _lights = new LightPool(Scene, 6);
    }

    public override void OnDestroy()
    {
        if (_decalPass is not null) Scene.RemoveRenderPass(_decalPass);
        if (_glowPass is not null) Scene.RemoveRenderPass(_glowPass);
        _textures?.Dispose();
        if (Current == this) Current = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        _fire?.Update();
        _smoke?.Update();
        _dust?.Update();
        _lights?.Update(deltaTime);

        for (int i = _tracers.Count - 1; i >= 0; i--)
        {
            TracerState tracer = _tracers[i];
            tracer.Age += deltaTime;
            if (tracer.Age * tracer.Speed - tracer.Length > tracer.Distance) _tracers.RemoveAt(i);
        }

        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            Spark spark = _sparks[i];
            spark.Age += deltaTime;
            if (spark.Age >= spark.Life)
            {
                _sparks.RemoveAt(i);
                continue;
            }

            spark.Velocity += new Vector3(0.0f, -spark.Gravity * deltaTime, 0.0f);
            spark.Velocity *= MathF.Max(0.0f, 1.0f - 1.8f * deltaTime);
            spark.Position += spark.Velocity * deltaTime;
        }

        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            _flashes[i].Age += deltaTime;
            if (_flashes[i].Age >= _flashes[i].Life) _flashes.RemoveAt(i);
        }

        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            _rings[i].Age += deltaTime;
            if (_rings[i].Age >= _rings[i].Life) _rings.RemoveAt(i);
        }

        for (int i = _decals.Count - 1; i >= 0; i--)
        {
            Decal decal = _decals[i];
            decal.Age += deltaTime;
            if (decal.Age >= DecalLifetime || (decal.Attached && !decal.Surface.IsValid)) _decals.RemoveAt(i);
        }
    }

    /// <summary>A tracer streaking from the muzzle to where the bullet landed.</summary>
    public void Tracer(Vector3 from, Vector3 to, Vector4 color)
    {
        float distance = Vector3.Distance(from, to);
        if (distance < 0.5f) return;
        _tracers.Add(new TracerState { From = from, To = to, Distance = distance, Color = color, Speed = 320.0f, Length = MathF.Min(9.0f, distance) });
    }

    /// <summary>A muzzle flash: a spiky star, a soft glow and a brief light.</summary>
    public void MuzzleFlash(Vector3 at, Vector4 color, float size)
    {
        float spin = Random.Shared.NextSingle() * MathF.Tau;
        _flashes.Add(new Flash { Position = at, Color = color, Size = size, Life = 0.05f, Rotation = spin, Texture = FlashKind.Star });
        _flashes.Add(new Flash { Position = at, Color = color * new Vector4(0.35f, 0.35f, 0.35f, 0.5f), Size = size * 1.5f, Life = 0.06f, Texture = FlashKind.Glow });
        _lights?.Flash(at, new Vector3(color.X, color.Y, color.Z) / MathF.Max(1.0f, MathF.Max(color.X, MathF.Max(color.Y, color.Z))), 2.4f, 6.0f, 0.06f);
    }

    /// <summary>Throws sparks from a point, around a direction.</summary>
    public void Sparks(Vector3 at, Vector3 direction, int count, float speed, Vector4 color, float spread = 0.7f, float gravity = 14.0f)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 jitter = RandomUnit() * spread;
            Vector3 dir = Vector3.Normalize(direction + jitter);
            float s = speed * (0.35f + 0.65f * Random.Shared.NextSingle());
            _sparks.Add(new Spark
            {
                Position = at, Velocity = dir * s, Color = color, Gravity = gravity,
                Life = 0.18f + 0.3f * Random.Shared.NextSingle(), Width = 0.012f + 0.01f * Random.Shared.NextSingle(),
            });
        }
    }

    /// <summary>A bullet's impact: sparks or dust by surface, a puff, a hole and a sound.</summary>
    public void Impact(in RaycastHit hit, Vector3 shotDirection, Surface surface, bool decal)
    {
        Vector3 bounce = Vector3.Reflect(shotDirection, hit.Normal);
        switch (surface)
        {
            case Surface.Metal:
                Sparks(hit.Point, Vector3.Normalize(bounce + hit.Normal), 14, 9.0f, new Vector4(4.0f, 2.6f, 1.2f, 1.0f), 0.8f);
                Sfx.PlayAt(Sfx.MetalHit, hit.Point, 0.45f, 3.0f, 0.12f);
                break;
            case Surface.Wood:
                Sparks(hit.Point, Vector3.Normalize(bounce + hit.Normal), 5, 4.0f, new Vector4(2.2f, 1.6f, 0.9f, 1.0f), 0.9f);
                _dust?.Burst(hit.Point, hit.Normal, 4);
                Sfx.PlayAt(Sfx.Thud, hit.Point, 0.35f, 3.0f, 0.2f);
                break;
            default:
                Sparks(hit.Point, Vector3.Normalize(bounce + hit.Normal * 1.5f), 6, 6.0f, new Vector4(3.0f, 2.2f, 1.3f, 1.0f), 0.6f);
                _dust?.Burst(hit.Point, hit.Normal, 6);
                Sfx.PlayAt(Sfx.Impact, hit.Point, 0.4f, 3.0f, 0.15f);
                break;
        }

        if (decal)
        {
            AddDecal(hit.Entity, hit.Point, hit.Normal, 0.07f + 0.02f * Random.Shared.NextSingle());
        }
    }

    /// <summary>An explosion: a flash, a fireball, smoke, sparks, a shockwave, a light, a sound and camera shake.</summary>
    public void Explosion(Vector3 at, float radius)
    {
        _flashes.Add(new Flash { Position = at, Color = new Vector4(9.0f, 5.5f, 2.4f, 1.0f), Size = radius * 1.5f, Life = 0.22f, Grow = true, Texture = FlashKind.Glow });
        _flashes.Add(new Flash
        {
            Position = at, Color = new Vector4(6.0f, 4.0f, 2.0f, 1.0f), Size = radius * 0.9f, Life = 0.09f,
            Rotation = Random.Shared.NextSingle() * MathF.Tau, Texture = FlashKind.Star,
        });
        _rings.Add(new Shockwave { Position = at + new Vector3(0.0f, 0.05f, 0.0f), Normal = Vector3.UnitY, Radius = radius * 1.6f, Life = 0.45f });
        _fire?.Burst(at, Vector3.UnitY, 26);
        _smoke?.Burst(at, Vector3.UnitY, 14);
        Sparks(at, Vector3.UnitY * 0.6f, 46, 20.0f, new Vector4(5.0f, 2.8f, 1.0f, 1.0f), 1.2f, 18.0f);
        _lights?.Flash(at + Vector3.UnitY * 0.5f, new Vector3(1.0f, 0.62f, 0.3f), 9.0f, radius * 3.5f, 0.45f);

        AudioExplosion(at);
        if (Playground.Current is { } game && game.Player.IsValid)
        {
            float distance = Vector3.Distance(game.Player.GetComponent<TransformComponent>().Position, at);
            game.AddTrauma(Math.Clamp(1.0f - distance / (radius * 6.0f), 0.0f, 1.0f) * 0.75f);
        }
    }

    /// <summary>A short ring of light on the ground, like a jump pad's launch.</summary>
    public void Pulse(Vector3 at, Vector3 normal, float radius, Vector4 color) =>
        _rings.Add(new Shockwave { Position = at, Normal = normal, Radius = radius, Life = 0.5f, Color = color });

    /// <summary>A puff of dust from a surface.</summary>
    public void Dust(Vector3 at, Vector3 normal, int count) => _dust?.Burst(at, normal, count);

    /// <summary>A little smoke, like a drone trailing it as it falls.</summary>
    public void Smoke(Vector3 at, int count) => _smoke?.Burst(at, Vector3.UnitY, count);

    /// <summary>Removes every bullet hole.</summary>
    public void ClearDecals() => _decals.Clear();

    private static void AudioExplosion(Vector3 at) => Sfx.PlayAt(Sfx.Explosion, at, 1.0f, 9.0f, 0.08f);

    // A hole stays on what it hit: on a moving prop it is kept in the prop's local space and follows it.
    private void AddDecal(Entity surface, Vector3 point, Vector3 normal, float size)
    {
        if (_decals.Count >= MaxDecals) _decals.RemoveAt(0);

        var decal = new Decal { Size = size, Rotation = Random.Shared.NextSingle() * MathF.Tau, Position = point, Normal = normal };
        if (surface.IsValid && surface.TryGetComponent(out PhysicsBody3DComponent? body) && body.IsDynamic && !body.IsKinematic
            && Matrix4x4.Invert(surface.GetComponent<TransformComponent>().Matrix, out Matrix4x4 inverse))
        {
            decal.Attached = true;
            decal.Surface = surface;
            decal.Position = Vector3.Transform(point, inverse);
            decal.Normal = Vector3.TransformNormal(normal, inverse);
        }

        _decals.Add(decal);
    }

    private void DrawDecals(RenderContext context)
    {
        if (_textures is null || _decals.Count == 0) return;

        BillboardBatch.Begin(context.ViewProjection);
        foreach (Decal decal in _decals)
        {
            Vector3 position = decal.Position;
            Vector3 normal = decal.Normal;
            if (decal.Attached)
            {
                Matrix4x4 world = decal.Surface.GetComponent<TransformComponent>().Matrix;
                position = Vector3.Transform(position, world);
                normal = Vector3.TransformNormal(normal, world);
            }

            normal = Vector3.Normalize(normal);
            Tangents(normal, decal.Rotation, out Vector3 tangent, out Vector3 bitangent);
            float fade = MathF.Min(1.0f, (DecalLifetime - decal.Age) / 3.0f);
            BillboardBatch.Draw(position + normal * 0.012f, tangent * decal.Size, bitangent * decal.Size, new Vector4(0.04f, 0.04f, 0.045f, 0.85f * fade),
                _textures.Hole);
        }

        BillboardBatch.End();
    }

    private void DrawGlow(RenderContext context)
    {
        if (_textures is null) return;
        if (_tracers.Count == 0 && _sparks.Count == 0 && _flashes.Count == 0 && _rings.Count == 0) return;

        Vector3 camera = context.CameraPosition;
        BillboardBatch.Begin(context.ViewProjection);

        foreach (TracerState tracer in _tracers)
        {
            float head = MathF.Min(tracer.Age * tracer.Speed, tracer.Distance);
            float tail = MathF.Max(0.0f, tracer.Age * tracer.Speed - tracer.Length);
            if (head - tail < 0.05f) continue;
            Vector3 dir = (tracer.To - tracer.From) / tracer.Distance;
            Streak(tracer.From + dir * tail, tracer.From + dir * head, camera, 0.035f, tracer.Color);
        }

        foreach (Spark spark in _sparks)
        {
            float t = spark.Age / spark.Life;
            Vector3 tail = spark.Position - spark.Velocity * 0.035f;
            Streak(tail, spark.Position, camera, spark.Width * (1.0f - t * 0.5f), spark.Color * new Vector4(1.0f, 1.0f, 1.0f, 1.0f - t));
        }

        foreach (Shockwave ring in _rings)
        {
            float t = ring.Age / ring.Life;
            float radius = ring.Radius * (1.0f - (1.0f - t) * (1.0f - t));
            Tangents(ring.Normal, 0.0f, out Vector3 tangent, out Vector3 bitangent);
            BillboardBatch.Draw(ring.Position, tangent * radius, bitangent * radius, ring.Color * new Vector4(1.0f, 1.0f, 1.0f, (1.0f - t) * 0.7f),
                _textures.Ring, BlendMode.Additive);
        }

        foreach (Flash flash in _flashes)
        {
            float t = flash.Age / flash.Life;
            float size = flash.Grow ? flash.Size * (0.3f + 0.7f * MathF.Sqrt(t)) : flash.Size * (1.0f - 0.3f * t);
            Facing(flash.Position, camera, flash.Rotation, out Vector3 right, out Vector3 up);
            Texture2D texture = flash.Texture == FlashKind.Star ? _textures.Flash : BillboardBatch.SoftDotTexture;
            BillboardBatch.Draw(flash.Position, right * size * 0.5f, up * size * 0.5f, flash.Color * new Vector4(1.0f, 1.0f, 1.0f, 1.0f - t * t),
                texture, BlendMode.Additive);
        }

        BillboardBatch.End();
    }

    // A quad from a to b, as wide as given, turned around its own axis to face the camera.
    private void Streak(Vector3 a, Vector3 b, Vector3 camera, float width, Vector4 color)
    {
        Vector3 axis = b - a;
        Vector3 mid = (a + b) * 0.5f;
        Vector3 side = Vector3.Cross(axis, camera - mid);
        if (side.LengthSquared() < 1e-10f) return;
        side = Vector3.Normalize(side) * width * 0.5f;
        BillboardBatch.Draw(mid, axis * 0.5f, side, color, _textures!.Streak, BlendMode.Additive);
    }

    private static void Facing(Vector3 position, Vector3 camera, float rotation, out Vector3 right, out Vector3 up)
    {
        Vector3 toCamera = camera - position;
        toCamera = toCamera.LengthSquared() > 1e-8f ? Vector3.Normalize(toCamera) : Vector3.UnitZ;
        Vector3 r = Vector3.Cross(Vector3.UnitY, toCamera);
        r = r.LengthSquared() > 1e-6f ? Vector3.Normalize(r) : Vector3.UnitX;
        Vector3 u = Vector3.Cross(toCamera, r);
        float c = MathF.Cos(rotation);
        float s = MathF.Sin(rotation);
        right = r * c + u * s;
        up = u * c - r * s;
    }

    private static void Tangents(Vector3 normal, float rotation, out Vector3 tangent, out Vector3 bitangent)
    {
        Vector3 reference = MathF.Abs(normal.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 t = Vector3.Normalize(Vector3.Cross(reference, normal));
        Vector3 b = Vector3.Cross(normal, t);
        float c = MathF.Cos(rotation);
        float s = MathF.Sin(rotation);
        tangent = t * c + b * s;
        bitangent = b * c - t * s;
    }

    private static Vector3 RandomUnit()
    {
        float z = Random.Shared.NextSingle() * 2.0f - 1.0f;
        float a = Random.Shared.NextSingle() * MathF.Tau;
        float r = MathF.Sqrt(1.0f - z * z);
        return new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z);
    }

    /// <summary>The Euler rotation (degrees) that turns an entity's +Y toward <paramref name="direction"/>.</summary>
    public static Vector3 EulerFromUp(Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        float pitch = MathF.Acos(Math.Clamp(direction.Y, -1.0f, 1.0f));
        float yaw = MathF.Atan2(direction.X, direction.Z);
        return new Vector3(pitch, yaw, 0.0f) * (180.0f / MathF.PI);
    }

    private enum FlashKind
    {
        Star,
        Glow,
    }

    private sealed class TracerState
    {
        public Vector3 From;
        public Vector3 To;
        public float Distance;
        public float Speed;
        public float Length;
        public float Age;
        public Vector4 Color;
    }

    private sealed class Spark
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector4 Color;
        public float Gravity;
        public float Width;
        public float Age;
        public float Life;
    }

    private sealed class Flash
    {
        public Vector3 Position;
        public Vector4 Color;
        public float Size;
        public float Rotation;
        public float Age;
        public float Life;
        public bool Grow;
        public FlashKind Texture;
    }

    private sealed class Shockwave
    {
        public Vector3 Position;
        public Vector3 Normal;
        public float Radius;
        public float Age;
        public float Life;
        public Vector4 Color = new(3.0f, 2.0f, 1.1f, 1.0f);
    }

    private sealed class Decal
    {
        public Entity Surface;
        public bool Attached;
        public Vector3 Position;
        public Vector3 Normal;
        public float Size;
        public float Rotation;
        public float Age;
    }

    /// <summary>
    /// A few entities carrying the same <see cref="ParticleSystemComponent"/> preset, used round-robin for bursts. A
    /// world-space emitter launches particles from where its entity was when the particle system last ran, so a
    /// burst is placed now and emitted on the next frame, once the system has seen the new place.
    /// </summary>
    private sealed class EmitterPool
    {
        private readonly List<(Entity Entity, ParticleSystemComponent Particles)> _emitters = new();
        private readonly List<(int Index, int Count, long Frame)> _pending = new();
        private int _next;

        public EmitterPool(Scene scene, string name, int size, Func<ParticleSystemComponent> preset)
        {
            for (int i = 0; i < size; i++)
            {
                Entity entity = scene.Instantiate($"{name} Emitter");
                ParticleSystemComponent particles = preset();
                particles.PlayOnAwake = false;
                particles.Looping = false;
                particles.EmissionRate = 0.0f;
                particles.Space = ParticleSimulationSpace.World;
                entity.AddComponent(particles);
                _emitters.Add((entity, particles));
            }
        }

        public void Burst(Vector3 at, Vector3 direction, int count)
        {
            int index = _next;
            _next = (_next + 1) % _emitters.Count;
            TransformComponent transform = _emitters[index].Entity.GetComponent<TransformComponent>();
            transform.Position = at;
            transform.Rotation = EulerFromUp(direction);
            _pending.Add((index, count, Time.FrameCount));
        }

        public void Update()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                (int index, int count, long frame) = _pending[i];
                if (Time.FrameCount <= frame) continue;
                _emitters[index].Particles.Emit(count);
                _pending.RemoveAt(i);
            }
        }
    }

    /// <summary>Point lights that blink on for a moment — muzzle flashes and explosions — and fade out.</summary>
    private sealed class LightPool
    {
        private readonly List<Slot> _slots = new();

        public LightPool(Scene scene, int size)
        {
            for (int i = 0; i < size; i++)
            {
                Entity entity = scene.Instantiate("Flash Light");
                var light = entity.AddComponent(new LightComponent { Type = LightType.Point, CastShadows = false, Enabled = false });
                _slots.Add(new Slot { Entity = entity, Light = light });
            }
        }

        public void Flash(Vector3 at, Vector3 color, float intensity, float range, float duration)
        {
            Slot slot = _slots[0];
            foreach (Slot candidate in _slots)
            {
                if (!candidate.Light.Enabled) { slot = candidate; break; }
                if (candidate.Age / candidate.Duration > slot.Age / slot.Duration) slot = candidate;
            }

            slot.Entity.GetComponent<TransformComponent>().Position = at;
            slot.Light.Color = color;
            slot.Light.Range = range;
            slot.Light.Enabled = true;
            slot.Peak = intensity;
            slot.Light.Intensity = intensity;
            slot.Age = 0.0f;
            slot.Duration = duration;
        }

        public void Update(float deltaTime)
        {
            foreach (Slot slot in _slots)
            {
                if (!slot.Light.Enabled) continue;
                slot.Age += deltaTime;
                float t = slot.Age / slot.Duration;
                if (t >= 1.0f)
                {
                    slot.Light.Enabled = false;
                    continue;
                }

                slot.Light.Intensity = slot.Peak * (1.0f - t) * (1.0f - t);
            }
        }

        private sealed class Slot
        {
            public Entity Entity;
            public LightComponent Light = null!;
            public float Peak;
            public float Age;
            public float Duration = 1.0f;
        }
    }
}
