using System;
using System.Numerics;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>The volume an emitter samples new particles' spawn positions and directions from.</summary>
public enum ParticleEmitterShape
{
    /// <summary>All particles spawn at the emitter origin and travel straight up (local +Y).</summary>
    Point,

    /// <summary>Particles spawn anywhere inside a box (<see cref="ParticleSystemComponent.BoxSize"/>) and travel up.</summary>
    Box,

    /// <summary>Particles spawn inside a sphere (<see cref="ParticleSystemComponent.Radius"/>) and travel outward.</summary>
    Sphere,

    /// <summary>Particles spawn on a disc and travel within a cone — a fountain/fire shape.</summary>
    Cone,
}

/// <summary>Whether particles live in the emitter's local space (following it) or in world space (left behind).</summary>
public enum ParticleSimulationSpace
{
    /// <summary>Particles are stored relative to the emitter, so moving/rotating the emitter carries them along.</summary>
    Local,

    /// <summary>Particles are stored in world space, so a moving emitter leaves a trail behind it.</summary>
    World,
}

/// <summary>How a particle quad is oriented when drawn.</summary>
public enum ParticleRenderMode
{
    /// <summary>The quad always faces the camera (screen-aligned) — the right choice for 3D scenes.</summary>
    Billboard3D,

    /// <summary>The quad lies flat on the world XY plane — the right choice for a 2D (orthographic) camera.</summary>
    Flat2D,
}

/// <summary>
/// A minimal CPU particle emitter: it spawns short-lived textured quads from a shaped volume and
/// animates their color and size over their lifetime. It is intentionally a <em>base</em> to build
/// effects (smoke, fire, sparks, dust, magic) on — not a full authoring stack. Like the other data
/// components it is plain data plus a small control surface for scripts
/// (<see cref="Play"/>/<see cref="Stop"/>/<see cref="Emit"/>); <c>ParticleSystem</c> advances it each
/// play-mode frame and <c>ParticleRenderSystem</c> draws it together with the entity's
/// <see cref="TransformComponent"/>. Simulation runs only in play mode, so particles are static in the
/// editor's edit view until you press Play.
/// </summary>
[ComponentMenu("Particle System", Order = 30)]
[SceneComponent("ParticleSystem")]
public sealed class ParticleSystemComponent : Component
{
    // ----- Runtime state (never serialized: internal fields, not public properties) ----------------

    private readonly Random _rng = new();
    private Particle[] _particles = Array.Empty<Particle>();
    private int _aliveCount;
    private float _emissionAccumulator;
    private float _playbackTime;
    private bool _playing;
    private bool _awakeHandled;

    // The owning emitter's world transform, refreshed each frame by ParticleSystem so a script's Emit()
    // (and world-space spawning) uses the entity's live position/rotation without the component needing a
    // back-reference to its entity.
    internal Matrix4x4 EmitterWorld = Matrix4x4.Identity;
    internal Matrix4x4 EmitterRotation = Matrix4x4.Identity;

    // ----- Playback --------------------------------------------------------------------------------

    /// <summary>Gets or sets whether the emitter starts playing automatically when the scene begins running.</summary>
    public bool PlayOnAwake { get; set; } = true;

    /// <summary>Gets or sets whether emission repeats forever. When off, emission stops after <see cref="Duration"/>.</summary>
    public bool Looping { get; set; } = true;

    /// <summary>Gets or sets how long (seconds) a non-looping emitter emits before stopping. Live particles still finish.</summary>
    [ShowIf(nameof(Looping), false)]
    [InspectorRange(0.0f, 120.0f, 0.1f)]
    public float Duration { get; set; } = 5.0f;

    // ----- Emission --------------------------------------------------------------------------------

    /// <summary>Gets or sets the hard cap on simultaneously alive particles. The buffer is sized to this.</summary>
    [InspectorRange(1.0f, 100000.0f, 1.0f)]
    public int MaxParticles { get; set; } = 256;

