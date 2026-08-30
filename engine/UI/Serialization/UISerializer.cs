using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spot.Assets;
using Spot.Core;
using Spot.Rendering;

namespace Spot.UI.Serialization;

/// <summary>
/// Reads and writes a <c>.sptui</c> UI document — a <see cref="UIRoot"/>'s scale settings and its retained
/// <see cref="Widget"/> tree — as JSON. This is the on-disk counterpart to building UI in code: the editor
/// edits a live widget tree and saves it here, and the runtime loads it back and instantiates it into a
/// scene's UI (see <c>UICanvasComponent</c>). The format mirrors scene serialization conventions (vectors as
/// number arrays, asset references as stored ref strings) and never throws on load: unknown widget types and
/// unconvertible fields are skipped and logged so a hand-edited or older document keeps loading.
/// </summary>
public static class UISerializer
{
    private const string TypePanel = "Panel";
    private const string TypeButton = "Button";
    private const string TypeText = "Text";
    private const string TypeImage = "Image";
    private const string TypeSlider = "Slider";
    private const string TypeToggle = "Toggle";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Serializes a UI document (scale settings and widget tree) to a <c>.sptui</c> file.</summary>
    /// <param name="root">The document root whose children and scale settings are written.</param>
    /// <param name="path">The destination file path.</param>
    public static void Save(UIRoot root, string path)
    {
        JsonObject json = Write(root);
        File.WriteAllText(path, json.ToJsonString(WriteOptions));
    }

    /// <summary>Serializes a UI document to a JSON object (the in-memory form the file stores).</summary>
    /// <param name="root">The document root to serialize.</param>
    public static JsonObject Write(UIRoot root)
    {
        var widgets = new JsonArray();
        foreach (Widget child in root.Children)
        {
            widgets.Add(WriteWidget(child));
        }

        return new JsonObject
        {
            ["ScaleMode"] = root.ScaleMode.ToString(),
            ["ReferenceHeight"] = root.ReferenceHeight,
            ["Widgets"] = widgets,
        };
    }

    /// <summary>
    /// Loads a <c>.sptui</c> file into a new <see cref="UIRoot"/>, hydrating any texture/font references. Returns
    /// an empty root (never null) when the file is missing or malformed, logging the cause.
    /// </summary>
    /// <param name="path">The <c>.sptui</c> file path (source or a <c>guid:</c> reference).</param>
    public static UIRoot Load(string path)
    {
        try
        {
            string resolved = AssetRef.IsGuidRef(path)
                ? AssetPath.ResolveContent(path) ?? throw new FileNotFoundException($"Unresolved UI document '{path}'.")
                : AssetPath.Resolve(path);
            string text = File.ReadAllText(resolved);
            return Read(JsonNode.Parse(text)?.AsObject());
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to load UI document '{0}': {1}", path, ex.Message);
            return new UIRoot();
        }
    }

    /// <summary>Builds a <see cref="UIRoot"/> from a parsed document object. Missing data yields defaults.</summary>
    /// <param name="obj">The parsed document root, or null for an empty document.</param>
    public static UIRoot Read(JsonObject? obj)
    {
        var root = new UIRoot();
        if (obj is null) return root;

        if (obj.TryGetPropertyValue("ScaleMode", out JsonNode? mode) && mode is not null
            && Enum.TryParse(mode.GetValue<string>(), out UIScaleMode scaleMode))
        {
            root.ScaleMode = scaleMode;
        }

        if (obj.TryGetPropertyValue("ReferenceHeight", out JsonNode? h) && h is not null)
        {
            root.ReferenceHeight = h.GetValue<float>();
        }

        if (obj.TryGetPropertyValue("Widgets", out JsonNode? widgets) && widgets is JsonArray array)
        {
            foreach (JsonNode? node in array)
            {
                Widget? widget = ReadWidget(node as JsonObject);
                if (widget is not null) root.Add(widget);
            }
        }

        return root;
    }

    // ----- write -----------------------------------------------------------------------------------

