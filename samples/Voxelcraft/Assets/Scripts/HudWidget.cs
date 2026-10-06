using System.Numerics;
using Spot.Engine.UI;
using Spot.Engine.Graphics;

namespace Voxelcraft;

/// <summary>
/// The whole interface, as one custom widget drawn with the engine's UI batch: the loading screen, the crosshair,
/// the hotbar with isometric block icons, the block name and notices, the clock, the debug screen (F3), the
/// inventory (E) and the pause menu with the controls. Sizes are UI units (the screen is 1080 tall).
/// </summary>
internal sealed class HudWidget : Widget
{
    private const float Slot = 64.0f;
    private const float Gap = 4.0f;
    private const int InventoryColumns = 9;
    private const float InventorySlot = 72.0f;

    private static readonly Vector4 Ink = new(1.0f, 1.0f, 1.0f, 1.0f);
    private static readonly Vector4 Muted = new(0.78f, 0.82f, 0.88f, 1.0f);
    private static readonly Vector4 Faint = new(0.6f, 0.64f, 0.7f, 1.0f);
    private static readonly Vector4 Accent = new(0.55f, 0.85f, 0.4f, 1.0f);
    private static readonly Vector4 Shadow = new(0.0f, 0.0f, 0.0f, 0.55f);

    private static readonly (string Keys, string Action)[] Controls =
    {
        ("WASD", "Move"),
        ("Mouse", "Look around"),
        ("Space", "Jump · swim up · fly up"),
        ("Space Space / F", "Fly on or off"),
        ("Ctrl / W W", "Sprint"),
        ("Shift", "Sneak · fly down"),
        ("Left click", "Break a block"),
        ("Right click", "Place the selected block"),
        ("Middle click", "Pick the block you look at"),
        ("1-9 / wheel", "Select a hotbar slot"),
        ("E", "Inventory"),
        ("T", "Time-lapse on or off"),
        ("N", "Skip to sunrise, noon, sunset, midnight"),
        ("F1 / F3", "Hide the interface / debug screen"),
    };

    private readonly GameHud _hud;
    private Vector2 _inventoryOrigin;
    private Vector2 _size;

    public HudWidget(GameHud hud)
    {
        _hud = hud;
        Name = "HUD";
    }

    /// <summary>Gets the block of the inventory slot under a point (UI units), if any.</summary>
    public BlockId? InventorySlotAt(Vector2 point)
    {
        Vector2 local = point - _inventoryOrigin;
        if (local.X < 0.0f || local.Y < 0.0f) return null;
        int column = (int)(local.X / InventorySlot);
        int row = (int)(local.Y / InventorySlot);
        if (column >= InventoryColumns) return null;
        int index = row * InventoryColumns + column;
        return index < Blocks.Placeable.Length ? Blocks.Placeable[index] : null;
    }

    protected override void OnDraw()
    {
        if (Parent is null) return;
        Vector4 screen = Parent.ScreenRect;
        _size = new Vector2(screen.Z, screen.W);
        Font font = UIRoot.DefaultFont;
        Game? game = Game.Current;
        PlayerController? player = PlayerController.Current;

        if (game is { Loaded: false })
        {
            DrawLoading(font, game.LoadProgress);
            return;
        }

        if (game is { HudHidden: true }) return;

        if (player is not null)
        {
            if (game is not { Paused: true } && game is not { InventoryOpen: true }) DrawCrosshair();
            DrawHotbar(font, player);
        }

        DrawClock(font);
        if (game is { ShowDebug: true }) DrawDebug(font, player);
        if (game is { Toast.Length: > 0 } && game.ToastAge < 2.5f) DrawToast(font, game.Toast, Fade(game.ToastAge, 2.5f));
        if (game is { InventoryOpen: true } && player is not null) DrawInventory(font, player);
        if (game is { Paused: true }) DrawPause(font);
    }

