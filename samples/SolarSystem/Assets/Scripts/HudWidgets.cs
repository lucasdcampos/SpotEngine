using System.Numerics;
using Spot.Engine.UI;
using Spot.Engine;
using Spot.Engine.Graphics;

namespace SolarSystem;

/// <summary>
/// The HUD's shared look: colors, and the generated textures every control draws with (created once, released
/// with the HUD).
/// </summary>
internal sealed class HudKit : IDisposable
{
    public static readonly Vector4 Ink = new(0.95f, 0.96f, 0.98f, 1.0f);
    public static readonly Vector4 Muted = new(0.62f, 0.67f, 0.76f, 1.0f);
    public static readonly Vector4 Faint = new(0.46f, 0.5f, 0.6f, 1.0f);
    public static readonly Vector4 Fill = new(0.03f, 0.038f, 0.06f, 0.9f);
    public static readonly Vector4 Accent = new(0.3f, 0.52f, 0.98f, 1.0f);
    public static readonly Vector4 FullUv = new(0.0f, 0.0f, 1.0f, 1.0f);

    public Texture2D Panel { get; } = UITextures.RoundedRect(14);
    public Texture2D Outline { get; } = UITextures.RoundedOutline(14);
    public Texture2D Shadow { get; } = UITextures.RoundedShadow(14, 18);
    public Texture2D Pill { get; } = UITextures.RoundedRect(10);
    public Texture2D Track { get; } = UITextures.RoundedRect(2);
    public Texture2D Keycap { get; } = UITextures.RoundedRect(5);
    public Texture2D Disc { get; } = UITextures.Disc();
    public Texture2D Ring { get; } = UITextures.Ring();
    public Texture2D PauseIcon { get; } = UITextures.Icon(IconShape.Pause);
    public Texture2D PlayIcon { get; } = UITextures.Icon(IconShape.Play);
    public Texture2D SettingsIcon { get; } = UITextures.Icon(IconShape.Settings);
    public Texture2D OverviewIcon { get; } = UITextures.Icon(IconShape.Overview);
    public Texture2D TourIcon { get; } = UITextures.Icon(IconShape.Tour);
    public Texture2D CloseIcon { get; } = UITextures.Icon(IconShape.Close);

    public void Dispose()
    {
        foreach (Texture2D texture in new[]
                 {
                     Panel, Outline, Shadow, Pill, Track, Keycap, Disc, Ring, PauseIcon, PlayIcon, SettingsIcon, OverviewIcon, TourIcon,
                     CloseIcon,
                 })
        {
            texture.Dispose();
        }
    }

    /// <summary>Draws a frosted card: a soft shadow, the dark fill and a faint outline.</summary>
    public void DrawCard(Vector2 at, Vector2 size, float opacity)
    {
        UIRenderer.DrawNineSlice(at - new Vector2(18.0f, 10.0f), size + new Vector2(36.0f, 36.0f), new Vector4(0.0f, 0.0f, 0.0f, 0.55f * opacity),
            Shadow, new Vector4(32.0f));
        UIRenderer.DrawNineSlice(at, size, Fade(Fill, opacity), Panel, new Vector4(14.0f));
        UIRenderer.DrawNineSlice(at, size, new Vector4(1.0f, 1.0f, 1.0f, 0.09f * opacity), Outline, new Vector4(14.0f));
    }

    /// <summary>Draws a key name on a small rounded cap; returns its width.</summary>
    public float DrawKeycap(string keys, Vector2 at, float fontSize, float opacity, bool alignRight = false)
    {
        Font font = UIRoot.DefaultFont;
        float width = UIRenderer.MeasureText(font, keys, fontSize, TextLayoutOptions.Default).X + 14.0f;
        float left = alignRight ? at.X - width : at.X;
        UIRenderer.DrawNineSlice(new Vector2(left, at.Y), new Vector2(width, fontSize + 9.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.1f * opacity), Keycap,
            new Vector4(5.0f));
        UIRenderer.DrawText(font, keys, new Vector2(left + 7.0f, at.Y + 3.5f), fontSize, Fade(Ink, opacity), TextLayoutOptions.Default);
        return width;
    }

