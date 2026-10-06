using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.UI;
using Spot.Engine.Graphics;

namespace SolarSystem;

/// <summary>
/// A custom widget — the runtime UI is open to subclassing — that draws everything tied to the 3D view: name
/// labels, the hover reticle, the info card with its leader line, the shortcut sheet and brief notices. It
/// projects the bodies with the camera as it is when the frame is drawn, so labels never trail a moving view.
/// </summary>
internal sealed class SolarOverlay : Widget
{
    private const float CardWidth = 340.0f;
    private const float Pad = 20.0f;

    private static readonly Vector4 Ink = HudKit.Ink;
    private static readonly Vector4 Muted = HudKit.Muted;
    private static readonly Vector4 Faint = HudKit.Faint;
    private static readonly Vector3 EarthBlue = new(0.36f, 0.58f, 0.95f);

    private static readonly (string Keys, string Action)[] Shortcuts =
    {
        ("Drag / WASD", "Orbit the camera"),
        ("Scroll / Q E", "Zoom"),
        ("Click", "Focus a body and pin its card"),
        ("1 - 8", "Focus a planet"),
        ("0 / M", "Focus the Sun / the Moon"),
        ("Tab", "Next body (Shift: previous)"),
        ("Esc", "Back to the overview"),
        ("Space", "Pause or resume time"),
        (",  .", "Slower / faster"),
        ("T", "Guided tour"),
        ("O / L", "Orbits / labels"),
        ("H", "Hide the interface"),
        ("F1", "This sheet"),
    };

    // One cached string per Latin-1 character, so letter-by-letter text allocates nothing per frame.
    private static readonly string[] Letters = Enumerable.Range(0, 256).Select(c => ((char)c).ToString()).ToArray();

    private readonly SolarHud _hud;
    private readonly HudKit _kit;
    private readonly List<Vector4> _labelRects = new();

    private CelestialBody? _cardBody;
    private List<Fact> _facts = new();
    private float _cardAlpha;
    private Vector2 _cardPosition;
    private float _reticle;
    private float _pixelsPerUnit = 1.0f;

    public SolarOverlay(SolarHud hud, HudKit kit)
    {
        _hud = hud;
        _kit = kit;
        Name = "Overlay";
    }

    /// <summary>Gets or sets how opaque the help sheet is (the HUD fades it in and out).</summary>
    public float HelpAlpha { get; set; }

    /// <summary>Gets or sets a message shown briefly at the bottom, and how opaque it is.</summary>
    public string Toast { get; set; } = "";

    public float ToastAlpha { get; set; }

    protected override void OnDraw()
    {
        if (Parent is null)
        {
            return;
        }

        _pixelsPerUnit = Parent is UIRoot root && root.Scale > 0.0f ? root.Scale : 1.0f;
        Vector4 screen = Parent.ScreenRect;
        var size = new Vector2(screen.Z, screen.W);
        Font font = UIRoot.DefaultFont;
        float dt = _hud.FrameTime;

        if (!_hud.InterfaceHidden && _hud.TryGetView(out Matrix4x4 viewProjection, out Vector3 cameraPosition, out float focal))
        {
            var view = new View(viewProjection, cameraPosition, focal, size);
            if (_hud.ShowLabels)
            {
                DrawLabels(font, view);
            }

            DrawSelection(font, view, dt);
        }

        if (HelpAlpha > 0.01f)
        {
            DrawHelp(font, size, HelpAlpha);
        }

        if (ToastAlpha > 0.01f && Toast.Length > 0)
        {
            DrawToast(font, size, ToastAlpha);
        }
    }

    // A camera snapshot for projecting world points into UI units.
    private readonly record struct View(Matrix4x4 ViewProjection, Vector3 Camera, float Focal, Vector2 Size)
    {
        public bool Project(Vector3 world, out Vector2 point)
        {
            Vector4 clip = Vector4.Transform(new Vector4(world, 1.0f), ViewProjection);
            point = default;
            if (clip.W <= 1e-4f) return false;
            point = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * Size.X, (0.5f - clip.Y / clip.W * 0.5f) * Size.Y);
            return true;
        }

