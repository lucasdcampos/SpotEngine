using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using Spot.Assets;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Editor.UI;

/// <summary>
/// Draws component inspectors automatically from component metadata. Each component "teaches" the
/// editor how to draw it through the attributes declared in the engine (<see cref="ComponentMenuAttribute"/>,
/// <see cref="InspectorRangeAttribute"/>, <see cref="InspectorColorAttribute"/>, <see cref="ShowIfAttribute"/>,
/// <see cref="AssetReferenceAttribute"/>, ...); this class reflects over a component's public properties and
/// renders each with the matching <see cref="EditorGui"/> widget, so the inspector needs no per-component code.
/// Property types that need bespoke UI (asset slots) and whole components that do (the script list) register a
/// custom drawer here.
/// </summary>
internal static class ComponentInspector
{
    // ----- Component discovery ---------------------------------------------------------------------

    /// <summary>Display metadata for one user-facing component type.</summary>
    public sealed class ComponentTypeInfo(Type type, string displayName, bool addable, bool removable, int order)
    {
        public Type Type { get; } = type;
        public string DisplayName { get; } = displayName;
        public bool Addable { get; } = addable;
        public bool Removable { get; } = removable;
        public int Order { get; } = order;
    }

    private static List<ComponentTypeInfo>? _componentTypes;

    /// <summary>All component types carrying a <see cref="ComponentMenuAttribute"/>, ordered for display.</summary>
    public static IReadOnlyList<ComponentTypeInfo> ComponentTypes => _componentTypes ??= DiscoverComponents();

    private static List<ComponentTypeInfo> DiscoverComponents()
    {
        var list = new List<ComponentTypeInfo>();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                // A partially-loadable assembly (ReflectionTypeLoadException, etc.) should never take the
                // editor down — just skip it.
                continue;
            }

