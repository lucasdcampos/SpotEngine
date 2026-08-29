using System.Numerics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// A floating damage number: a short-lived world-space <see cref="TextComponent"/> that drifts upward and fades
/// out, then destroys itself. It is the Horde Survival demo's proof of the engine's world-space text — the same
/// billboarded text used for labels and nameplates. Spawn one with the static <see cref="Spawn"/>.
/// </summary>
public sealed class DamageNumber : EntityBehaviour
{
    private const float Lifetime = 0.6f;
    private const float RiseSpeed = 2.4f;

    private float _age;
    private Vector4 _baseColor = Vector4.One;

    /// <summary>Spawns a floating number at a world position that rises and fades over its short life.</summary>
    /// <param name="scene">The scene to spawn into.</param>
    /// <param name="position">The world-space start position (2D; drawn slightly in front of sprites).</param>
    /// <param name="amount">The number to show.</param>
    /// <param name="color">The text color at full opacity.</param>
    public static void Spawn(Scene scene, Vector2 position, int amount, Vector4 color)
    {
        Entity entity = scene.Instantiate("DamageNumber");
        entity.GetComponent<TransformComponent>().Position = new Vector3(position, 0.3f);

        entity.AddComponent(new TextComponent
        {
            Text = amount.ToString(),
            Color = color,
            FontSize = 48f,
            WorldScale = 0.014f,
            Alignment = TextAlign.Center,
            Billboard = true,
        });

        entity.AddScript<DamageNumber>()._baseColor = color;
    }

    public override void OnUpdate(float deltaTime)
    {
        _age += deltaTime;
        float t = _age / Lifetime;
        if (t >= 1.0f)
        {
            Destroy();
            return;
        }

        GetComponent<TransformComponent>().Position += new Vector3(0f, RiseSpeed * deltaTime, 0f);
        GetComponent<TextComponent>().Color = new Vector4(_baseColor.X, _baseColor.Y, _baseColor.Z, 1f - t);
    }
}