    private static JsonObject WriteWidget(Widget widget)
    {
        var obj = new JsonObject { ["Type"] = TypeName(widget), ["Name"] = widget.Name };
        obj["Rect"] = WriteRect(widget.Rect);
        obj["Visible"] = widget.Visible;
        obj["Enabled"] = widget.Enabled;

        switch (widget)
        {
            case Panel p:
                obj["Color"] = Vec4(p.Color);
                obj["SpriteRef"] = p.SpriteRef;
                obj["Border"] = Vec4(p.Border);
                break;
            case Button b:
                obj["Label"] = b.Label;
                obj["FontRef"] = b.FontRef;
                obj["FontSize"] = b.FontSize;
                obj["TextColor"] = Vec4(b.TextColor);
                obj["NormalColor"] = Vec4(b.NormalColor);
                obj["HoverColor"] = Vec4(b.HoverColor);
                obj["PressedColor"] = Vec4(b.PressedColor);
                obj["SpriteRef"] = b.SpriteRef;
                obj["Border"] = Vec4(b.Border);
                break;
            case Text t:
                obj["Content"] = t.Content;
                obj["FontRef"] = t.FontRef;
                obj["FontSize"] = t.FontSize;
                obj["Color"] = Vec4(t.Color);
                obj["Align"] = (int)t.Align;
                obj["Wrap"] = t.Wrap;
                break;
            case Image img:
                obj["TextureRef"] = img.TextureRef;
                obj["Color"] = Vec4(img.Color);
                obj["Border"] = Vec4(img.Border);
                break;
            case Slider s:
                obj["Value"] = s.Value;
                obj["Min"] = s.Min;
                obj["Max"] = s.Max;
                obj["TrackColor"] = Vec4(s.TrackColor);
                obj["FillColor"] = Vec4(s.FillColor);
                obj["HandleColor"] = Vec4(s.HandleColor);
                obj["HandleWidth"] = s.HandleWidth;
                break;
            case Toggle tg:
                obj["On"] = tg.On;
                obj["Label"] = tg.Label;
                obj["FontRef"] = tg.FontRef;
                obj["FontSize"] = tg.FontSize;
                obj["TextColor"] = Vec4(tg.TextColor);
                obj["BoxColor"] = Vec4(tg.BoxColor);
                obj["HoverColor"] = Vec4(tg.HoverColor);
                obj["CheckColor"] = Vec4(tg.CheckColor);
                break;
        }

        if (widget.Children.Count > 0)
        {
            var children = new JsonArray();
            foreach (Widget child in widget.Children)
            {
                children.Add(WriteWidget(child));
            }

            obj["Children"] = children;
        }

        return obj;
    }

    private static JsonObject WriteRect(UIRect rect) => new()
    {
        ["Anchor"] = Vec2(rect.Anchor),
        ["Pivot"] = Vec2(rect.Pivot),
        ["Position"] = Vec2(rect.Position),
        ["Size"] = Vec2(rect.Size),
    };

    // ----- read ------------------------------------------------------------------------------------

    private static Widget? ReadWidget(JsonObject? obj)
    {
        if (obj is null) return null;

        string type = obj.TryGetPropertyValue("Type", out JsonNode? t) ? t?.GetValue<string>() ?? "" : "";
        Widget? widget = Create(type);
        if (widget is null)
        {
            Log.CoreWarn("Skipping unknown UI widget type '{0}'.", type);
            return null;
        }

        widget.Name = Str(obj, "Name");
        if (obj.TryGetPropertyValue("Rect", out JsonNode? rect) && rect is JsonObject rectObj)
        {
            widget.Rect = ReadRect(rectObj);
        }

        widget.Visible = Bool(obj, "Visible", true);
        widget.Enabled = Bool(obj, "Enabled", true);

        switch (widget)
        {
            case Panel p:
                p.Color = Vec4(obj, "Color", p.Color);
                p.SpriteRef = Str(obj, "SpriteRef");
                p.Border = Vec4(obj, "Border", p.Border);
                p.Sprite = LoadTexture(p.SpriteRef);
                break;
            case Button b:
                b.Label = Str(obj, "Label");
                b.FontRef = Str(obj, "FontRef");
                b.FontSize = Flt(obj, "FontSize", b.FontSize);
                b.TextColor = Vec4(obj, "TextColor", b.TextColor);
                b.NormalColor = Vec4(obj, "NormalColor", b.NormalColor);
                b.HoverColor = Vec4(obj, "HoverColor", b.HoverColor);
                b.PressedColor = Vec4(obj, "PressedColor", b.PressedColor);
                b.SpriteRef = Str(obj, "SpriteRef");
                b.Border = Vec4(obj, "Border", b.Border);
                b.Sprite = LoadTexture(b.SpriteRef);
                b.Font = LoadFont(b.FontRef);
                break;
            case Text tx:
                tx.Content = Str(obj, "Content");
                tx.FontRef = Str(obj, "FontRef");
                tx.FontSize = Flt(obj, "FontSize", tx.FontSize);
                tx.Color = Vec4(obj, "Color", tx.Color);
                tx.Align = (TextAlign)Int(obj, "Align", (int)tx.Align);
                tx.Wrap = Bool(obj, "Wrap", tx.Wrap);
                tx.Font = LoadFont(tx.FontRef);
                break;
            case Image img:
                img.TextureRef = Str(obj, "TextureRef");
                img.Color = Vec4(obj, "Color", img.Color);
                img.Border = Vec4(obj, "Border", img.Border);
                img.Texture = LoadTexture(img.TextureRef);
                break;
            case Slider s:
                s.Value = Flt(obj, "Value", s.Value);
                s.Min = Flt(obj, "Min", s.Min);
                s.Max = Flt(obj, "Max", s.Max);
                s.TrackColor = Vec4(obj, "TrackColor", s.TrackColor);
                s.FillColor = Vec4(obj, "FillColor", s.FillColor);
                s.HandleColor = Vec4(obj, "HandleColor", s.HandleColor);
                s.HandleWidth = Flt(obj, "HandleWidth", s.HandleWidth);
                break;
            case Toggle tg:
                tg.On = Bool(obj, "On", tg.On);
                tg.Label = Str(obj, "Label");
                tg.FontRef = Str(obj, "FontRef");
                tg.FontSize = Flt(obj, "FontSize", tg.FontSize);
                tg.TextColor = Vec4(obj, "TextColor", tg.TextColor);
                tg.BoxColor = Vec4(obj, "BoxColor", tg.BoxColor);
                tg.HoverColor = Vec4(obj, "HoverColor", tg.HoverColor);
                tg.CheckColor = Vec4(obj, "CheckColor", tg.CheckColor);
                tg.Font = LoadFont(tg.FontRef);
                break;
        }

        if (obj.TryGetPropertyValue("Children", out JsonNode? children) && children is JsonArray array)
        {
            foreach (JsonNode? node in array)
            {
                Widget? child = ReadWidget(node as JsonObject);
                if (child is not null) widget.Add(child);
            }
        }

        return widget;
    }

