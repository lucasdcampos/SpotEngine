using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ImGuiNET;
using Spot.DebugUI.UI;
using Spot.Engine.Scenes;

namespace Spot.Engine.Tests;

/// <summary>
/// The tests that install a stand-in <see cref="Application.Instance"/> (needed by DebugUI's <see cref="EditorFonts"/>)
/// run alone, so the fake never leaks into a test running alongside them.
/// </summary>
[CollectionDefinition(nameof(ApplicationInstanceCollection), DisableParallelization = true)]
public sealed class ApplicationInstanceCollection
{
}

/// <summary>
/// Pins the ID-stack balance of the inspector's row helpers. The native cimgui ships with ImGui's asserts compiled
/// out, so a helper that pops one ID too many doesn't fail fast: it drives the window's ID stack negative, the next
/// PushID writes in front of the stack's heap buffer, and the editor dies with STATUS_HEAP_CORRUPTION when ImGui
/// later frees that buffer (about a minute after the window goes idle). Each helper must leave the stack as it found it.
/// </summary>
[Collection(nameof(ApplicationInstanceCollection))]
public sealed class EditorGuiIdStackTests : IDisposable
{
    private static readonly FieldInfo s_instanceField =
        typeof(Application).GetField("s_instance", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly Dictionary<string, Action> s_helpers = new()
    {
        ["Vector3Control"] = () => { Vector3 v = Vector3.One; EditorGui.Vector3Control("Position", ref v); },
        ["Vector2Control"] = () => { Vector2 v = Vector2.One; EditorGui.Vector2Control("Size", ref v); },
        ["DragFloat"] = () => { float v = 1.0f; EditorGui.DragFloat("Mass", ref v); },
        ["Checkbox"] = () => { bool v = true; EditorGui.Checkbox("Cast Shadows", ref v); },
        ["Color3"] = () => { Vector3 v = Vector3.One; EditorGui.Color3("Tint", ref v); },
        ["Color4"] = () => { Vector4 v = Vector4.One; EditorGui.Color4("Color", ref v); },
        ["Combo"] = () => { int v = 0; EditorGui.Combo("Mode", ref v, new[] { "A", "B" }); },
        ["InputText"] = () => { string v = "Player"; EditorGui.InputText("Name", ref v); },
        ["AssetSlot (empty)"] = () => EditorGui.AssetSlot("Mesh", "MODEL_FILE", new[] { "*.fbx" }, null, out _),
        ["AssetSlot (set)"] = () => EditorGui.AssetSlot("Mesh", "MODEL_FILE", new[] { "*.fbx" }, "Models/rock.fbx", out _),
        ["EntityField"] = () => { Entity v = default; EditorGui.EntityField("Target", new Scene(), ref v); },
        ["ScriptSlot"] = () => EditorGui.ScriptSlot("Script", Array.Empty<string>(), out _),
    };

    private readonly object? _previousApplication;
    private readonly IntPtr _context;

    public EditorGuiIdStackTests()
    {
        // EditorFonts reads Application.Instance.Fonts; an uninitialized Application (no ImGui service, so no fonts)
        // makes it fall back to ImGui's current font.
        _previousApplication = s_instanceField.GetValue(null);
        s_instanceField.SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(Application)));

        _context = ImGui.CreateContext();
        ImGuiIOPtr io = ImGui.GetIO();
        unsafe
        {
            io.NativePtr->IniFilename = null; // never write an imgui.ini from a test
        }

        io.DisplaySize = new Vector2(1280.0f, 720.0f);
        io.DeltaTime = 1.0f / 60.0f;
        io.Fonts.AddFontDefault();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _);
        io.Fonts.SetTexID(1);
    }

    public static TheoryData<string> Helpers => new(s_helpers.Keys);

    [Theory]
    [MemberData(nameof(Helpers))]
    public void Helper_LeavesTheIdStackBalanced(string helper)
    {
        ImGui.NewFrame();
        ImGui.Begin("Inspector");

        // The same label hashes to the same ID only if the ID stack under it is unchanged.
        uint before = ImGui.GetID("probe");
        s_helpers[helper]();
        uint after = ImGui.GetID("probe");

        ImGui.End();
        ImGui.Render();

        Assert.Equal(before, after);
    }

    public void Dispose()
    {
        ImGui.DestroyContext(_context);
        s_instanceField.SetValue(null, _previousApplication);
    }
}
