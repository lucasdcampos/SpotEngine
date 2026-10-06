using System.Numerics;
using Spot.Engine.UI;
using Spot.Engine;
using Spot.Engine.Graphics;

namespace ProvingGrounds;

/// <summary>The HUD's palette.</summary>
public static class HudColors
{
    public static readonly Vector4 Ink = new(0.96f, 0.97f, 1.0f, 1.0f);
    public static readonly Vector4 Muted = new(0.68f, 0.73f, 0.8f, 1.0f);
    public static readonly Vector4 Faint = new(0.47f, 0.52f, 0.6f, 1.0f);
    public static readonly Vector4 Accent = new(0.33f, 0.86f, 1.0f, 1.0f);
    public static readonly Vector4 Warm = new(1.0f, 0.6f, 0.22f, 1.0f);
    public static readonly Vector4 Gold = new(1.0f, 0.84f, 0.36f, 1.0f);
    public static readonly Vector4 Danger = new(1.0f, 0.36f, 0.33f, 1.0f);
    public static readonly Vector4 Fill = new(0.025f, 0.03f, 0.045f, 0.66f);
}

/// <summary>
/// The HUD's shared look: the generated textures every element draws with (created once, released with the HUD) and
/// small drawing helpers — text with a soft shadow so it reads over a bright sky, letter-spaced captions, frosted
/// cards, key caps.
/// </summary>
internal sealed class HudKit : IDisposable
{
    public static readonly Vector4 FullUv = new(0.0f, 0.0f, 1.0f, 1.0f);

    // One cached string per Latin-1 character, so letter-spaced text allocates nothing per frame.
    private static readonly string[] Letters = Enumerable.Range(0, 256).Select(c => ((char)c).ToString()).ToArray();

    public Texture2D Panel { get; } = UITextures.RoundedRect(12);
    public Texture2D Outline { get; } = UITextures.RoundedOutline(12);
    public Texture2D Shadow { get; } = UITextures.RoundedShadow(12, 22);
    public Texture2D Pill { get; } = UITextures.RoundedRect(8);
    public Texture2D Small { get; } = UITextures.RoundedRect(3);
    public Texture2D Disc { get; } = UITextures.Disc();
    public Texture2D Ring { get; } = UITextures.Ring();
    public Texture2D Glow { get; } = UITextures.Glow();
    public Texture2D HitMarker { get; } = UITextures.HitMarker();
    public Texture2D Vignette { get; } = UITextures.Vignette();
    public Texture2D RifleIcon { get; } = UITextures.Icon(IconShape.Rifle);
    public Texture2D LauncherIcon { get; } = UITextures.Icon(IconShape.Launcher);
    public Texture2D InfinityIcon { get; } = UITextures.Icon(IconShape.Infinity, 96, 48);
    public Texture2D DiamondIcon { get; } = UITextures.Icon(IconShape.Diamond, 32, 32);
    public Texture2D BoltIcon { get; } = UITextures.Icon(IconShape.Bolt, 48, 48);

    public static Font Font => UIRoot.DefaultFont;

    public void Dispose()
    {
        foreach (Texture2D texture in new[]
                 {
                     Panel, Outline, Shadow, Pill, Small, Disc, Ring, Glow, HitMarker, Vignette, RifleIcon, LauncherIcon, InfinityIcon,
                     DiamondIcon, BoltIcon,
                 })
        {
            texture.Dispose();
        }
    }

    public static Vector4 Fade(Vector4 color, float opacity) => color with { W = color.W * opacity };

    public static float Approach(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);

    public static void Quad(Vector2 at, Vector2 size, Vector4 color) => UIRenderer.DrawQuad(at, size, color);

    public static void Image(Texture2D texture, Vector2 at, Vector2 size, Vector4 color) => UIRenderer.DrawQuad(at, size, color, texture, FullUv);

    public static Vector2 Measure(string text, float size) => UIRenderer.MeasureText(Font, text, size, TextLayoutOptions.Default);

