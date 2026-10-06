using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// Score popups in the world ("+100" over a bullseye): a small pool of entities carrying a billboarded
/// <see cref="TextRenderer"/>, reused rather than created per hit. Each rises, pops in scale and fades; its world
/// size grows with distance so it reads the same on screen near or far.
/// </summary>
internal sealed class WorldPopups : IDisposable
{
    private const int PoolSize = 24;
    private const float Lifetime = 1.1f;

    private readonly Scene _scene;
    private readonly List<Popup> _pool = new();
    private int _next;

    public WorldPopups(Scene scene)
    {
        _scene = scene;
        for (int i = 0; i < PoolSize; i++)
        {
            Entity entity = scene.Instantiate("Popup");
            var text = entity.AddComponent(new TextRenderer { Text = "", FontSize = 44.0f, Billboard = true, Enabled = false });
            _pool.Add(new Popup { Entity = entity, Text = text });
        }
    }

    public void Spawn(string text, Vector3 at, Vector4 color, float size = 1.0f)
    {
        Popup popup = _pool[_next];
        _next = (_next + 1) % _pool.Count;

        popup.Origin = at;
        popup.Age = 0.0f;
        popup.Color = color;
        popup.Size = size;
        popup.Drift = new Vector3(Random.Shared.NextSingle() - 0.5f, 0.0f, Random.Shared.NextSingle() - 0.5f) * 0.4f;
        popup.Text.Text = text;
        popup.Text.Enabled = true;
    }

    public void Update(float deltaTime)
    {
        Vector3 camera = CameraPosition();
        foreach (Popup popup in _pool)
        {
            if (!popup.Text.Enabled) continue;

            popup.Age += deltaTime;
            float t = popup.Age / Lifetime;
            if (t >= 1.0f || !popup.Entity.IsValid)
            {
                popup.Text.Enabled = false;
                continue;
            }

            float rise = 1.0f - (1.0f - t) * (1.0f - t);
            Vector3 position = popup.Origin + new Vector3(0.0f, 0.9f * rise, 0.0f) + popup.Drift * rise;
            popup.Entity.GetComponent<Transform>().Position = position;

            // Overshoot in, settle, then fade over the last half.
            float pop = t < 0.12f ? 0.6f + 0.65f * (t / 0.12f) : 1.25f - 0.25f * MathF.Min(1.0f, (t - 0.12f) / 0.15f);
            float distance = Vector3.Distance(position, camera);
            popup.Text.WorldScale = 0.008f * popup.Size * pop * MathF.Max(1.0f, distance / 7.0f);
            float alpha = t < 0.5f ? 1.0f : 1.0f - (t - 0.5f) / 0.5f;
            popup.Text.Color = new Vector4(popup.Color.X * 1.6f, popup.Color.Y * 1.6f, popup.Color.Z * 1.6f, alpha);
        }
    }

    public void Dispose()
    {
        foreach (Popup popup in _pool)
        {
            if (popup.Entity.IsValid)
            {
                _scene.Destroy(popup.Entity);
            }
        }

        _pool.Clear();
    }

    private Vector3 CameraPosition() =>
        _scene.TryGetActivePrimaryCamera(out Entity camera) ? camera.GetComponent<Transform>().WorldPosition : Vector3.Zero;

    private sealed class Popup
    {
        public Entity Entity;
        public TextRenderer Text = null!;
        public Vector3 Origin;
        public Vector3 Drift;
        public Vector4 Color;
        public float Size;
        public float Age;
    }
}