    private void DrawLoading(Font font, float progress)
    {
        UIRenderer.DrawQuad(Vector2.Zero, _size, new Vector4(0.07f, 0.09f, 0.12f, 1.0f));
        // A strip of the world's own blocks along the bottom.
        var icons = _hud.Icons;
        if (icons is not null)
        {
            float x = 0.0f;
            int i = 0;
            while (x < _size.X)
            {
                BlockId id = Blocks.Placeable[i++ % 12];
                UIRenderer.DrawQuad(new Vector2(x, _size.Y - 120.0f), new Vector2(64.0f), new Vector4(1, 1, 1, 0.18f), icons, _hud.IconUv(id));
                x += 72.0f;
            }
        }

        Vector2 center = _size * 0.5f;
        Centered(font, "VOXELCRAFT", center + new Vector2(0.0f, -90.0f), 72.0f, Ink);
        Centered(font, "Generating the world", center + new Vector2(0.0f, 0.0f), 22.0f, Muted);
        var bar = new Vector2(420.0f, 8.0f);
        Vector2 at = center + new Vector2(-bar.X * 0.5f, 50.0f);
        UIRenderer.DrawQuad(at, bar, new Vector4(1.0f, 1.0f, 1.0f, 0.12f));
        UIRenderer.DrawQuad(at, new Vector2(bar.X * Math.Clamp(progress, 0.0f, 1.0f), bar.Y), Accent);
        Centered(font, $"Seed {VoxelWorld.Current?.Seed ?? 0}", center + new Vector2(0.0f, 80.0f), 15.0f, Faint);
    }

    private void DrawCrosshair()
    {
        Vector2 c = new(MathF.Round(_size.X * 0.5f), MathF.Round(_size.Y * 0.5f));
        const float arm = 11.0f;
        const float t = 2.0f;
        var dark = new Vector4(0.0f, 0.0f, 0.0f, 0.45f);
        UIRenderer.DrawQuad(c - new Vector2(arm + 1, t * 0.5f + 1), new Vector2(arm * 2 + 2, t + 2), dark);
        UIRenderer.DrawQuad(c - new Vector2(t * 0.5f + 1, arm + 1), new Vector2(t + 2, arm * 2 + 2), dark);
        var light = new Vector4(1.0f, 1.0f, 1.0f, 0.9f);
        UIRenderer.DrawQuad(c - new Vector2(arm, t * 0.5f), new Vector2(arm * 2, t), light);
        UIRenderer.DrawQuad(c - new Vector2(t * 0.5f, arm), new Vector2(t, arm * 2), light);
    }

    private void DrawHotbar(Font font, PlayerController player)
    {
        float width = Slot * 9 + Gap * 8;
        var origin = new Vector2(MathF.Round((_size.X - width) * 0.5f), _size.Y - Slot - 26.0f);
        UIRenderer.DrawQuad(origin - new Vector2(8.0f), new Vector2(width + 16.0f, Slot + 16.0f), new Vector4(0.0f, 0.0f, 0.0f, 0.35f));

        for (int i = 0; i < 9; i++)
        {
            var at = new Vector2(origin.X + i * (Slot + Gap), origin.Y);
            bool selected = i == player.Selected;
            UIRenderer.DrawQuad(at, new Vector2(Slot), selected ? new Vector4(1.0f, 1.0f, 1.0f, 0.16f) : new Vector4(0.0f, 0.0f, 0.0f, 0.35f));
            if (_hud.Icons is { } icons)
            {
                float lift = selected ? -3.0f : 0.0f;
                UIRenderer.DrawQuad(at + new Vector2(8.0f, 8.0f + lift), new Vector2(Slot - 16.0f), Ink, icons, _hud.IconUv(player.Hotbar[i]));
            }

            UIRenderer.DrawText(font, (i + 1).ToString(), at + new Vector2(5.0f, 2.0f), 13.0f, new Vector4(1, 1, 1, selected ? 0.9f : 0.45f), TextLayoutOptions.Default);
            if (selected) Frame(at - new Vector2(2.0f), new Vector2(Slot + 4.0f), 3.0f, Ink);
        }

        // The name of the block just selected, above the bar.
        if (player.SinceSelect < 2.0f)
        {
            float alpha = Fade(player.SinceSelect, 2.0f);
            string name = Blocks.Get(player.SelectedBlock).Name;
            Centered(font, name, new Vector2(_size.X * 0.5f, origin.Y - 42.0f), 22.0f, new Vector4(1, 1, 1, alpha), shadow: true);
        }
    }