    /// <summary>Draws text with a soft shadow beneath; the alignment is about <paramref name="at"/>'s X.</summary>
    public static float Text(string text, Vector2 at, float size, Vector4 color, TextAlign align = TextAlign.Left, bool shadow = true)
    {
        float width = Measure(text, size).X;
        float x = align switch
        {
            TextAlign.Center => at.X - width * 0.5f,
            TextAlign.Right => at.X - width,
            _ => at.X,
        };

        if (shadow && color.W > 0.01f)
        {
            UIRenderer.DrawText(Font, text, new Vector2(x, at.Y + 1.5f), size, new Vector4(0.0f, 0.0f, 0.0f, 0.5f * color.W), TextLayoutOptions.Default);
        }

        UIRenderer.DrawText(Font, text, new Vector2(x, at.Y), size, color, TextLayoutOptions.Default);
        return width;
    }

    /// <summary>The width of letter-spaced text.</summary>
    public static float MeasureTracked(string text, float size, float tracking)
    {
        float width = 0.0f;
        foreach (char c in text)
        {
            width += Measure(Letters[c & 0xFF], size).X + tracking;
        }

        return MathF.Max(0.0f, width - tracking);
    }

    /// <summary>Draws letter-spaced text (captions, titles), with a soft shadow.</summary>
    public static float Tracked(string text, Vector2 at, float size, Vector4 color, float tracking, TextAlign align = TextAlign.Left, bool shadow = true)
    {
        float width = MeasureTracked(text, size, tracking);
        float x = align switch
        {
            TextAlign.Center => at.X - width * 0.5f,
            TextAlign.Right => at.X - width,
            _ => at.X,
        };

        foreach (char c in text)
        {
            string letter = Letters[c & 0xFF];
            if (shadow && color.W > 0.01f)
            {
                UIRenderer.DrawText(Font, letter, new Vector2(x, at.Y + 1.5f), size, new Vector4(0.0f, 0.0f, 0.0f, 0.5f * color.W), TextLayoutOptions.Default);
            }

            UIRenderer.DrawText(Font, letter, new Vector2(x, at.Y), size, color, TextLayoutOptions.Default);
            x += Measure(letter, size).X + tracking;
        }

        return width;
    }

    /// <summary>Draws a frosted card: a soft shadow, the translucent fill and a faint outline.</summary>
    public void Card(Vector2 at, Vector2 size, float opacity, Vector4? fill = null)
    {
        if (opacity <= 0.001f) return;
        UIRenderer.DrawNineSlice(at - new Vector2(22.0f, 14.0f), size + new Vector2(44.0f, 44.0f), new Vector4(0.0f, 0.0f, 0.0f, 0.4f * opacity), Shadow,
            new Vector4(34.0f));
        UIRenderer.DrawNineSlice(at, size, Fade(fill ?? HudColors.Fill, opacity), Panel, new Vector4(12.0f));
        UIRenderer.DrawNineSlice(at, size, new Vector4(1.0f, 1.0f, 1.0f, 0.1f * opacity), Outline, new Vector4(12.0f));
    }

    /// <summary>Draws a small rounded rectangle (bars, chips).</summary>
    public void Rounded(Vector2 at, Vector2 size, Vector4 color, bool small = false)
    {
        if (color.W <= 0.001f || size.X <= 0.0f || size.Y <= 0.0f) return;
        float border = small ? 3.0f : 8.0f;
        border = MathF.Min(border, MathF.Min(size.X, size.Y) * 0.5f);
        UIRenderer.DrawNineSlice(at, size, color, small ? Small : Pill, new Vector4(border));
    }