    private static UIRect ReadRect(JsonObject obj) => new()
    {
        Anchor = Vec2(obj, "Anchor", Vector2.Zero),
        Pivot = Vec2(obj, "Pivot", Vector2.Zero),
        Position = Vec2(obj, "Position", Vector2.Zero),
        Size = Vec2(obj, "Size", new Vector2(100f, 100f)),
    };

    /// <summary>Creates an empty widget for a type name, or null when the name is unknown.</summary>
    /// <param name="type">The serialized widget type name.</param>
    public static Widget? Create(string type) => type switch
    {
        TypePanel => new Panel(),
        TypeButton => new Button(),
        TypeText => new Text(),
        TypeImage => new Image(),
        TypeSlider => new Slider(),
        TypeToggle => new Toggle(),
        _ => null,
    };

    /// <summary>Returns the serialized type name for a widget, or an empty string for an unknown type.</summary>
    /// <param name="widget">The widget to name.</param>
    public static string TypeName(Widget widget) => widget switch
    {
        Panel => TypePanel,
        Button => TypeButton,
        Text => TypeText,
        Image => TypeImage,
        Slider => TypeSlider,
        Toggle => TypeToggle,
        _ => "",
    };

    /// <summary>Deep-clones a widget subtree via a serialize/deserialize round-trip (used by editor duplicate).</summary>
    /// <param name="widget">The widget to clone.</param>
    public static Widget Clone(Widget widget) => ReadWidget(WriteWidget(widget))!;

    // ----- helpers ---------------------------------------------------------------------------------

    private static Texture2D? LoadTexture(string reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        try
        {
            return Texture2D.Load(reference);
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to load UI texture '{0}': {1}", reference, ex.Message);
            return null;
        }
    }

    private static Font? LoadFont(string reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        try
        {
            return Font.Load(reference);
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to load UI font '{0}': {1}", reference, ex.Message);
            return null;
        }
    }

    private static JsonArray Vec2(Vector2 v) => new(v.X, v.Y);

    private static JsonArray Vec4(Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    private static string Str(JsonObject obj, string key) =>
        obj.TryGetPropertyValue(key, out JsonNode? n) && n is not null ? n.GetValue<string>() : "";

    private static bool Bool(JsonObject obj, string key, bool fallback) =>
        obj.TryGetPropertyValue(key, out JsonNode? n) && n is not null ? n.GetValue<bool>() : fallback;

    private static float Flt(JsonObject obj, string key, float fallback) =>
        obj.TryGetPropertyValue(key, out JsonNode? n) && n is not null ? n.GetValue<float>() : fallback;

    private static int Int(JsonObject obj, string key, int fallback) =>
        obj.TryGetPropertyValue(key, out JsonNode? n) && n is not null ? n.GetValue<int>() : fallback;

    private static Vector2 Vec2(JsonObject obj, string key, Vector2 fallback)
    {
        if (obj.TryGetPropertyValue(key, out JsonNode? n) && n is JsonArray a && a.Count >= 2)
        {
            return new Vector2(a[0]!.GetValue<float>(), a[1]!.GetValue<float>());
        }

        return fallback;
    }

    private static Vector4 Vec4(JsonObject obj, string key, Vector4 fallback)
    {
        if (obj.TryGetPropertyValue(key, out JsonNode? n) && n is JsonArray a && a.Count >= 4)
        {
            return new Vector4(a[0]!.GetValue<float>(), a[1]!.GetValue<float>(), a[2]!.GetValue<float>(), a[3]!.GetValue<float>());
        }

        return fallback;
    }
}