    /// <summary>Gets or sets how many particles are spawned per second.</summary>
    [InspectorRange(0.0f, 1000.0f, 1.0f)]
    public float EmissionRate { get; set; } = 24.0f;

    // ----- Emitter shape ---------------------------------------------------------------------------

    /// <summary>Gets or sets the volume new particles spawn from.</summary>
    public ParticleEmitterShape Shape { get; set; } = ParticleEmitterShape.Cone;

    /// <summary>Gets or sets the box extents (full size) for a <see cref="ParticleEmitterShape.Box"/> emitter.</summary>
    [ShowIf(nameof(Shape), ParticleEmitterShape.Box)]
    public Vector3 BoxSize { get; set; } = Vector3.One;

    /// <summary>Gets or sets the radius for a sphere or cone emitter.</summary>
    [ShowIf(nameof(Shape), ParticleEmitterShape.Sphere, ParticleEmitterShape.Cone)]
    [InspectorRange(0.0f, 50.0f, 0.05f)]
    public float Radius { get; set; } = 0.5f;

    /// <summary>Gets or sets the half-angle (degrees) of a <see cref="ParticleEmitterShape.Cone"/> emitter's spread.</summary>
    [ShowIf(nameof(Shape), ParticleEmitterShape.Cone)]
    [InspectorRange(0.0f, 90.0f, 0.5f)]
    public float ConeAngle { get; set; } = 25.0f;

    // ----- Initial particle state ------------------------------------------------------------------

    /// <summary>Gets or sets how long (seconds) each particle lives.</summary>
    [InspectorRange(0.01f, 60.0f, 0.05f)]
    public float StartLifetime { get; set; } = 2.0f;

    /// <summary>Gets or sets the initial speed particles leave the emitter with, along the shape's direction.</summary>
    [InspectorRange(0.0f, 50.0f, 0.05f)]
    public float StartSpeed { get; set; } = 2.0f;

    /// <summary>Gets or sets the particle quad's starting size, in world units.</summary>
    [InspectorRange(0.0f, 50.0f, 0.01f)]
    public float StartSize { get; set; } = 0.3f;

    /// <summary>Gets or sets the particle spin speed, in degrees per second.</summary>
    [InspectorRange(-720.0f, 720.0f, 1.0f)]
    public float SpinSpeed { get; set; }

    /// <summary>Gets or sets the color particles start at (RGBA, alpha included).</summary>
    [InspectorColor]
    public Vector4 StartColor { get; set; } = Vector4.One;

    // ----- Over-lifetime ---------------------------------------------------------------------------

    /// <summary>Gets or sets the color particles fade to over their life. Defaults to transparent white (fade out).</summary>
    [InspectorColor]
    public Vector4 EndColor { get; set; } = new(1.0f, 1.0f, 1.0f, 0.0f);

    /// <summary>Gets or sets the particle quad's size at the end of its life, in world units.</summary>
    [InspectorRange(0.0f, 50.0f, 0.01f)]
    public float EndSize { get; set; }

    /// <summary>Gets or sets a downward acceleration applied every frame (positive pulls particles down).</summary>
    [InspectorRange(-50.0f, 50.0f, 0.05f)]
    public float Gravity { get; set; }

    /// <summary>Gets or sets how quickly particle velocity decays (drag). 0 keeps momentum; higher slows faster.</summary>
    [InspectorRange(0.0f, 10.0f, 0.01f)]
    public float Damping { get; set; }

    /// <summary>Gets or sets how much per-particle random variation (0..1) is applied to life, speed, size and spin.</summary>
    [InspectorRange(0.0f, 1.0f, 0.01f)]
    public float Randomness { get; set; } = 0.2f;

    // ----- Simulation & rendering ------------------------------------------------------------------

    /// <summary>Gets or sets whether particles follow the emitter (<see cref="ParticleSimulationSpace.Local"/>) or are left in world space.</summary>
    public ParticleSimulationSpace Space { get; set; } = ParticleSimulationSpace.Local;