    /// <summary>Draws a key name on a small cap; returns its width.</summary>
    public float Keycap(string keys, Vector2 at, float size, float opacity, bool alignRight = false)
    {
        float width = Measure(keys, size).X + size * 1.1f;
        float left = alignRight ? at.X - width : at.X;
        float height = size * 1.75f;
        UIRenderer.DrawNineSlice(new Vector2(left, at.Y), new Vector2(width, height), new Vector4(1.0f, 1.0f, 1.0f, 0.12f * opacity), Small, new Vector4(3.0f));
        UIRenderer.DrawNineSlice(new Vector2(left, at.Y + height - 2.0f), new Vector2(width, 2.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.08f * opacity), Small,
            new Vector4(1.0f));
        UIRenderer.DrawText(Font, keys, new Vector2(left + size * 0.55f, at.Y + size * 0.3f), size, Fade(HudColors.Ink, opacity), TextLayoutOptions.Default);
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
}

/// <summary>
/// A frosted panel that fades as a whole — every control on it reads its <see cref="Opacity"/> — and swallows the
/// clicks that land on it, so they never reach the game.
/// </summary>
internal sealed class GlassPanel : Panel
{
    private readonly HudKit _kit;

    public GlassPanel(HudKit kit) => _kit = kit;

    public float Opacity = 1.0f;

    protected override bool IsInteractive => true;

    protected override void OnDraw() =>
        _kit.Card(new Vector2(ScreenRect.X, ScreenRect.Y), new Vector2(ScreenRect.Z, ScreenRect.W), Opacity,
            new Vector4(0.03f, 0.035f, 0.05f, 0.86f));
}

/// <summary>A <see cref="Button"/> drawn as a menu row: an accent bar and highlight on hover, the label, a key cap.</summary>
internal sealed class MenuButton : Button
{
    private readonly HudKit _kit;
    private float _hover;
    private bool _wasHovered;

    public MenuButton(HudKit kit, string label, string shortcut)
    {
        _kit = kit;
        Label = label;
        Shortcut = shortcut;
        FontSize = 19.0f;
    }

    public string Shortcut;

    public Vector4 Accent = HudColors.Accent;

    protected override void OnDraw()
    {
        float opacity = HudKit.Opacity(this);
        if (opacity <= 0.001f) return;

        if (Hovered && !_wasHovered) Sfx.Play(Sfx.UiHover, 0.35f, 0.0f, 1.0f, Spot.Engine.Audio.AudioMixer.UiBus);
        _wasHovered = Hovered;
        _hover = HudKit.Approach(_hover, Hovered ? 1.0f : 0.0f, Time.UnscaledDeltaTime * 9.0f);

        var at = new Vector2(ScreenRect.X, ScreenRect.Y);
        var size = new Vector2(ScreenRect.Z, ScreenRect.W);
        _kit.Rounded(at, size, new Vector4(1.0f, 1.0f, 1.0f, (0.035f + 0.07f * _hover + (Held ? 0.05f : 0.0f)) * opacity));
        _kit.Rounded(at + new Vector2(0.0f, size.Y * 0.2f), new Vector2(3.0f, size.Y * 0.6f), HudKit.Fade(Accent, _hover * opacity), small: true);

        HudKit.Text(Label, new Vector2(at.X + 18.0f + 4.0f * _hover, at.Y + (size.Y - FontSize * 1.2f) * 0.5f), FontSize,
            HudKit.Fade(HudColors.Ink * new Vector4(1.0f, 1.0f, 1.0f, 0.82f + 0.18f * _hover), opacity), shadow: false);
        if (Shortcut.Length > 0)
        {
            _kit.Keycap(Shortcut, new Vector2(at.X + size.X - 14.0f, at.Y + (size.Y - 13.0f * 1.75f) * 0.5f), 13.0f, opacity, alignRight: true);
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
        FontSize = 17.0f;
    }

    protected override void OnDraw()
    {
        float opacity = HudKit.Opacity(this);
        if (opacity <= 0.001f) return;

        _knob = _placed ? HudKit.Approach(_knob, On ? 1.0f : 0.0f, Time.UnscaledDeltaTime * 8.0f) : On ? 1.0f : 0.0f;
        _placed = true;

        var at = new Vector2(ScreenRect.X, ScreenRect.Y);
        float height = ScreenRect.W;
        HudKit.Text(Label, new Vector2(at.X, at.Y + (height - FontSize * 1.2f) * 0.5f), FontSize,
            HudKit.Fade(HudColors.Ink * new Vector4(1.0f, 1.0f, 1.0f, Hovered ? 1.0f : 0.82f), opacity), shadow: false);

        var track = new Vector2(42.0f, 22.0f);
        var trackAt = new Vector2(at.X + ScreenRect.Z - track.X, at.Y + (height - track.Y) * 0.5f);
        Vector4 off = new(1.0f, 1.0f, 1.0f, Hovered ? 0.22f : 0.15f);
        _kit.Rounded(trackAt, track, HudKit.Fade(Vector4.Lerp(off, HudColors.Accent, _knob), opacity));

        const float knob = 18.0f;
        var knobAt = new Vector2(trackAt.X + 2.0f + _knob * (track.X - knob - 4.0f), trackAt.Y + 2.0f);
        HudKit.Image(_kit.Disc, knobAt, new Vector2(knob), HudKit.Fade(Vector4.One, opacity));
    }
}

/// <summary>A <see cref="Slider"/> drawn as a labeled setting: the name and value above a thin track and a round knob.</summary>
internal sealed class SettingSlider : Slider
{
    private readonly HudKit _kit;

