using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
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

        string[] patterns = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp" };
        if (EditorGui.AssetSlot(meta.Label, "IMAGE_FILE", patterns, path, out string? newPath))
        {
            try
            {
                var newTexture = newPath != null ? new Texture2D(newPath) : null;
                texture?.Dispose();
                meta.Prop.SetValue(component, newTexture);
                meta.AssetPathProp!.SetValue(component, newPath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load texture: {0}", ex.Message);
            }
        }
    }

    private static void DrawModelSlot(Entity entity, object component, PropertyMeta meta)
    {
        var model = (Model?)meta.Prop.GetValue(component);
        string? path = (string?)meta.AssetPathProp!.GetValue(component);

        string[] patterns = { "*.obj", "*.fbx", "*.gltf", "*.glb", "*.dae" };
        
        EditorGui.AssetSlotCustomItems customItems = (ref string? selectedPath, ref bool changed) => 
        {
            if (ImGui.MenuItem("Cube", "", path == "primitive:Cube"))
            {
                selectedPath = "primitive:Cube";
                changed = true;
            }
            if (ImGui.MenuItem("Plane", "", path == "primitive:Plane"))
            {
                selectedPath = "primitive:Plane";
                changed = true;
            }
            if (ImGui.MenuItem("Quad", "", path == "primitive:Quad"))
            {
                selectedPath = "primitive:Quad";
                changed = true;
            }
            if (ImGui.MenuItem("Sphere", "", path == "primitive:Sphere"))
            {
                selectedPath = "primitive:Sphere";
                changed = true;
            }
        };

        if (EditorGui.AssetSlot(meta.Label, "MODEL_FILE", patterns, path, out string? newPath, customItems))
        {
            try
            {
                var newModel = newPath != null ? Model.Load(newPath) : null;
                meta.Prop.SetValue(component, newModel);
                meta.AssetPathProp!.SetValue(component, newPath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load model: {0}", ex.Message);
            }
        }
    }

    private static void DrawMaterialSlot(Entity entity, object component, PropertyMeta meta)
    {
        var material = (Material?)meta.Prop.GetValue(component);
        string? path = (string?)meta.AssetPathProp!.GetValue(component);

        string[] patterns = { "*.sptmat" };
        
        EditorGui.AssetSlotCustomItems customItems = (ref string? selectedPath, ref bool changed) => 
        {
            if (ImGui.MenuItem("Checkerboard", "", path == "editor:Checkerboard"))
            {
                selectedPath = "editor:Checkerboard";
                changed = true;
            }
        };

        if (EditorGui.AssetSlot(meta.Label, "MATERIAL_FILE", patterns, path, out string? newPath, customItems))
        {
            try
            {
                var newMaterial = newPath != null ? Material.Load(newPath) : null;
                meta.Prop.SetValue(component, newMaterial);
                meta.AssetPathProp!.SetValue(component, newPath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load material '{0}': {1}", newPath, ex.Message);
            }
        }
    }

    private static void DrawScriptComponent(Entity entity, object component)
    {
        var scriptComp = (ScriptComponent)component;
        bool enabled = scriptComp.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled))
            scriptComp.Enabled = enabled;

        // Each attached script is a row: a code glyph, its class name (dim + tagged when the type can't be
        // resolved), and a remove button. A missing type usually means the script hasn't compiled yet or was
        // renamed — surfacing it here beats a silent no-op at runtime.
        int scriptToRemove = -1;
        for (int i = 0; i < scriptComp.Items.Count; i++)
        {
            ScriptInstance item = scriptComp.Items[i];
            bool resolved = item.Instance != null;

            ImGui.PushID(i);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ScriptGlyphColor, EditorIcons.Code);
            ImGui.SameLine();
            if (resolved)
                ImGui.TextUnformatted(item.ClassName);
            else
                ImGui.TextColored(ScriptMissingColor, $"{item.ClassName}  (not found)");

            // Right-align the remove button to the card's inner edge.
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
            if (ImGui.Button(EditorIcons.Times, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight())))
                scriptToRemove = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove script");

            // Editable tunables: the script's public fields/properties, drawn indented under its row.
            if (item.Instance != null)
            {
                ImGui.Indent();
                DrawScriptFields(item.Instance);
                ImGui.Unindent();
            }

            ImGui.PopID();
        }

        if (scriptToRemove >= 0)
            scriptComp.Items.RemoveAt(scriptToRemove);

        ImGui.Spacing();
        // The slot both accepts a dragged script file and, on click, opens a searchable list of every
        // EntityBehaviour in the loaded assemblies — no more typing class names by hand. Adding one
        // instantiates it immediately so its fields are editable right away.
        if (EditorGui.ScriptSlot("Add Script", scriptComp.ClassNames.ToList(), out string? chosen)
            && chosen != null && !scriptComp.ClassNames.Contains(chosen))
        {
            scriptComp.Items.Add(new ScriptInstance(chosen, ScriptResolver.Create(chosen, entity)));
        }
    }

    private static readonly Vector4 ScriptGlyphColor = new(0.36f, 0.66f, 0.98f, 1.0f);
    private static readonly Vector4 ScriptMissingColor = new(0.95f, 0.70f, 0.25f, 1.0f);

    // ----- Script field editing --------------------------------------------------------------------

    private sealed class ScriptFieldMeta
    {
        public string Label = string.Empty;
        public Type Type = typeof(object);
        public Func<object, object?> Get = _ => null;
        public Action<object, object?> Set = (_, _) => { };
        public bool HasRange;
        public bool IsColor;
        public bool HasReset;
        public float Min;
        public float Max;
        public float Speed = 0.1f;
        public float Reset;
        public string[]? EnumNames;
        public object[]? EnumValues;
    }

    private static readonly Dictionary<Type, ScriptFieldMeta[]> _scriptFieldCache = new();

    // Draws a script's public fields and read/write properties as inline editors. Editing a value marks
    // the scene dirty automatically, since script fields are now part of the serialized scene.
    private static void DrawScriptFields(EntityBehaviour script)
    {
        foreach (ScriptFieldMeta meta in ScriptFieldsFor(script.GetType()))
        {
            ImGui.PushID(meta.Label);
            try
            {
                DrawScriptField(script, meta);
            }
            catch (Exception ex)
            {
                Log.Error("Inspector failed to draw script field '{0}': {1}", meta.Label, ex.Message);
            }
            finally
            {
                ImGui.PopID();
            }
        }
    }

    private static void DrawScriptField(EntityBehaviour script, ScriptFieldMeta meta)
    {
        Type t = meta.Type;
        string label = meta.Label;

        if (t == typeof(float))
        {
            float v = (float)meta.Get(script)!;
            if (EditorGui.DragFloat(label, ref v, meta.HasRange ? meta.Speed : 0.1f, meta.HasRange ? meta.Min : 0f, meta.HasRange ? meta.Max : 0f))
                meta.Set(script, v);
        }
        else if (t == typeof(int))
        {
            float v = (int)meta.Get(script)!;
            float speed = meta.HasRange ? meta.Speed : 1f;
            if (EditorGui.DragFloat(label, ref v, speed, meta.HasRange ? meta.Min : 0f, meta.HasRange ? meta.Max : 0f, "%.0f"))
                meta.Set(script, (int)MathF.Round(v));
        }
        else if (t == typeof(bool))
        {
            bool v = (bool)meta.Get(script)!;
            if (EditorGui.Checkbox(label, ref v))
                meta.Set(script, v);
        }
        else if (t.IsEnum)
        {
            object cur = meta.Get(script)!;
            int idx = Array.IndexOf(meta.EnumValues!, cur);
            if (idx < 0) idx = 0;
            if (EditorGui.Combo(label, ref idx, meta.EnumNames!))
                meta.Set(script, meta.EnumValues![idx]);
        }
        else if (t == typeof(Vector2))
        {
            var v = (Vector2)meta.Get(script)!;
            if (EditorGui.Vector2Control(label, ref v, meta.HasReset ? meta.Reset : 0f))
                meta.Set(script, v);
        }
        else if (t == typeof(Vector3))
        {
            var v = (Vector3)meta.Get(script)!;
            bool changed = meta.IsColor
                ? EditorGui.Color3(label, ref v)
                : EditorGui.Vector3Control(label, ref v, meta.HasReset ? meta.Reset : 0f);
            if (changed)
                meta.Set(script, v);
        }
        else if (t == typeof(Vector4))
        {
            var v = (Vector4)meta.Get(script)!;
            if (EditorGui.Color4(label, ref v))
                meta.Set(script, v);
        }
        else if (t == typeof(string))
        {
            string v = (string?)meta.Get(script) ?? string.Empty;
            if (EditorGui.InputText(label, ref v))
                meta.Set(script, v);
        }
    }

    private static ScriptFieldMeta[] ScriptFieldsFor(Type type)
    {
        if (_scriptFieldCache.TryGetValue(type, out ScriptFieldMeta[]? cached))
            return cached;

        var metas = new List<ScriptFieldMeta>();

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsInitOnly || field.IsLiteral) continue;
            if (!IsScriptFieldType(field.FieldType)) continue;
            if (field.GetCustomAttribute<HideInInspectorAttribute>() != null) continue;
            metas.Add(BuildScriptFieldMeta(field, field.FieldType, field.Name, o => field.GetValue(o), (o, v) => field.SetValue(o, v)));
        }

        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetMethod is not { IsPublic: true } || prop.SetMethod is not { IsPublic: true }) continue;
            if (!IsScriptFieldType(prop.PropertyType)) continue;
            if (prop.GetCustomAttribute<HideInInspectorAttribute>() != null) continue;
            metas.Add(BuildScriptFieldMeta(prop, prop.PropertyType, prop.Name, o => prop.GetValue(o), (o, v) => prop.SetValue(o, v)));
        }

        ScriptFieldMeta[] arr = metas.ToArray();
        _scriptFieldCache[type] = arr;
        return arr;
    }

    private static ScriptFieldMeta BuildScriptFieldMeta(MemberInfo member, Type type, string name, Func<object, object?> get, Action<object, object?> set)
    {
        var meta = new ScriptFieldMeta
        {
            Type = type,
            Get = get,
            Set = set,
            Label = member.GetCustomAttribute<InspectorLabelAttribute>()?.Label ?? Humanize(name),
            IsColor = member.GetCustomAttribute<InspectorColorAttribute>() != null,
        };

        var range = member.GetCustomAttribute<InspectorRangeAttribute>();
        if (range != null)
        {
            meta.HasRange = true;
            meta.Min = range.Min;
            meta.Max = range.Max;
            meta.Speed = range.Speed;
        }

        var reset = member.GetCustomAttribute<InspectorResetAttribute>();
        if (reset != null)
        {
            meta.HasReset = true;
            meta.Reset = reset.Value;
        }

        if (type.IsEnum)
        {
            meta.EnumNames = Enum.GetNames(type);
            meta.EnumValues = Enum.GetValues(type).Cast<object>().ToArray();
        }

        return meta;
    }

    private static bool IsScriptFieldType(Type t) =>
        t == typeof(bool) || t == typeof(int) || t == typeof(float) || t == typeof(string) ||
        t.IsEnum || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4);
}