        // The on-screen radius of a sphere: its tangent cone, scaled by the focal length.
        public float Radius(Vector3 center, float radius)
        {
            float d = Vector3.Distance(center, Camera);
            return radius / MathF.Sqrt(MathF.Max(d * d - radius * radius, 1e-6f)) * Focal * Size.Y * 0.5f;
        }
    }

    private void DrawLabels(Font font, in View view)
    {
        _labelRects.Clear();
        CelestialBody? card = _hud.CardBody;
        foreach (CelestialBody body in _hud.Bodies)
        {
            if (!view.Project(body.Position, out Vector2 at)) continue;
            float radius = view.Radius(body.Position, body.Radius);
            bool hot = body == _hud.Hovered || body == _hud.Pinned;

            // Specks get a faint ring so they can be found at all; big bodies speak for themselves.
            if (radius < 4.0f)
            {
                float marker = 9.0f;
                UIRenderer.DrawQuad(at - new Vector2(marker), new Vector2(marker * 2.0f), WithAlpha(body.AccentColor, hot ? 0.9f : 0.45f), _kit.Ring,
                    new Vector4(0.0f, 0.0f, 1.0f, 1.0f));
            }

            if (body == card && _cardAlpha > 0.5f) continue;
            if (radius > view.Size.Y * 0.35f) continue;

            // A moon's label waits until the camera is close enough to tell it from its planet.
            if (body.Primary is { } primary && view.Project(primary.Position, out Vector2 primaryAt) && Vector2.Distance(at, primaryAt) < 40.0f)
            {
                continue;
            }

            string name = body.DisplayName.ToUpperInvariant();
            const float fontSize = 12.5f;
            const float tracking = 1.8f;
            float width = MeasureTracked(font, name, fontSize, tracking);
            var position = new Vector2(at.X - width * 0.5f, at.Y + MathF.Max(radius, 6.0f) + 8.0f);
            var rect = new Vector4(position.X - 4.0f, position.Y - 2.0f, width + 8.0f, fontSize + 6.0f);
            if (Overlaps(rect)) continue;
            _labelRects.Add(rect);

            Vector4 color = hot ? Ink : new Vector4(Muted.X, Muted.Y, Muted.Z, 0.8f);
            DrawTracked(font, name, position, fontSize, color, tracking);
        }
    }

    private void DrawSelection(Font font, in View view, float dt)
    {
        CelestialBody? body = _hud.CardBody;
        if (body != _cardBody && body is not null)
        {
            // A new body: restart the card's entrance and rebuild its facts.
            _cardBody = body;
            _facts = Facts.For(body);
            _cardAlpha = 0.0f;
            _reticle = 0.0f;
        }

        float target = body is not null ? 1.0f : 0.0f;
        _cardAlpha = Approach(_cardAlpha, target, dt * (target > _cardAlpha ? 5.5f : 7.0f));
        _reticle = Approach(_reticle, target, dt * 4.0f);
        if (_cardBody is null || _cardAlpha <= 0.005f)
        {
            return;
        }

        CelestialBody shown = _cardBody;
        if (!view.Project(shown.Position, out Vector2 at))
        {
            return;
        }

        float ease = 1.0f - (1.0f - _cardAlpha) * (1.0f - _cardAlpha);
        float radius = view.Radius(shown.Position, shown.VisualRadius);
        float ringRadius = MathF.Max(radius * 1.12f, 12.0f) + 10.0f * (1.0f - _reticle);
        DrawBrackets(at, ringRadius, WithAlpha(shown.AccentColor, 0.9f * ease));

        float height = CardHeight(font, shown);
        Vector2 size = view.Size;
        bool right = at.X + ringRadius + 48.0f + CardWidth < size.X - 24.0f || at.X < size.X * 0.5f;
        float x = right ? at.X + ringRadius + 48.0f : at.X - ringRadius - 48.0f - CardWidth;
        float y = Math.Clamp(at.Y - 70.0f, 24.0f, MathF.Max(24.0f, size.Y - height - 110.0f));
        var goal = new Vector2(Math.Clamp(x, 24.0f, MathF.Max(24.0f, size.X - CardWidth - 24.0f)), y);
        _cardPosition = _cardAlpha < 0.02f ? goal : Vector2.Lerp(_cardPosition, goal, 1.0f - MathF.Exp(-14.0f * dt));
        Vector2 card = _cardPosition + new Vector2(0.0f, 10.0f * (1.0f - ease));

        // The leader: out from the reticle, then across to the card's edge, all at right angles.
        float edgeX = right ? card.X : card.X + CardWidth;
        float startX = right ? at.X + ringRadius : at.X - ringRadius;
        float targetY = Math.Clamp(at.Y, card.Y + 22.0f, card.Y + height - 22.0f);
        float elbowX = startX + (edgeX - startX) * 0.45f;
        Vector4 line = WithAlpha(shown.AccentColor, 0.55f * ease);
        HLine(startX, elbowX, at.Y, line);
        VLine(elbowX, at.Y, targetY, line);
        HLine(elbowX, edgeX, targetY, line);

        DrawCard(font, shown, card, height, ease);
    }

    private float CardHeight(Font font, CelestialBody body)
    {
        float description = UIRenderer.MeasureText(font, body.Description, 14.0f, Wrapped(CardWidth - Pad * 2.0f)).Y;
        int rows = (_facts.Count + 1) / 2;
        return Pad + 78.0f + description + 18.0f + rows * 48.0f + 96.0f;
    }

    private void DrawCard(Font font, CelestialBody body, Vector2 at, float height, float alpha)
    {
        _kit.DrawCard(at, new Vector2(CardWidth, height), alpha);

        Vector4 ink = Fade(Ink, alpha);
        Vector4 muted = Fade(Muted, alpha);
        Vector4 faint = Fade(Faint, alpha);
        float x = at.X + Pad;
        float y = at.Y + Pad;

        // Header: a dot in the body's color, its name, and what it is.
        UIRenderer.DrawQuad(new Vector2(x, y + 9.0f), new Vector2(11.0f), WithAlpha(body.AccentColor, alpha), _kit.Disc, new Vector4(0.0f, 0.0f, 1.0f, 1.0f));
        UIRenderer.DrawText(font, body.DisplayName, new Vector2(x + 20.0f, y - 2.0f), 27.0f, ink, TextLayoutOptions.Default);
        string kicker = body.Kind switch
        {
            BodyKind.Planet => $"{body.Category}  ·  {Facts.Ordinal(body.Order)} from the Sun",
            BodyKind.Moon => $"{body.Category}  ·  {body.Primary?.DisplayName ?? "planet"}",
            _ => body.Category,
        };
        UIRenderer.DrawText(font, kicker, new Vector2(x, y + 36.0f), 14.0f, muted, TextLayoutOptions.Default);
        UIRenderer.DrawQuad(new Vector2(x, y + 62.0f), new Vector2(34.0f, 2.0f), WithAlpha(body.AccentColor, alpha));
        y += 78.0f;

        Vector2 text = UIRenderer.DrawText(font, body.Description, new Vector2(x, y), 14.0f, Fade(new Vector4(0.8f, 0.83f, 0.88f, 1.0f), alpha),
            Wrapped(CardWidth - Pad * 2.0f));
        y += text.Y + 18.0f;

        // Facts, two to a row.
        float column = (CardWidth - Pad * 2.0f) * 0.5f;
        UIRenderer.DrawQuad(new Vector2(x, y - 9.0f), new Vector2(CardWidth - Pad * 2.0f, 1.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.07f * alpha));
        for (int i = 0; i < _facts.Count; i++)
        {
            Fact fact = _facts[i];
            var cell = new Vector2(x + (i % 2) * column, y + (i / 2) * 48.0f);
            DrawTracked(font, fact.Label.ToUpperInvariant(), cell + new Vector2(0.0f, 3.0f), 11.5f, faint, 1.2f);
            DrawValue(font, fact, cell + new Vector2(0.0f, 19.0f), ink, muted);
        }

        y += (_facts.Count + 1) / 2 * 48.0f + 4.0f;
        UIRenderer.DrawQuad(new Vector2(x, y), new Vector2(CardWidth - Pad * 2.0f, 1.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.07f * alpha));
        DrawSizeComparison(font, body, new Vector2(x, y + 12.0f), alpha);
    }

    // Size against the Earth: two discs to scale, sitting on a common baseline.
    private void DrawSizeComparison(Font font, CelestialBody body, Vector2 at, float alpha)
    {
        DrawTracked(font, "SIZE VS EARTH", at + new Vector2(0.0f, 3.0f), 11.5f, Fade(Faint, alpha), 1.2f);
        UIRenderer.DrawText(font, Facts.SizeComparedToEarth(body), at + new Vector2(0.0f, 20.0f), 17.0f, Fade(Ink, alpha), TextLayoutOptions.Default);

        const float earthKm = 12_742.0f;
        const float tallest = 58.0f;
        float ratio = body.DiameterKm / earthKm;
        float bodySize = ratio >= 1.0f ? tallest : MathF.Max(tallest * ratio, 3.0f);
        float earthSize = ratio >= 1.0f ? MathF.Max(tallest / ratio, 2.0f) : tallest;
        float baseline = at.Y + 66.0f;
        float right = at.X + CardWidth - Pad * 2.0f;

        var earthAt = new Vector2(right - earthSize, baseline - earthSize);
        var bodyAt = new Vector2(earthAt.X - 14.0f - bodySize, baseline - bodySize);
        var uv = new Vector4(0.0f, 0.0f, 1.0f, 1.0f);
        if (MathF.Abs(ratio - 1.0f) < 0.005f)
        {
            UIRenderer.DrawQuad(earthAt, new Vector2(earthSize), WithAlpha(body.AccentColor, alpha), _kit.Disc, uv);
            return;
        }

        UIRenderer.DrawQuad(bodyAt, new Vector2(bodySize), WithAlpha(body.AccentColor, alpha), _kit.Disc, uv);
        UIRenderer.DrawQuad(earthAt, new Vector2(earthSize), WithAlpha(EarthBlue, alpha), _kit.Disc, uv);
    }

    // A value with an optional superscript exponent and a unit in a quieter color: "5.97 × 10²⁴ kg".
    private static void DrawValue(Font font, Fact fact, Vector2 at, Vector4 ink, Vector4 muted)
    {
        const float size = 18.0f;
        Vector2 value = UIRenderer.DrawText(font, fact.Value, at, size, ink, TextLayoutOptions.Default);
        float x = at.X + value.X;
        if (fact.Exponent.Length > 0)
        {
            x += 1.0f + UIRenderer.DrawText(font, fact.Exponent, new Vector2(x + 1.0f, at.Y - 5.0f), 11.5f, ink, TextLayoutOptions.Default).X;
        }

        if (fact.Unit.Length > 0)
        {
            UIRenderer.DrawText(font, fact.Unit, new Vector2(x, at.Y + 3.0f), 14.0f, muted, TextLayoutOptions.Default);
        }
    }

    private void DrawHelp(Font font, Vector2 size, float alpha)
    {
        const float width = 520.0f;
        const float row = 34.0f;
        float height = 96.0f + Shortcuts.Length * row;
        var at = new Vector2((size.X - width) * 0.5f, (size.Y - height) * 0.5f);
        float ease = 1.0f - (1.0f - alpha) * (1.0f - alpha);
        at.Y += 12.0f * (1.0f - ease);

        UIRenderer.DrawQuad(Vector2.Zero, size, new Vector4(0.0f, 0.0f, 0.0f, 0.35f * alpha));
        _kit.DrawCard(at, new Vector2(width, height), alpha);

        UIRenderer.DrawText(font, "Keyboard & mouse", at + new Vector2(28.0f, 24.0f), 24.0f, Fade(Ink, alpha), TextLayoutOptions.Default);
        UIRenderer.DrawText(font, "F1 or Esc to close", at + new Vector2(width - 28.0f - 140.0f, 31.0f), 13.0f, Fade(Faint, alpha),
            new TextLayoutOptions { Align = TextAlign.Right, MaxWidth = 140.0f, LineSpacing = 1.0f });

        float y = at.Y + 78.0f;
        foreach ((string keys, string action) in Shortcuts)
        {
            _kit.DrawKeycap(keys, new Vector2(at.X + 28.0f, y), 13.5f, alpha);
            UIRenderer.DrawText(font, action, new Vector2(at.X + 196.0f, y + 4.0f), 15.0f, Fade(new Vector4(0.82f, 0.85f, 0.9f, 1.0f), alpha),
                TextLayoutOptions.Default);
            y += row;
        }
    }

    private void DrawToast(Font font, Vector2 size, float alpha)
    {
        const float fontSize = 15.0f;
        float width = UIRenderer.MeasureText(font, Toast, fontSize, TextLayoutOptions.Default).X + 40.0f;
        var at = new Vector2((size.X - width) * 0.5f, size.Y - 96.0f);
        UIRenderer.DrawNineSlice(at, new Vector2(width, 38.0f), HudKit.Fade(HudKit.Fill, alpha), _kit.Panel, new Vector4(14.0f));
        UIRenderer.DrawText(font, Toast, at + new Vector2(20.0f, 9.0f), fontSize, Fade(Ink, alpha), TextLayoutOptions.Default);
    }

    private bool Overlaps(Vector4 rect)
    {
        foreach (Vector4 other in _labelRects)
        {
            if (rect.X < other.X + other.Z && other.X < rect.X + rect.Z && rect.Y < other.Y + other.W && other.Y < rect.Y + rect.W)
            {
                return true;
            }
        }

        return false;
    }

    private static float MeasureTracked(Font font, string text, float size, float tracking)
    {
        float width = 0.0f;
        foreach (char c in text)
        {
            width += UIRenderer.MeasureText(font, Letter(c), size, TextLayoutOptions.Default).X + tracking;
        }

        return MathF.Max(0.0f, width - tracking);
    }

    // Draws text with extra space between letters — small capitals read more calmly that way. Each letter is snapped
    // to a whole screen pixel, or thin strokes (an I) would smear into neighbouring pixels and fade at small sizes.
    private void DrawTracked(Font font, string text, Vector2 at, float size, Vector4 color, float tracking)
    {
        float x = at.X;
        float y = MathF.Round(at.Y * _pixelsPerUnit) / _pixelsPerUnit;
        foreach (char c in text)
        {
            float snapped = MathF.Round(x * _pixelsPerUnit) / _pixelsPerUnit;
            x += UIRenderer.DrawText(font, Letter(c), new Vector2(snapped, y), size, color, TextLayoutOptions.Default).X + tracking;
        }
    }

    // Four corner brackets around a point — a targeting reticle that stays crisp at any size.
    private static void DrawBrackets(Vector2 center, float radius, Vector4 color)
    {
        float arm = Math.Clamp(radius * 0.35f, 6.0f, 22.0f);
        const float t = 1.5f;
        for (int corner = 0; corner < 4; corner++)
        {
            float sx = corner % 2 == 0 ? -1.0f : 1.0f;
            float sy = corner < 2 ? -1.0f : 1.0f;
            var tip = new Vector2(center.X + sx * radius, center.Y + sy * radius);
            UIRenderer.DrawQuad(new Vector2(sx < 0 ? tip.X : tip.X - arm, tip.Y - t * 0.5f), new Vector2(arm, t), color);
            UIRenderer.DrawQuad(new Vector2(tip.X - t * 0.5f, sy < 0 ? tip.Y : tip.Y - arm), new Vector2(t, arm), color);
        }
    }

    private static string Letter(char c) => c < Letters.Length ? Letters[c] : c.ToString();

    private static void HLine(float x0, float x1, float y, Vector4 color) =>
        UIRenderer.DrawQuad(new Vector2(MathF.Min(x0, x1), y - 0.5f), new Vector2(MathF.Abs(x1 - x0), 1.0f), color);

    private static void VLine(float x, float y0, float y1, Vector4 color) =>
        UIRenderer.DrawQuad(new Vector2(x - 0.5f, MathF.Min(y0, y1)), new Vector2(1.0f, MathF.Abs(y1 - y0) + 1.0f), color);

    private static TextLayoutOptions Wrapped(float width) => new() { MaxWidth = width, Align = TextAlign.Left, LineSpacing = 1.3f };

    private static Vector4 WithAlpha(Vector3 color, float alpha) => new(color, alpha);

    private static Vector4 Fade(Vector4 color, float alpha) => color with { W = color.W * alpha };

    private static float Approach(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);
}