    private void DrawClock(Font font)
    {
        if (DayNightCycle.Current is not { } cycle) return;
        string text = $"Day {cycle.Day + 1}  ·  {cycle.Clock}{(cycle.TimeLapse ? "  ·  time-lapse" : "")}";
        Vector2 measured = UIRenderer.MeasureText(font, text, 17.0f, TextLayoutOptions.Default);
        var at = new Vector2(_size.X - measured.X - 28.0f, 22.0f);
        UIRenderer.DrawQuad(at - new Vector2(12.0f, 7.0f), measured + new Vector2(24.0f, 14.0f), new Vector4(0.0f, 0.0f, 0.0f, 0.3f));

        // The sun or moon beside it: a small square, warm by day, pale by night.
        bool day = cycle.SunDirection.Y > -0.05f;
        UIRenderer.DrawQuad(at + new Vector2(-34.0f, 3.0f), new Vector2(12.0f), day ? new Vector4(1.0f, 0.86f, 0.4f, 1.0f) : new Vector4(0.82f, 0.86f, 0.95f, 1.0f));
        UIRenderer.DrawText(font, text, at, 17.0f, Muted, TextLayoutOptions.Default);
    }

    private void DrawDebug(Font font, PlayerController? player)
    {
        World? world = VoxelWorld.Current?.World;
        var lines = new List<string>
        {
            $"Voxelcraft  ·  {_hud.Fps:0} fps  ({_hud.FrameMs:0.0} ms)",
        };

        if (world is not null)
        {
            WorldRenderer? renderer = WorldRenderer.Current;
            lines.Add($"Chunks: {world.Chunks.Count} loaded, {renderer?.VisibleChunks ?? 0} drawn  ·  {(renderer?.DrawnQuads ?? 0) / 1000.0f:0.0}k quads");
            lines.Add($"Render distance: {world.RenderDistance}  ·  jobs: {world.PendingJobs} on {world.Workers} workers  ·  stream {world.UpdateMilliseconds:0.0} ms");
        }

        if (player is not null)
        {
            Vector3 p = player.Position;
            lines.Add($"XYZ: {p.X:0.00} / {p.Y:0.00} / {p.Z:0.00}");
            lines.Add($"Chunk: {World.ChunkCoord((int)MathF.Floor(p.X))}, {World.ChunkCoord((int)MathF.Floor(p.Z))}  ·  Facing: {Facing(player.Yaw)}");
            if (world is not null)
            {
                Column column = world.Generator.ColumnAt((int)MathF.Floor(p.X), (int)MathF.Floor(p.Z));
                lines.Add($"Biome: {column.Biome}  ·  temperature {column.Temperature:0.00}, humidity {column.Humidity:0.00}");
            }

            string mode = player.Flying ? "flying" : player.InWater ? "swimming" : player.Sneaking ? "sneaking" : player.Sprinting ? "sprinting" : player.OnGround ? "walking" : "falling";
            lines.Add($"Mode: {mode}  ·  speed {new Vector2(player.Velocity.X, player.Velocity.Z).Length():0.0} m/s");
            if (player.Target is { } hit)
            {
                lines.Add($"Looking at: {Blocks.Get(hit.Block).Name} ({hit.Position.X}, {hit.Position.Y}, {hit.Position.Z})");
            }
        }

        if (DayNightCycle.Current is { } cycle) lines.Add($"Time: {cycle.TimeOfDay:0.000}  ·  daylight {cycle.Daylight:0.00}");
        lines.Add($"Seed: {VoxelWorld.Current?.Seed ?? 0}");

        float y = 20.0f;
        foreach (string line in lines)
        {
            Vector2 size = UIRenderer.MeasureText(font, line, 16.0f, TextLayoutOptions.Default);
            UIRenderer.DrawQuad(new Vector2(16.0f, y - 2.0f), size + new Vector2(12.0f, 6.0f), new Vector4(0.0f, 0.0f, 0.0f, 0.45f));
            UIRenderer.DrawText(font, line, new Vector2(22.0f, y), 16.0f, Ink, TextLayoutOptions.Default);
            y += 24.0f;
        }
    }