    /// <summary>Gets or sets whether particles billboard toward the camera (3D) or lie flat on the XY plane (2D).</summary>
    public ParticleRenderMode RenderMode { get; set; } = ParticleRenderMode.Billboard3D;

    /// <summary>Gets or sets how particle pixels combine with the scene: <see cref="ParticleBlend.Alpha"/> or <see cref="ParticleBlend.Additive"/> (glow).</summary>
    public ParticleBlend Blend { get; set; } = ParticleBlend.Alpha;

    /// <summary>Gets or sets the particle texture. When unset, a soft round dot is used so particles look good out of the box.</summary>
    [AssetReference(nameof(TexturePath))]
    public Texture2D? Texture { get; set; }

    /// <summary>Gets or sets the stored reference to the particle texture, used for serialization.</summary>
    [HideInInspector]
    public string? TexturePath { get; set; }

    // ----- Script control surface ------------------------------------------------------------------

    /// <summary>Gets whether the emitter is currently spawning particles.</summary>
    public bool IsPlaying => _playing;

    /// <summary>Gets how many particles are currently alive.</summary>
    public int AliveParticles => _aliveCount;

    /// <summary>Starts (or resumes) emission and resets the non-looping duration timer.</summary>
    public void Play()
    {
        _playing = true;
        _playbackTime = 0.0f;
    }

    /// <summary>Stops emitting new particles. Particles already alive keep simulating until they expire.</summary>
    public void Stop() => _playing = false;

    /// <summary>Removes every live particle immediately.</summary>
    public void Clear() => _aliveCount = 0;

    /// <summary>Spawns <paramref name="count"/> particles right now, regardless of emission rate (a burst).</summary>
    /// <param name="count">The number of particles to spawn.</param>
    public void Emit(int count)
    {
        EnsureCapacity();
        for (int i = 0; i < count; i++)
        {
            EmitOne();
        }
    }

    // ----- Internals used by ParticleSystem / ParticleRenderSystem (same assembly) ------------------

    internal Particle[] Buffer => _particles;

    internal int AliveCount => _aliveCount;

    /// <summary>
    /// Advances the emitter by <paramref name="deltaTime"/>: handles play-on-awake, rate-based emission
    /// (honoring a non-looping duration), and integrates every live particle (velocity, gravity, drag,
    /// spin, aging). Called by <c>ParticleSystem</c> each play-mode frame.
    /// </summary>
    internal void Simulate(float deltaTime)
    {
        EnsureCapacity();

        if (!_awakeHandled)
        {
            _awakeHandled = true;
            if (PlayOnAwake)
            {
                _playing = true;
            }
        }

        if (_playing)
        {
            _playbackTime += deltaTime;
            bool emitting = Looping || _playbackTime <= Duration;
            if (emitting && EmissionRate > 0.0f && StartLifetime > 0.0f)
            {
                _emissionAccumulator += EmissionRate * deltaTime;
                while (_emissionAccumulator >= 1.0f)
                {
                    _emissionAccumulator -= 1.0f;
                    EmitOne();
                }
            }
        }

        Vector3 gravity = new(0.0f, -Gravity, 0.0f);
        float damp = Math.Clamp(1.0f - Damping * deltaTime, 0.0f, 1.0f);

        for (int i = 0; i < _aliveCount;)
        {
            ref Particle p = ref _particles[i];
            p.Age += deltaTime;
            if (p.Age >= p.Lifetime)
            {
                // Swap-remove: overwrite the dead slot with the last live particle, shrink the count.
                _particles[i] = _particles[--_aliveCount];
                continue;
            }

            p.Velocity += gravity * deltaTime;
            p.Velocity *= damp;
            p.Position += p.Velocity * deltaTime;
            p.Rotation += p.AngularVelocity * deltaTime;
            i++;
        }
    }