    public SettingSlider(HudKit kit, string label, Func<float, string> format)
    {
        _kit = kit;
        Label = label;
        Format = format;
    }

    public string Label;

    public Func<float, string> Format;

    protected override void OnDraw()
    {
        float opacity = HudKit.Opacity(this);
        if (opacity <= 0.001f) return;

        var at = new Vector2(ScreenRect.X, ScreenRect.Y);
        float width = ScreenRect.Z;
        HudKit.Text(Label, at, 17.0f, HudKit.Fade(HudColors.Ink * new Vector4(1.0f, 1.0f, 1.0f, Hovered || Held ? 1.0f : 0.82f), opacity), shadow: false);
        HudKit.Text(Format(Value), new Vector2(at.X + width, at.Y + 1.0f), 15.0f, HudKit.Fade(Held ? HudColors.Accent : HudColors.Muted, opacity),
            Spot.Engine.Graphics.TextAlign.Right, shadow: false);

        float t = Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0.0f, 1.0f) : 0.0f;
        float y = at.Y + ScreenRect.W - 11.0f;
        _kit.Rounded(new Vector2(at.X, y - 2.0f), new Vector2(width, 4.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.13f * opacity), small: true);
        _kit.Rounded(new Vector2(at.X, y - 2.0f), new Vector2(MathF.Max(width * t, 4.0f), 4.0f), HudKit.Fade(HudColors.Accent, opacity), small: true);

        float knob = Hovered || Held ? 18.0f : 15.0f;
        var knobAt = new Vector2(at.X + width * t - knob * 0.5f, y - knob * 0.5f);
        HudKit.Image(_kit.Disc, knobAt + new Vector2(0.0f, 1.0f), new Vector2(knob), new Vector4(0.0f, 0.0f, 0.0f, 0.35f * opacity));
        HudKit.Image(_kit.Disc, knobAt, new Vector2(knob), HudKit.Fade(Vector4.One, opacity));
    }
}

/// <summary>A caption or heading on a <see cref="GlassPanel"/>, letter-spaced, fading with it.</summary>
internal sealed class Caption : Widget
{
    public string Content = "";

    public float Size = 13.0f;

    public float Tracking = 2.0f;

    public Vector4 Color = HudColors.Muted;

    protected override void OnDraw() =>
        HudKit.Tracked(Content, new Vector2(ScreenRect.X, ScreenRect.Y), Size, HudKit.Fade(Color, HudKit.Opacity(this)), Tracking, shadow: false);
}
