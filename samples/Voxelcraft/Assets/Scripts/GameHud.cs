using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Engine;
using Spot.Engine.Graphics;

namespace Voxelcraft;

/// <summary>
/// Puts the <see cref="HudWidget"/> on the scene's UI, renders the block icons it shows (isometric, from the same
/// procedural atlas as the world), measures the frame rate, and handles the inventory's clicks.
/// </summary>
public sealed class GameHud : Component
{
    private const int IconSize = 64;
    private const int IconColumns = 8;

    private HudWidget? _widget;

    /// <summary>Gets the icon sheet: one icon per block id.</summary>
    public Texture2D? Icons { get; private set; }

    public float Fps { get; private set; } = 60.0f;

    public float FrameMs { get; private set; } = 16.7f;

    /// <summary>Gets the mouse position in UI units.</summary>
    public Vector2 Pointer { get; private set; }

    public override void OnStart()
    {
        try
        {
            BlockAtlas.Build();
            Icons = BuildIcons();
        }
        catch (Exception ex)
        {
            Log.Error("Voxelcraft: could not build the block icons: {0}", ex.Message);
        }

        _widget = UI.Add(new HudWidget(this));
    }

    public override void OnDestroy()
    {
        if (_widget is not null) UI.Remove(_widget);
        Icons?.Dispose();
        Icons = null;
    }

    /// <summary>Gets the UV rectangle of a block's icon in <see cref="Icons"/>.</summary>
    public Vector4 IconUv(BlockId id)
    {
        int i = (int)id;
        int rows = ((int)BlockId.Count + IconColumns - 1) / IconColumns;
        float u = (i % IconColumns) / (float)IconColumns;
        float v = (i / IconColumns) / (float)rows;
        return new Vector4(u, v, u + 1.0f / IconColumns, v + 1.0f / rows);
    }

    public override void OnUpdate(float deltaTime)
    {
        float dt = Time.UnscaledDeltaTime;
        if (dt > 0.0f)
        {
            FrameMs += (dt * 1000.0f - FrameMs) * 0.05f;
            Fps = 1000.0f / MathF.Max(FrameMs, 0.01f);
        }

        Pointer = UI.Scale > 0.0f ? Input.MousePosition / UI.Scale : Input.MousePosition;

        if (Game.Current is { InventoryOpen: true } && PlayerController.Current is { } player && _widget is not null)
        {
            for (int i = 0; i < 9; i++)
            {
                if (Input.GetKeyDown(Key.Alpha1 + i)) player.Select(i);
            }

            if (Input.GetMouseButtonDown(MouseButton.Left) && _widget.InventorySlotAt(Pointer) is { } block)
            {
                player.Hotbar[player.Selected] = block;
                player.Select(player.Selected);
            }
        }
    }

    private static Texture2D BuildIcons()
    {
        int rows = ((int)BlockId.Count + IconColumns - 1) / IconColumns;
        int width = IconColumns * IconSize;
        int height = rows * IconSize;
        var pixels = new byte[width * height * 4];
        for (int i = 1; i < (int)BlockId.Count; i++)
        {
            if ((BlockId)i == BlockId.Water) continue;
            BlockAtlas.DrawIcon((BlockId)i, pixels, width, (i % IconColumns) * IconSize, (i / IconColumns) * IconSize, IconSize);
        }

        return new Texture2D((uint)width, (uint)height, pixels);
    }
}
