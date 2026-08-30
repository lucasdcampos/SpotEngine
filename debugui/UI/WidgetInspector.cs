using System;
using System.Numerics;
using ImGuiNET;
using Spot.Assets;
using Spot.Core;
using Spot.Rendering;
using Spot.UI;

namespace Spot.DebugUI.UI;

/// <summary>
/// Draws the inspector for a selected UI <see cref="Widget"/>, mirroring the component inspector: a shared
/// section for name, rectangle and visibility, then per-type fields. Reuses the same <see cref="EditorGui"/>
/// row helpers and asset slots so authoring UI feels like editing an entity. Editing mutates the live widget
/// tree the canvas renders, so changes are immediate; the document is written to disk on save.
/// </summary>
public static class WidgetInspector
{
    private static readonly string[] ImagePatterns = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp" };
    private static readonly string[] FontPatterns = { "*.ttf", "*.otf" };

    /// <summary>
    /// Raised when the user asks to edit a UI document (e.g. the "Edit UI" button on a UICanvasComponent),
    /// carrying the document's source path. The editor subscribes to open it in the UI authoring panels.
    /// </summary>
    public static Action<string>? OpenDocumentRequested;

    /// <summary>Draws the inspector body for the given widget.</summary>
    /// <param name="widget">The selected widget to edit.</param>
    public static void Draw(Widget widget)
    {
        ImGui.TextDisabled(TypeLabel(widget));
        ImGui.Separator();
        ImGui.Spacing();

        string name = widget.Name;
        if (EditorGui.InputText("Name", ref name))
            widget.Name = name;

        bool visible = widget.Visible;
        if (EditorGui.Checkbox("Visible", ref visible))
            widget.Visible = visible;

        bool enabled = widget.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled))
            widget.Enabled = enabled;

        ImGui.Spacing();
        ImGui.TextDisabled("Rect");
        ImGui.Separator();
        UIRect rect = widget.Rect;
        bool rectChanged = false;
        rectChanged |= EditorGui.Vector2Control("Anchor", ref rect.Anchor, 0.0f, 0.01f);
        rectChanged |= EditorGui.Vector2Control("Pivot", ref rect.Pivot, 0.0f, 0.01f);
        rectChanged |= EditorGui.Vector2Control("Position", ref rect.Position, 0.0f, 1.0f);
        rectChanged |= EditorGui.Vector2Control("Size", ref rect.Size, 0.0f, 1.0f);
        if (rectChanged)
            widget.Rect = rect;

        ImGui.Spacing();
        ImGui.TextDisabled("Appearance");
        ImGui.Separator();

        switch (widget)
        {
            case Panel p: DrawPanel(p); break;
            case Button b: DrawButton(b); break;
            case Text t: DrawText(t); break;
            case Image img: DrawImage(img); break;
            case Slider s: DrawSlider(s); break;
            case Toggle tg: DrawToggle(tg); break;
        }
    }

    private static void DrawPanel(Panel p)
    {
        Color4("Color", ref p.Color);
        TextureSlot("Sprite", ref p.SpriteRef, t => p.Sprite = t);
    }

    private static void DrawButton(Button b)
    {
        InputText("Label", ref b.Label);
        FontSlot("Font", ref b.FontRef, f => b.Font = f);
        DragFloat("Font Size", ref b.FontSize, 0.5f, 1.0f, 200.0f);
        Color4("Text Color", ref b.TextColor);
        Color4("Normal", ref b.NormalColor);
        Color4("Hover", ref b.HoverColor);
        Color4("Pressed", ref b.PressedColor);
        TextureSlot("Sprite", ref b.SpriteRef, t => b.Sprite = t);
    }

    private static void DrawText(Text t)
    {
        InputText("Content", ref t.Content, 1024);
        FontSlot("Font", ref t.FontRef, f => t.Font = f);
        DragFloat("Font Size", ref t.FontSize, 0.5f, 1.0f, 200.0f);
        Color4("Color", ref t.Color);

        string[] names = Enum.GetNames(typeof(TextAlign));
        int align = (int)t.Align;
        if (EditorGui.Combo("Align", ref align, names))
            t.Align = (TextAlign)align;

        bool wrap = t.Wrap;
        if (EditorGui.Checkbox("Wrap", ref wrap))
            t.Wrap = wrap;
    }

    private static void DrawImage(Image img)
    {
        TextureSlot("Texture", ref img.TextureRef, t => img.Texture = t);
        Color4("Color", ref img.Color);
    }

    private static void DrawSlider(Slider s)
    {
        DragFloat("Value", ref s.Value, 0.01f, 0.0f, 0.0f);
        DragFloat("Min", ref s.Min, 0.01f, 0.0f, 0.0f);
        DragFloat("Max", ref s.Max, 0.01f, 0.0f, 0.0f);
        DragFloat("Handle Width", ref s.HandleWidth, 0.5f, 1.0f, 200.0f);
        Color4("Track", ref s.TrackColor);
        Color4("Fill", ref s.FillColor);
        Color4("Handle", ref s.HandleColor);
    }

    private static void DrawToggle(Toggle tg)
    {
        bool on = tg.On;
        if (EditorGui.Checkbox("On", ref on))
            tg.On = on;
        InputText("Label", ref tg.Label);
        FontSlot("Font", ref tg.FontRef, f => tg.Font = f);
        DragFloat("Font Size", ref tg.FontSize, 0.5f, 1.0f, 200.0f);
        Color4("Text Color", ref tg.TextColor);
        Color4("Box", ref tg.BoxColor);
        Color4("Hover", ref tg.HoverColor);
        Color4("Check", ref tg.CheckColor);
    }

    // ----- field helpers (widgets expose public fields, so pass by ref) ----------------------------

    private static void InputText(string label, ref string value, uint max = 256)
    {
        string v = value;
        if (EditorGui.InputText(label, ref v, max))
            value = v;
    }

    private static void DragFloat(string label, ref float value, float speed, float min, float max)
    {
        float v = value;
        if (EditorGui.DragFloat(label, ref v, speed, min, max))
            value = v;
    }

    private static void Color4(string label, ref Vector4 value)
    {
        Vector4 v = value;
        if (EditorGui.Color4(label, ref v))
            value = v;
    }

    private static void TextureSlot(string label, ref string reference, Action<Texture2D?> assign)
    {
        string? display = AssetDatabase.ToDisplayPath(string.IsNullOrEmpty(reference) ? null : reference);
        if (EditorGui.AssetSlot(label, "IMAGE_FILE", ImagePatterns, display, out string? newPath))
        {
            string? guid = AssetDatabase.ToGuidRef(newPath);
            reference = guid ?? string.Empty;
            assign(string.IsNullOrEmpty(reference) ? null : SafeLoadTexture(reference));
        }
    }

    private static void FontSlot(string label, ref string reference, Action<Font?> assign)
    {
        string? display = AssetDatabase.ToDisplayPath(string.IsNullOrEmpty(reference) ? null : reference);
        if (EditorGui.AssetSlot(label, "FONT_FILE", FontPatterns, display, out string? newPath))
        {
            string? guid = AssetDatabase.ToGuidRef(newPath);
            reference = guid ?? string.Empty;
            assign(string.IsNullOrEmpty(reference) ? null : SafeLoadFont(reference));
        }
    }

    private static Texture2D? SafeLoadTexture(string reference)
    {
        try { return Texture2D.Load(reference); }
        catch (Exception ex) { Log.Error("Failed to load UI texture '{0}': {1}", reference, ex.Message); return null; }
    }

    private static Font? SafeLoadFont(string reference)
    {
        try { return Font.Load(reference); }
        catch (Exception ex) { Log.Error("Failed to load UI font '{0}': {1}", reference, ex.Message); return null; }
    }

    private static string TypeLabel(Widget widget) => widget switch
    {
        Panel => "Panel",
        Button => "Button",
        Text => "Text",
        Image => "Image",
        Slider => "Slider",
        Toggle => "Toggle",
        _ => "Widget",
    };
}