    // Grows/shrinks the particle buffer to match MaxParticles, preserving live particles.
    private void EnsureCapacity()
    {
        int capacity = Math.Clamp(MaxParticles, 0, 100_000);
        if (_particles.Length == capacity)
        {
            return;
        }

        var resized = new Particle[capacity];
        int keep = Math.Min(_aliveCount, capacity);
        Array.Copy(_particles, resized, keep);
        _particles = resized;
        _aliveCount = keep;
    }

    // Spawns a single particle from the emitter shape, applying per-particle randomness and the current
    // simulation space. Silently does nothing when the buffer is full.
    private void EmitOne()
    {
        if (_aliveCount >= _particles.Length)
        {
            return;
        }

        (Vector3 localPos, Vector3 localDir) = SampleShape();

        float rand = Math.Clamp(Randomness, 0.0f, 1.0f);
        float lifetime = StartLifetime * (1.0f - rand * NextFloat());
        float speed = StartSpeed * (1.0f - rand * NextFloat());
        float sizeScale = 1.0f - rand * NextFloat();

        Vector3 velocity = localDir * speed;

        Vector3 position;
        if (Space == ParticleSimulationSpace.World)
        {
            position = Vector3.Transform(localPos, EmitterWorld);
            velocity = Vector3.TransformNormal(velocity, EmitterRotation);
        }
        else
        {
            position = localPos;
        }

        float spin = SpinSpeed * (MathF.PI / 180.0f);

        _particles[_aliveCount++] = new Particle
        {
            Position = position,
            Velocity = velocity,
            Age = 0.0f,
            Lifetime = MathF.Max(lifetime, 0.01f),
            SizeScale = sizeScale,
            Rotation = (NextFloat() * 2.0f - 1.0f) * MathF.PI * rand,
            AngularVelocity = spin,
        };
    }

    // Returns a spawn position and a unit travel direction in emitter-local space for the current shape.
    private (Vector3 Position, Vector3 Direction) SampleShape()
    {
        switch (Shape)
        {
            case ParticleEmitterShape.Box:
            {
                var pos = new Vector3(
                    (NextFloat() - 0.5f) * BoxSize.X,
                    (NextFloat() - 0.5f) * BoxSize.Y,
                    (NextFloat() - 0.5f) * BoxSize.Z);
                return (pos, Vector3.UnitY);
            }
            case ParticleEmitterShape.Sphere:
            {
                Vector3 dir = RandomUnitVector();
                float r = Radius * MathF.Cbrt(NextFloat());
                return (dir * r, dir);
            }
            case ParticleEmitterShape.Cone:
            {
                float angle = ConeAngle * (MathF.PI / 180.0f);
                float cosMax = MathF.Cos(angle);
                float cosT = 1.0f - NextFloat() * (1.0f - cosMax);
                float sinT = MathF.Sqrt(MathF.Max(0.0f, 1.0f - cosT * cosT));
                float phi = NextFloat() * MathF.Tau;
                var dir = new Vector3(sinT * MathF.Cos(phi), cosT, sinT * MathF.Sin(phi));

                float discRadius = Radius * MathF.Sqrt(NextFloat());
                float discPhi = NextFloat() * MathF.Tau;
                var pos = new Vector3(discRadius * MathF.Cos(discPhi), 0.0f, discRadius * MathF.Sin(discPhi));
                return (pos, dir);
            }
            default:
                return (Vector3.Zero, Vector3.UnitY);
        }
    }

    private Vector3 RandomUnitVector()
    {
        float z = 2.0f * NextFloat() - 1.0f;
        float t = MathF.Tau * NextFloat();
        float r = MathF.Sqrt(MathF.Max(0.0f, 1.0f - z * z));
        return new Vector3(r * MathF.Cos(t), r * MathF.Sin(t), z);
    }

    private float NextFloat() => (float)_rng.NextDouble();
}

/// <summary>One live particle. A plain struct kept in a pooled array so a busy emitter allocates nothing.</summary>
internal struct Particle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Age;
    public float Lifetime;
    public float SizeScale;
    public float Rotation;
    public float AngularVelocity;
}
