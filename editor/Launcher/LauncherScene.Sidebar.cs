using System;
using System.Numerics;
using ImGuiNET;
using Spot.DebugUI.UI;
using Spot.Engine;
using static Spot.Editor.Launcher.LauncherColors;
using static Spot.Editor.Launcher.LauncherWidgets;

namespace Spot.Editor.Launcher;

// Left column: the brand, the two primary actions, and the resource links pinned to the bottom.
public partial class LauncherScene
{
    private const float LinkRowHeight = 30.0f;

    private void DrawSidebar(in LauncherColors c)
    {
        // AlwaysUseWindowPadding: borderless children ignore WindowPadding otherwise. The padding is popped right
        // after BeginChild (which captures it) so tooltips opened inside do not inherit it.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, c.Sidebar);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(LauncherLayout.SidebarPaddingX, LauncherLayout.TopInset));
        ImGui.BeginChild("##sidebar", new Vector2(LauncherLayout.SidebarWidth, 0), ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        float width = ImGui.GetContentRegionAvail().X;
        DrawBrand(c, width);
        ImGui.Dummy(new Vector2(0, Space.Xxl));

        var buttonSize = new Vector2(width, LauncherLayout.ButtonHeight);
        if (Button("##new", EditorIcons.Plus, "New Project", buttonSize, ButtonKind.Primary, c, "Create a project (Ctrl+N)"))
        {
            OpenNewProjectDialog();
        }
        ImGui.Dummy(new Vector2(0, Space.Sm));
        if (Button("##open", EditorIcons.FolderOpen, "Open Project", buttonSize, ButtonKind.Secondary, c,
                "Open a .sptproj from disk (Ctrl+O)"))
        {
            _pendingBrowse = true;
        }

        DrawSidebarFooter(c, width);

        ImGui.PopStyleVar();
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    // The mark beside the engine name, with the version as its caption — the one place the launcher shows it.
    private static void DrawBrand(in LauncherColors c, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        Vector2 p = ImGui.GetCursorScreenPos();
        float size = LauncherLayout.HeaderHeight;
        DrawLogo(dl, p, size, c);

        ImFontPtr heading = EditorFonts.Heading;
        ImFontPtr small = EditorFonts.Small;
        const float lineGap = 1.0f;
        float blockHeight = heading.FontSize + lineGap + small.FontSize;
        float x = p.X + size + Space.Md;
        float y = p.Y + (size - blockHeight) * 0.5f;
        Text(dl, heading, new Vector2(x, y), c.Text, "Spot Engine");
        Text(dl, small, new Vector2(x, y + heading.FontSize + lineGap), c.TextMuted,
            $"Version {Application.Instance.EngineVersion}");

        ImGui.Dummy(new Vector2(width, size));
    }

    // The "spot" brand mark: a rounded accent tile with a ring and an offset dot.
    private static void DrawLogo(ImDrawListPtr dl, Vector2 p0, float s, in LauncherColors c)
    {
        Vector2 p1 = p0 + new Vector2(s, s);
        float rounding = MathF.Round(s * 0.26f);
        GradientRect(dl, p0, p1, Mix(c.Accent, Vector4.One, 0.14f), Mix(c.Accent, new Vector4(0, 0, 0, 1), 0.12f),
            rounding, ImDrawFlags.None);
        dl.AddRect(p0, p1, U32(WithAlpha(Vector4.One, 0.10f)), rounding);

        dl.AddCircle(p0 + new Vector2(s * 0.40f, s * 0.40f), s * 0.24f, U32(WithAlpha(Vector4.One, 0.6f)), 0, s * 0.065f);
        dl.AddCircleFilled(p0 + new Vector2(s * 0.63f, s * 0.63f), s * 0.15f, U32(WithAlpha(Vector4.One, 0.96f)));
    }

    // Documentation and repository links, then the copyright line, pinned to the bottom of the column.
    private void DrawSidebarFooter(in LauncherColors c, float width)
    {
        ImFontPtr small = EditorFonts.Small;
        float footerHeight = LinkRowHeight * 2 + Space.Md + small.FontSize;
        float top = ImGui.GetWindowHeight() - LauncherLayout.BottomInset - footerHeight;
        // In a window too short to fit everything the footer flows after the buttons instead of overlapping them.
        if (top > ImGui.GetCursorPosY()) ImGui.SetCursorPosY(top);

        if (LinkRow("##docs", EditorIcons.Book, "Documentation", width, c, DocsUrl)) OpenExternally(DocsUrl);
        if (LinkRow("##repo", EditorIcons.CodeBranch, "GitHub Repository", width, c, RepoUrl)) OpenExternally(RepoUrl);

        ImGui.Dummy(new Vector2(0, Space.Md));
        Text(ImGui.GetWindowDrawList(), small, ImGui.GetCursorScreenPos(), c.TextFaint, $"© {DateTime.Now.Year} Spot Engine");
        ImGui.Dummy(new Vector2(width, small.FontSize));
    }
}