    /// <summary>How visible a widget is: the opacity of the <see cref="GlassPanel"/> it sits on, if any.</summary>
    public static float Opacity(Widget widget)
    {
        for (Widget? w = widget; w is not null; w = w.Parent)
        {
            if (w is GlassPanel panel) return panel.Opacity;
        }

        return 1.0f;
    }

    public static Vector4 Fade(Vector4 color, float opacity) => color with { W = color.W * opacity };

    public static float Approach(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);
}

/// <summary>
/// A frosted panel that fades as a whole — every control on it reads its <see cref="Opacity"/> — and swallows the
/// clicks that land on it, so they don't fall through to the planets behind.
/// </summary>
internal sealed class GlassPanel : Panel
{
    private readonly HudKit _kit;

    public GlassPanel(HudKit kit) => _kit = kit;

    public float Opacity = 1.0f;

    protected override bool IsInteractive => true;

    protected override void OnDraw()
    {
        if (Opacity > 0.001f)
        {
            _kit.DrawCard(new Vector2(ScreenRect.X, ScreenRect.Y), new Vector2(ScreenRect.Z, ScreenRect.W), Opacity);
        }
    }
}

/// <summary>
/// A round icon button — a <see cref="Button"/> with its own look — that lights up when <see cref="Active"/> and
/// shows a tooltip, with its keyboard shortcut, after a moment under the pointer.
/// </summary>
internal sealed class IconButton : Button
{
    private const float TooltipDelay = 0.35f;

    private readonly HudKit _kit;
    private float _hover;
    private float _hoverTime;

    public IconButton(HudKit kit, Texture2D icon)
    {
        _kit = kit;
        Icon = icon;
    }

    public Texture2D Icon;

    public float IconSize = 20.0f;

    public string Tooltip = "";

    public string Shortcut = "";

    /// <summary>Whether the button's mode is on (the tour running, the settings open): drawn in the accent color.</summary>
    public bool Active;

    protected override void OnDraw()
    {
        float opacity = HudKit.Opacity(this);
        if (opacity <= 0.001f) return;

        float dt = Time.UnscaledDeltaTime;
        _hover = HudKit.Approach(_hover, Hovered ? 1.0f : 0.0f, dt * 10.0f);
        _hoverTime = Hovered && !Held ? _hoverTime + dt : 0.0f;

        var at = new Vector2(ScreenRect.X, ScreenRect.Y);
        var size = new Vector2(ScreenRect.Z, ScreenRect.W);
        Vector4 backdrop = Active
            ? HudKit.Accent * new Vector4(1.0f + 0.12f * _hover, 1.0f + 0.12f * _hover, 1.0f + 0.12f * _hover, 0.95f)
            : new Vector4(1.0f, 1.0f, 1.0f, 0.09f * _hover + (Held ? 0.07f : 0.0f));
        if (backdrop.W > 0.001f)
        {
            UIRenderer.DrawQuad(at, size, HudKit.Fade(backdrop, opacity), _kit.Disc, HudKit.FullUv);
        }

        Vector4 tint = Active ? Vector4.One : HudKit.Ink * new Vector4(1.0f, 1.0f, 1.0f, 0.72f + 0.28f * _hover);
        var icon = new Vector2(IconSize);
        UIRenderer.DrawQuad(at + (size - icon) * 0.5f, icon, HudKit.Fade(tint, opacity), Icon, HudKit.FullUv);

        if (Tooltip.Length > 0 && _hoverTime > TooltipDelay)
        {
            DrawTooltip(opacity * MathF.Min(1.0f, (_hoverTime - TooltipDelay) * 8.0f));
        }
    }