            foreach (Type type in types)
            {
                if (type.IsAbstract || !typeof(Component).IsAssignableFrom(type))
                    continue;
                var menu = type.GetCustomAttribute<ComponentMenuAttribute>();
                if (menu == null)
                    continue;
                list.Add(new ComponentTypeInfo(type, menu.DisplayName, menu.Addable, menu.Removable, menu.Order));
            }
        }

        list.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.DisplayName, b.DisplayName));
        return list;
    }

    // ----- Public entry points ---------------------------------------------------------------------

    /// <summary>Draws the collapsible header and body for the component of <paramref name="info"/> if present.</summary>
    public static void DrawComponent(Entity entity, ComponentTypeInfo info)
    {
        EditorGui.Component(entity, info.Type, info.DisplayName, info.Removable, () =>
        {
            object? component = entity.GetComponent(info.Type);
            if (component != null)
                DrawComponentBody(entity, component, info.Type, showEnabled: info.Removable);
        });
    }

    /// <summary>
    /// Draws a component's contents. If a custom whole-component drawer is registered for the type it is used;
    /// otherwise each visible property is drawn by reflection. <paramref name="showEnabled"/> mirrors the old
    /// inspector, where core (non-removable) components like Transform had no Enabled toggle.
    /// </summary>
    public static void DrawComponentBody(Entity entity, object component, Type type, bool showEnabled = true)
    {
        if (_componentDrawers.TryGetValue(type, out var custom))
        {
            try
            {
                custom(entity, component);
            }
            catch (Exception ex)
            {
                Log.Error("Inspector drawer for '{0}' failed: {1}", type.Name, ex.Message);
            }
            return;
        }

        if (showEnabled && component is Component baseComponent)
        {
            bool enabled = baseComponent.Enabled;
            if (EditorGui.Checkbox("Enabled", ref enabled))
                baseComponent.Enabled = enabled;
        }

        foreach (PropertyMeta meta in MetaFor(type))
        {
            if (!ShowIfSatisfied(meta, component))
                continue;

            ImGui.PushID(meta.Prop.Name);
            try
            {
                DrawProperty(entity, component, meta);
            }
            catch (Exception ex)
            {
                // One faulty property must not break the rest of the inspector.
                Log.Error("Inspector failed to draw '{0}.{1}': {2}", type.Name, meta.Prop.Name, ex.Message);
            }
            finally
            {
                ImGui.PopID();
            }
        }
    }

    // ----- Property metadata cache -----------------------------------------------------------------

    private sealed class PropertyMeta(PropertyInfo prop)
    {
        public PropertyInfo Prop { get; } = prop;
        public string Label { get; set; } = prop.Name;
        public bool HasRange { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public float Speed { get; set; } = 0.1f;
        public bool IsColor { get; set; }
        public bool HasReset { get; set; }
        public float Reset { get; set; }
        public PropertyInfo? ShowIfProp { get; set; }
        public object[]? ShowIfValues { get; set; }
        public PropertyInfo? AssetPathProp { get; set; }
        public string[]? EnumNames { get; set; }
        public object[]? EnumValues { get; set; }
    }

    private static readonly Dictionary<Type, PropertyMeta[]> _metaCache = new();

    private static PropertyMeta[] MetaFor(Type type)
    {
        if (_metaCache.TryGetValue(type, out PropertyMeta[]? cached))
            return cached;

        var metas = new List<PropertyMeta>();
        IEnumerable<PropertyInfo> props = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType != typeof(Component)) // Enabled is drawn first, separately.
            .OrderBy(p => p.MetadataToken);

        foreach (PropertyInfo prop in props)
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;
            if (prop.GetCustomAttribute<HideInInspectorAttribute>() != null)
                continue;

            var assetRef = prop.GetCustomAttribute<AssetReferenceAttribute>();
            bool readWrite = prop.GetMethod is { IsPublic: true } && prop.SetMethod is { IsPublic: true };
            if (assetRef == null && !readWrite)
                continue; // computed / read-only, and not an asset slot.

            var meta = new PropertyMeta(prop);

            meta.Label = prop.GetCustomAttribute<InspectorLabelAttribute>()?.Label ?? Humanize(prop.Name);

            var range = prop.GetCustomAttribute<InspectorRangeAttribute>();
            if (range != null)
            {
                meta.HasRange = true;
                meta.Min = range.Min;
                meta.Max = range.Max;
                meta.Speed = range.Speed;
            }

            meta.IsColor = prop.GetCustomAttribute<InspectorColorAttribute>() != null;

            var reset = prop.GetCustomAttribute<InspectorResetAttribute>();
            if (reset != null)
            {
                meta.HasReset = true;
                meta.Reset = reset.Value;
            }

            var showIf = prop.GetCustomAttribute<ShowIfAttribute>();
            if (showIf != null)
            {
                meta.ShowIfProp = type.GetProperty(showIf.PropertyName, BindingFlags.Public | BindingFlags.Instance);
                meta.ShowIfValues = showIf.Values;
            }

            if (assetRef != null)
                meta.AssetPathProp = type.GetProperty(assetRef.PathPropertyName, BindingFlags.Public | BindingFlags.Instance);

            if (prop.PropertyType.IsEnum)
            {
                meta.EnumNames = Enum.GetNames(prop.PropertyType);
                meta.EnumValues = Enum.GetValues(prop.PropertyType).Cast<object>().ToArray();
            }

            metas.Add(meta);
        }

        PropertyMeta[] arr = metas.ToArray();
        _metaCache[type] = arr;
        return arr;
    }

    private static bool ShowIfSatisfied(PropertyMeta meta, object component)
    {
        if (meta.ShowIfProp == null || meta.ShowIfValues == null)
            return true;
        object? current = meta.ShowIfProp.GetValue(component);
        foreach (object expected in meta.ShowIfValues)
        {
            if (Equals(current, expected))
                return true;
        }
        return false;
    }

    // ----- Generic property drawing ----------------------------------------------------------------

    private static void DrawProperty(Entity entity, object component, PropertyMeta meta)
    {
        PropertyInfo prop = meta.Prop;
        Type pt = prop.PropertyType;

        // Asset reference slots (texture/model/material) get their own drag-drop UI.
        if (meta.AssetPathProp != null && _typeDrawers.TryGetValue(pt, out var assetDrawer))
        {
            assetDrawer(entity, component, meta);
            return;
        }

        string label = meta.Label;

        if (pt == typeof(float))
        {
            float v = (float)prop.GetValue(component)!;
            float speed = meta.HasRange ? meta.Speed : 0.1f;
            float min = meta.HasRange ? meta.Min : 0.0f;
            float max = meta.HasRange ? meta.Max : 0.0f;
            if (EditorGui.DragFloat(label, ref v, speed, min, max))
                prop.SetValue(component, v);
        }
        else if (pt == typeof(bool))
        {
            bool v = (bool)prop.GetValue(component)!;
            if (EditorGui.Checkbox(label, ref v))
                prop.SetValue(component, v);
        }
        else if (pt.IsEnum)
        {
            object cur = prop.GetValue(component)!;
            int idx = Array.IndexOf(meta.EnumValues!, cur);
            if (idx < 0) idx = 0;
            if (EditorGui.Combo(label, ref idx, meta.EnumNames!))
                prop.SetValue(component, meta.EnumValues![idx]);
        }
        else if (pt == typeof(Vector2))
        {
            var v = (Vector2)prop.GetValue(component)!;
            if (EditorGui.Vector2Control(label, ref v, meta.HasReset ? meta.Reset : 0.0f))
                prop.SetValue(component, v);
        }
        else if (pt == typeof(Vector3))
        {
            var v = (Vector3)prop.GetValue(component)!;
            bool changed = meta.IsColor
                ? EditorGui.Color3(label, ref v)
                : EditorGui.Vector3Control(label, ref v, meta.HasReset ? meta.Reset : 0.0f);
            if (changed)
                prop.SetValue(component, v);
        }
        else if (pt == typeof(Vector4))
        {
            // Every Vector4 the inspector shows is a color; there is no plain 4-axis control.
            var v = (Vector4)prop.GetValue(component)!;
            if (EditorGui.Color4(label, ref v))
                prop.SetValue(component, v);
        }
        else if (pt == typeof(string))
        {
            string v = (string?)prop.GetValue(component) ?? string.Empty;
            if (EditorGui.InputText(label, ref v))
                prop.SetValue(component, v);
        }
        // Unknown/unsupported types are silently skipped.
    }

    /// <summary>Turns a PascalCase property name into spaced words ("FieldOfView" → "Field Of View").</summary>
    private static string Humanize(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) &&
                (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ----- Custom drawers --------------------------------------------------------------------------

    private static readonly Dictionary<Type, Action<Entity, object, PropertyMeta>> _typeDrawers = new()
    {
        [typeof(Texture2D)] = DrawTextureSlot,
        [typeof(Model)] = DrawModelSlot,
        [typeof(Material)] = DrawMaterialSlot,
    };

    private static readonly Dictionary<Type, Action<Entity, object>> _componentDrawers = new()
    {
        [typeof(ScriptComponent)] = DrawScriptComponent,
    };

    private static void DrawTextureSlot(Entity entity, object component, PropertyMeta meta)
    {
        var texture = (Texture2D?)meta.Prop.GetValue(component);
        string? path = (string?)meta.AssetPathProp!.GetValue(component);

        ImGui.TextUnformatted(meta.Label);
        ImGui.Button(texture != null
            ? $"{System.IO.Path.GetFileName(path) ?? "Texture"} (Drop to change)"
            : "Drop Texture Here", new Vector2(-1, 30));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("IMAGE_FILE");
                if (payload.NativePtr != null)
                {
                    string? filepath = Marshal.PtrToStringUTF8(payload.Data);
                    if (filepath != null)
                    {
                        try
                        {
                            var newTexture = new Texture2D(filepath);
                            texture?.Dispose();
                            meta.Prop.SetValue(component, newTexture);
                            meta.AssetPathProp.SetValue(component, filepath);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Failed to load texture: {0}", ex.Message);
                        }
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (texture != null && ImGui.Button($"Remove {meta.Label}", new Vector2(-1, 24)))
        {
            texture.Dispose();
            meta.Prop.SetValue(component, null);
            meta.AssetPathProp.SetValue(component, null);
        }
    }

    private static void DrawModelSlot(Entity entity, object component, PropertyMeta meta)
    {
        var model = (Model?)meta.Prop.GetValue(component);
        string? path = (string?)meta.AssetPathProp!.GetValue(component);

        ImGui.TextUnformatted(meta.Label);
        string modelLabel = model != null
            ? $"{System.IO.Path.GetFileName(path) ?? "Model"} (Drop to change)"
            : "Drop 3D Model Here";
        bool clicked = ImGui.Button(modelLabel, new Vector2(-1, 30));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("MODEL_FILE");
                if (payload.NativePtr != null)
                {
                    string? filepath = Marshal.PtrToStringUTF8(payload.Data);
                    if (filepath != null)
                    {
                        try
                        {
                            meta.Prop.SetValue(component, Model.Load(filepath));
                            meta.AssetPathProp.SetValue(component, filepath);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Failed to load model: {0}", ex.Message);
                        }
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (clicked)
            ImGui.OpenPopup("SelectModelPopup");
        if (ImGui.BeginPopup("SelectModelPopup"))
        {
            if (ImGui.MenuItem("None", "", model == null))
            {
                meta.Prop.SetValue(component, null);
                meta.AssetPathProp.SetValue(component, null);
            }
            ImGui.Separator();
            DrawPrimitiveItem(component, meta, "Cube", path);
            DrawPrimitiveItem(component, meta, "Plane", path);
            DrawPrimitiveItem(component, meta, "Quad", path);
            DrawPrimitiveItem(component, meta, "Sphere", path);
            ImGui.EndPopup();
        }
    }

    private static void DrawPrimitiveItem(object component, PropertyMeta meta, string name, string? currentPath)
    {
        string primPath = "primitive:" + name;
        if (ImGui.MenuItem(name, "", currentPath == primPath))
        {
            meta.AssetPathProp!.SetValue(component, primPath);
            meta.Prop.SetValue(component, Model.Load(primPath));
        }
    }

    private static void DrawMaterialSlot(Entity entity, object component, PropertyMeta meta)
    {
        var material = (Material?)meta.Prop.GetValue(component);
        string? path = (string?)meta.AssetPathProp!.GetValue(component);

        ImGui.TextUnformatted(meta.Label);
        string materialLabel = material != null
            ? System.IO.Path.GetFileName(path) ?? "Material"
            : "Select Material...";
        bool clicked = ImGui.Button(materialLabel, new Vector2(-1, 30));

        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("MATERIAL_FILE");
                if (payload.NativePtr != null)
                {
                    string? filepath = Marshal.PtrToStringUTF8(payload.Data);
                    if (filepath != null)
                        AssignMaterial(component, meta, filepath);
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (clicked)
            ImGui.OpenPopup("SelectMaterialPopup");
        if (ImGui.BeginPopup("SelectMaterialPopup"))
        {
            if (ImGui.MenuItem("None", "", material == null))
            {
                meta.Prop.SetValue(component, null);
                meta.AssetPathProp.SetValue(component, null);
            }
            if (ImGui.MenuItem("Checkerboard", "", path == "editor:Checkerboard"))
                AssignMaterial(component, meta, "editor:Checkerboard");

            List<string> materials = EnumerateProjectMaterials();
            if (materials.Count > 0)
                ImGui.Separator();
            foreach (string matPath in materials)
            {
                bool isSelected = string.Equals(path, matPath, StringComparison.OrdinalIgnoreCase);
                if (ImGui.MenuItem(System.IO.Path.GetFileName(matPath), "", isSelected))
                    AssignMaterial(component, meta, matPath);
            }
            if (materials.Count == 0)
                ImGui.TextDisabled("No materials in project.");

            ImGui.EndPopup();
        }
    }

    private static void AssignMaterial(object component, PropertyMeta meta, string path)
    {
        try
        {
            meta.Prop.SetValue(component, Material.Load(path));
            meta.AssetPathProp!.SetValue(component, path);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load material '{0}': {1}", path, ex.Message);
        }
    }

    private static List<string> EnumerateProjectMaterials()
    {
        var result = new List<string>();
        string? dir = Project.Active?.GetAssetDirectory();
        if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
        {
            try
            {
                result.AddRange(System.IO.Directory.EnumerateFiles(dir, "*.sptmat", System.IO.SearchOption.AllDirectories));
            }
            catch
            {
                // Enumeration failures (permissions, race with deletion) just yield no materials.
            }
        }
        return result;
    }

    private static void DrawScriptComponent(Entity entity, object component)
    {
        var scriptComp = (ScriptComponent)component;
        bool enabled = scriptComp.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled))
            scriptComp.Enabled = enabled;

        int scriptToRemove = -1;
        for (int i = 0; i < scriptComp.ClassNames.Count; i++)
        {
            string className = scriptComp.ClassNames[i];
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 30.0f);
            if (ImGui.InputText($"##Script{i}", ref className, 256))
                scriptComp.ClassNames[i] = className;
            ImGui.SameLine();
            if (ImGui.Button($"X##{i}"))
                scriptToRemove = i;
        }

        if (scriptToRemove >= 0)
            scriptComp.ClassNames.RemoveAt(scriptToRemove);

        ImGui.Button("Drop Script Here", new Vector2(-1, 30));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("SCRIPT_FILE");
                if (payload.NativePtr != null)
                {
                    string? filename = Marshal.PtrToStringUTF8(payload.Data);
                    if (filename != null && filename.EndsWith(".cs"))
                    {
                        string cName = filename.Substring(0, filename.Length - 3);
                        if (!scriptComp.ClassNames.Contains(cName))
                            scriptComp.ClassNames.Add(cName);
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }
    }
}