    private void DrawToast(Font font, string text, float alpha)
    {
        Vector2 size = UIRenderer.MeasureText(font, text, 18.0f, TextLayoutOptions.Default);
        var at = new Vector2(MathF.Round((_size.X - size.X) * 0.5f), 90.0f);
        UIRenderer.DrawQuad(at - new Vector2(18.0f, 9.0f), size + new Vector2(36.0f, 18.0f), new Vector4(0.0f, 0.0f, 0.0f, 0.45f * alpha));
        UIRenderer.DrawText(font, text, at, 18.0f, new Vector4(1, 1, 1, alpha), TextLayoutOptions.Default);
    }

    private void DrawInventory(Font font, PlayerController player)
    {
        UIRenderer.DrawQuad(Vector2.Zero, _size, new Vector4(0.0f, 0.0f, 0.0f, 0.45f));
        int rows = (Blocks.Placeable.Length + InventoryColumns - 1) / InventoryColumns;
        var grid = new Vector2(InventoryColumns * InventorySlot, rows * InventorySlot);
        var panel = grid + new Vector2(48.0f, 130.0f);
        Vector2 at = ((_size - panel) * 0.5f).Round();
        UIRenderer.DrawQuad(at, panel, new Vector4(0.1f, 0.12f, 0.15f, 0.94f));
        Frame(at, panel, 1.0f, new Vector4(1, 1, 1, 0.12f));
        UIRenderer.DrawText(font, "Blocks", at + new Vector2(24.0f, 18.0f), 26.0f, Ink, TextLayoutOptions.Default);
        UIRenderer.DrawText(font, "Click a block to put it in the selected slot  ·  1-9 picks the slot  ·  E or Esc closes",
            at + new Vector2(24.0f, panel.Y - 34.0f), 14.0f, Faint, TextLayoutOptions.Default);

        _inventoryOrigin = at + new Vector2(24.0f, 64.0f);
        BlockId? hovered = InventorySlotAt(_hud.Pointer);
        for (int i = 0; i < Blocks.Placeable.Length; i++)
        {
            BlockId id = Blocks.Placeable[i];
            var cell = _inventoryOrigin + new Vector2(i % InventoryColumns * InventorySlot, i / InventoryColumns * InventorySlot);
            bool hot = hovered == id;
            bool current = player.SelectedBlock == id;
            UIRenderer.DrawQuad(cell + new Vector2(3.0f), new Vector2(InventorySlot - 6.0f), hot ? new Vector4(1, 1, 1, 0.16f) : new Vector4(0, 0, 0, 0.3f));
            if (current) Frame(cell + new Vector2(3.0f), new Vector2(InventorySlot - 6.0f), 2.0f, Accent);
            if (_hud.Icons is { } icons)
            {
                UIRenderer.DrawQuad(cell + new Vector2(12.0f), new Vector2(InventorySlot - 24.0f), Ink, icons, _hud.IconUv(id));
            }
        }

        if (hovered is { } block)
        {
            string name = Blocks.Get(block).Name;
            Vector2 size = UIRenderer.MeasureText(font, name, 16.0f, TextLayoutOptions.Default);
            Vector2 tip = _hud.Pointer + new Vector2(18.0f, -6.0f);
            UIRenderer.DrawQuad(tip - new Vector2(8.0f, 5.0f), size + new Vector2(16.0f, 10.0f), new Vector4(0.03f, 0.03f, 0.05f, 0.95f));
            UIRenderer.DrawText(font, name, tip, 16.0f, Ink, TextLayoutOptions.Default);
        }
    }