    // A pill above the button: the action, then its key.
    private void DrawTooltip(float opacity)
    {
        const float fontSize = 13.0f;
        const float height = 30.0f;
        Font font = UIRoot.DefaultFont;
        float textWidth = UIRenderer.MeasureText(font, Tooltip, fontSize, TextLayoutOptions.Default).X;
        float keyWidth = Shortcut.Length > 0 ? UIRenderer.MeasureText(font, Shortcut, 11.5f, TextLayoutOptions.Default).X + 14.0f + 8.0f : 0.0f;
        float width = textWidth + keyWidth + 24.0f;

        Widget root = this;
        while (root.Parent is not null) root = root.Parent;
        float x = Math.Clamp(ScreenRect.X + ScreenRect.Z * 0.5f - width * 0.5f, 8.0f, root.ScreenRect.Z - width - 8.0f);
        float y = ScreenRect.Y - height - 14.0f;

        UIRenderer.DrawNineSlice(new Vector2(x, y), new Vector2(width, height), HudKit.Fade(new Vector4(0.06f, 0.07f, 0.1f, 0.96f), opacity), _kit.Pill,
            new Vector4(10.0f));
        UIRenderer.DrawText(font, Tooltip, new Vector2(x + 12.0f, y + 7.0f), fontSize, HudKit.Fade(HudKit.Ink, opacity), TextLayoutOptions.Default);
        if (Shortcut.Length > 0)
        {
            _kit.DrawKeycap(Shortcut, new Vector2(x + width - 12.0f, y + 5.5f), 11.5f, opacity, alignRight: true);
        }
    }
}

/// <summary>A <see cref="Toggle"/> drawn as a label and a sliding switch; the whole row is the hit area.</summary>
internal sealed class Switch : Toggle
{
    private readonly HudKit _kit;
    private float _knob;
    private bool _placed;

    public Switch(HudKit kit, string label)
    {
        _kit = kit;
        Label = label;
        FontSize = 14.5f;
    }

    protected override void OnDraw()
    {
        float opacity = HudKit.Opacity(this);
        if (opacity <= 0.001f) return;

        // The knob slides when flipped, but starts where it belongs.
        _knob = _placed ? HudKit.Approach(_knob, On ? 1.0f : 0.0f, Time.UnscaledDeltaTime * 8.0f) : On ? 1.0f : 0.0f;
        _placed = true;

        var at = new Vector2(ScreenRect.X, ScreenRect.Y);
        float height = ScreenRect.W;
        Font font = UIRoot.DefaultFont;
        UIRenderer.DrawText(font, Label, new Vector2(at.X, at.Y + (height - FontSize * 1.2f) * 0.5f), FontSize,
            HudKit.Fade(HudKit.Ink * new Vector4(1.0f, 1.0f, 1.0f, Hovered ? 1.0f : 0.86f), opacity), TextLayoutOptions.Default);

        var track = new Vector2(36.0f, 20.0f);
        var trackAt = new Vector2(at.X + ScreenRect.Z - track.X, at.Y + (height - track.Y) * 0.5f);
        Vector4 off = new(1.0f, 1.0f, 1.0f, Hovered ? 0.2f : 0.14f);
        Vector4 color = Vector4.Lerp(off, HudKit.Accent, _knob);
        UIRenderer.DrawNineSlice(trackAt, track, HudKit.Fade(color, opacity), _kit.Pill, new Vector4(10.0f));

        const float knob = 16.0f;
        var knobAt = new Vector2(trackAt.X + 2.0f + _knob * (track.X - knob - 4.0f), trackAt.Y + 2.0f);
        UIRenderer.DrawQuad(knobAt, new Vector2(knob), HudKit.Fade(Vector4.One, opacity), _kit.Disc, HudKit.FullUv);
    }
}

/// <summary>
/// A <see cref="Slider"/> drawn as a thin rounded track with a tick per step and a round knob. With
/// <see cref="Steps"/> set, the value snaps to whole steps.
/// </summary>
internal sealed class PillSlider : Slider
{
    private readonly HudKit _kit;

    public PillSlider(HudKit kit) => _kit = kit;

    public int Steps;

    /// <summary>Gets whether the knob is being dragged.</summary>
    public bool Dragging => Held;

    protected override void OnDraw()
    {
        float opacity = HudKit.Opacity(this);
        if (opacity <= 0.001f) return;

        var at = new Vector2(ScreenRect.X, ScreenRect.Y);
        float width = ScreenRect.Z;
        float t = Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0.0f, 1.0f) : 0.0f;
        float y = at.Y + ScreenRect.W * 0.5f;

