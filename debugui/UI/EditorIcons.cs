using System.Collections.Generic;

namespace Spot.DebugUI.UI;

/// <summary>
/// Font Awesome 6 Free (Solid) icon glyphs, baked into the body font by the engine (see Program.cs /
/// <see cref="Spot.Engine.IconFontSpec"/>). Every codepoint is in the Basic Multilingual Plane, so each
/// icon is a single-char string that can be concatenated into any label — e.g.
/// <c>ImGui.Button(EditorIcons.Move)</c> or <c>$"{EditorIcons.Folder}  Assets"</c>. Codepoints come
/// from the canonical IconFontCppHeaders (IconsFontAwesome6.h).
/// </summary>
public static class EditorIcons
{
    public const string Camera = "";     // camera
    public const string Video = "";      // video
    public const string Lightbulb = "";  // lightbulb
    public const string Cube = "";       // cube
    public const string Cubes = "";      // cubes
    public const string Image = "";      // image
    public const string Cloud = "";      // cloud
    public const string Circle = "";     // circle
    public const string Code = "";       // code
    public const string File = "";       // file
    public const string Folder = "";     // folder
    public const string FolderOpen = ""; // folder-open
    public const string Palette = "";    // palette
    public const string Sitemap = "";    // sitemap
    public const string Sun = "";        // sun
    public const string Gear = "";       // gear
    public const string Search = "";     // magnifying-glass
    public const string Move = "";       // up-down-left-right
    public const string Rotate = "";     // rotate
    public const string Scale = "";      // maximize
    public const string Play = "";       // play
    public const string Stop = "";       // stop
    public const string EllipsisV = "";  // ellipsis-vertical
    public const string Times = "";   // xmark (used as a "clear"/"none" affordance)
    public const string Music = "";   // music
    public const string Fire = "\uf06d";    // fire (used for particles)
    public const string Gamepad = "\uf11b"; // gamepad (the viewport is looking through the game camera)
    public const string Eye = "\uf06e";          // eye
    public const string VectorSquare = "\uf5cb"; // vector-square (collider overlay toggle)
    public const string DrawPolygon = "\uf5ee";  // draw-polygon (wireframe toggle)
    public const string Plus = "\uf067";         // plus
    public const string CaretDown = "\uf0d7";    // caret-down (drop-down affordance)
    public const string Ellipsis = "\uf141";     // ellipsis (horizontal "more actions")
    public const string Book = "\uf02d";         // book (documentation)
    public const string CodeBranch = "\uf126";   // code-branch (source repository)
    public const string ExternalLink = "\uf08e"; // arrow-up-right-from-square (opens outside the app)
    public const string Grid = "\uf009";         // table-cells-large (grid view)
    public const string List = "\uf03a";         // list (list view)
    public const string Sort = "\uf160";         // arrow-down-wide-short
    public const string Clock = "\uf017";        // clock
    public const string Check = "\uf00c";        // check
    public const string Copy = "\uf0c5";         // copy
    public const string Trash = "\uf2ed";        // trash-can
    public const string Warning = "\uf071";      // triangle-exclamation

    // Every codepoint above; the engine bakes exactly these (as an ImGui [lo,hi,…,0] range) so the
    // atlas stays small instead of loading all ~1500 Font Awesome glyphs.
    private static readonly ushort[] Codepoints =
    {
        0xf030, 0xf03d, 0xf0eb, 0xf1b2, 0xf1b3, 0xf03e, 0xf0c2, 0xf111,
        0xf121, 0xf15b, 0xf07b, 0xf07c, 0xf53f, 0xf0e8, 0xf185, 0xf013,
        0xf002, 0xf0b2, 0xf2f1, 0xf31e, 0xf04b, 0xf04d, 0xf142, 0xf00d,
        0xf001, 0xf06d, 0xf11b, 0xf06e, 0xf5cb, 0xf5ee, 0xf067, 0xf0d7,
        0xf141, 0xf02d, 0xf126, 0xf08e, 0xf009, 0xf03a, 0xf160, 0xf017,
        0xf00c, 0xf0c5, 0xf2ed, 0xf071,
    };

    /// <summary>The ImGui glyph range (<c>[lo, hi, …, 0]</c>) covering exactly <see cref="Codepoints"/>.</summary>
    public static ushort[] GlyphRanges { get; } = BuildRanges();

    private static ushort[] BuildRanges()
    {
        var ranges = new List<ushort>(Codepoints.Length * 2 + 1);
        foreach (ushort cp in Codepoints)
        {
            ranges.Add(cp);
            ranges.Add(cp);
        }
        ranges.Add(0);
        return ranges.ToArray();
    }
}