    private void DrawPause(Font font)
    {
        UIRenderer.DrawQuad(Vector2.Zero, _size, new Vector4(0.02f, 0.03f, 0.05f, 0.5f));
        float columnWidth = 520.0f;
        float top = MathF.Round(_size.Y * 0.5f - 300.0f);
        float cx = _size.X * 0.5f;
        var card = new Vector2(columnWidth + 120.0f, 120.0f + 30.0f * Controls.Length + 80.0f);
        var cardAt = new Vector2(MathF.Round(cx - card.X * 0.5f), top - 40.0f);
        UIRenderer.DrawQuad(cardAt, card, new Vector4(0.08f, 0.1f, 0.13f, 0.9f));
        Frame(cardAt, card, 1.0f, new Vector4(1, 1, 1, 0.1f));
        Centered(font, "Paused", new Vector2(cx, top), 54.0f, Ink);
        Centered(font, "Click or press Esc to resume", new Vector2(cx, top + 70.0f), 18.0f, Accent);

        float y = top + 130.0f;
        float left = MathF.Round(cx - columnWidth * 0.5f);
        foreach ((string keys, string action) in Controls)
        {
            Vector2 size = UIRenderer.MeasureText(font, keys, 15.0f, TextLayoutOptions.Default);
            UIRenderer.DrawQuad(new Vector2(left, y - 3.0f), size + new Vector2(16.0f, 8.0f), new Vector4(1, 1, 1, 0.1f));
            UIRenderer.DrawText(font, keys, new Vector2(left + 8.0f, y), 15.0f, Ink, TextLayoutOptions.Default);
            UIRenderer.DrawText(font, action, new Vector2(left + 190.0f, y), 15.0f, Muted, TextLayoutOptions.Default);
            y += 30.0f;
        }

        Centered(font, $"Seed {VoxelWorld.Current?.Seed ?? 0}", new Vector2(cx, y + 16.0f), 14.0f, Faint);
    }

    // ---- helpers ----

    private static void Centered(Font font, string text, Vector2 center, float size, Vector4 color, bool shadow = false)
    {
        Vector2 measured = UIRenderer.MeasureText(font, text, size, TextLayoutOptions.Default);
        Vector2 at = (center - measured * 0.5f).Round();
        if (shadow) UIRenderer.DrawText(font, text, at + new Vector2(2.0f), size, Shadow with { W = Shadow.W * color.W }, TextLayoutOptions.Default);
        UIRenderer.DrawText(font, text, at, size, color, TextLayoutOptions.Default);
    }

    private static void Frame(Vector2 at, Vector2 size, float t, Vector4 color)
    {
        UIRenderer.DrawQuad(at, new Vector2(size.X, t), color);
        UIRenderer.DrawQuad(at + new Vector2(0.0f, size.Y - t), new Vector2(size.X, t), color);
        UIRenderer.DrawQuad(at + new Vector2(0.0f, t), new Vector2(t, size.Y - t * 2), color);
        UIRenderer.DrawQuad(at + new Vector2(size.X - t, t), new Vector2(t, size.Y - t * 2), color);
    }

    private static float Fade(float age, float lifetime) => Math.Clamp((lifetime - age) / 0.4f, 0.0f, 1.0f) * Math.Clamp(age / 0.12f, 0.0f, 1.0f);

    private static string Facing(float yaw)
    {
        // Yaw 0 looks down -Z (north); it grows turning left, toward west.
        string[] names = { "north", "west", "south", "east" };
        return names[(int)MathF.Round(yaw / 90.0f) % 4];
    }
}

internal static class VectorRounding
{
    public static Vector2 Round(this Vector2 v) => new(MathF.Round(v.X), MathF.Round(v.Y));
}