        UIRenderer.DrawNineSlice(new Vector2(at.X, y - 2.0f), new Vector2(width, 4.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.12f * opacity), _kit.Track,
            new Vector4(2.0f));
        UIRenderer.DrawNineSlice(new Vector2(at.X, y - 2.0f), new Vector2(MathF.Max(width * t, 4.0f), 4.0f), HudKit.Fade(HudKit.Accent, opacity),
            _kit.Track, new Vector4(2.0f));

        for (int i = 0; i < Steps; i++)
        {
            float tick = Steps > 1 ? (float)i / (Steps - 1) : 0.0f;
            Vector4 color = tick <= t + 1e-3f ? new Vector4(1.0f, 1.0f, 1.0f, 0.55f) : new Vector4(1.0f, 1.0f, 1.0f, 0.22f);
            UIRenderer.DrawQuad(new Vector2(at.X + width * tick - 1.5f, y + 7.0f), new Vector2(3.0f), HudKit.Fade(color, opacity), _kit.Disc,
                HudKit.FullUv);
        }

        float knob = Hovered || Held ? 18.0f : 16.0f;
        var knobAt = new Vector2(at.X + width * t - knob * 0.5f, y - knob * 0.5f);
        UIRenderer.DrawQuad(knobAt + new Vector2(0.0f, 1.0f), new Vector2(knob), new Vector4(0.0f, 0.0f, 0.0f, 0.35f * opacity), _kit.Disc, HudKit.FullUv);
        UIRenderer.DrawQuad(knobAt, new Vector2(knob), HudKit.Fade(Vector4.One, opacity), _kit.Disc, HudKit.FullUv);
    }
}

/// <summary>A <see cref="Button"/> drawn as a menu row: a label, a key on the right, and a soft highlight on hover.</summary>
internal sealed class MenuButton : Button
{
    private readonly HudKit _kit;

    public MenuButton(HudKit kit, string label, string shortcut)
    {
        _kit = kit;
        Label = label;
        Shortcut = shortcut;
        FontSize = 14.5f;
    }

    public string Shortcut;

    protected override void OnDraw()
    {
        float opacity = HudKit.Opacity(this);
        if (opacity <= 0.001f) return;

        var at = new Vector2(ScreenRect.X, ScreenRect.Y);
        var size = new Vector2(ScreenRect.Z, ScreenRect.W);
        if (Hovered)
        {
            UIRenderer.DrawNineSlice(at, size, new Vector4(1.0f, 1.0f, 1.0f, (Held ? 0.1f : 0.06f) * opacity), _kit.Pill, new Vector4(10.0f));
        }

        UIRenderer.DrawText(UIRoot.DefaultFont, Label, new Vector2(at.X + 8.0f, at.Y + (size.Y - FontSize * 1.2f) * 0.5f), FontSize,
            HudKit.Fade(HudKit.Ink * new Vector4(1.0f, 1.0f, 1.0f, 0.86f), opacity), TextLayoutOptions.Default);
        if (Shortcut.Length > 0)
        {
            _kit.DrawKeycap(Shortcut, new Vector2(at.X + size.X - 8.0f, at.Y + (size.Y - 20.5f) * 0.5f), 11.5f, opacity, alignRight: true);
        }
    }
}

/// <summary>A <see cref="Text"/> that fades with the panel it sits on.</summary>
internal sealed class FadeText : Text
{
    protected override void OnDraw()
    {
        Vector4 color = Color;
        Color = HudKit.Fade(color, HudKit.Opacity(this));
        base.OnDraw();
        Color = color;
    }
}

/// <summary>A one-pixel line that fades with the panel it sits on.</summary>
internal sealed class Divider : Widget
{
    protected override void OnDraw() =>
        UIRenderer.DrawQuad(new Vector2(ScreenRect.X, ScreenRect.Y), new Vector2(ScreenRect.Z, 1.0f),
            new Vector4(1.0f, 1.0f, 1.0f, 0.07f * HudKit.Opacity(this)));
}
