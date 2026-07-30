using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace Spot.Editor.UI;

/// <summary>
/// Thin P/Invoke shim over Dear ImGui's DockBuilder API. These functions live in imgui_internal.h
/// and are compiled into the native cimgui library that ImGui.NET already loads, but the managed
/// ImGui.NET binding does not surface them. We import them directly so the editor can build a
/// default docked layout programmatically (used for first launch and "Reset Layout").
/// </summary>
internal static class ImGuiDock
{
    private const string Lib = "cimgui";

    // ImGuiDockNodeFlags_DockSpace, a private flag from imgui_internal.h (not in the public enum).
    public const int DockNodeFlagsDockSpace = 1 << 10;

    [DllImport(Lib)]
    public static extern void igDockBuilderRemoveNode(uint nodeId);

    [DllImport(Lib)]
    public static extern uint igDockBuilderAddNode(uint nodeId, int flags);

    [DllImport(Lib)]
    public static extern void igDockBuilderSetNodeSize(uint nodeId, Vector2 size);

    [DllImport(Lib)]
    public static extern uint igDockBuilderSplitNode(
        uint nodeId, ImGuiDir splitDir, float sizeRatioForNodeAtDir,
        out uint outIdAtDir, out uint outIdAtOppositeDir);

    [DllImport(Lib)]
    public static extern void igDockBuilderDockWindow(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string windowName, uint nodeId);

    [DllImport(Lib)]
    public static extern void igDockBuilderFinish(uint nodeId);
}
