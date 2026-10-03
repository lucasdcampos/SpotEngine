using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using ImGuiNET;
using Spot.Engine.Animation;
using Spot.Engine.Assets;
using Spot.Engine.Physics;
using Spot.Engine.Scenes;
using Spot.Framework;
using Spot.Framework.Audio;
using Spot.Framework.Graphics;
using Spot.DebugUI.Undo;

namespace Spot.DebugUI.UI;

/// <summary>
/// Draws component inspectors automatically from component metadata. Each component "teaches" the
/// editor how to draw it through the attributes declared in the engine (<see cref="ComponentMenuAttribute"/>,
/// <see cref="InspectorRangeAttribute"/>, <see cref="InspectorColorAttribute"/>, <see cref="ShowIfAttribute"/>,
/// <see cref="AssetReferenceAttribute"/>, ...); this class reflects over a component's public properties and
/// renders each with the matching <see cref="EditorGui"/> widget, so the inspector needs no per-component code.
/// Property types that need bespoke UI (asset slots) and whole components that do (a UI canvas) register a
/// custom drawer here. User components are drawn from their public fields and properties.
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

    /// <summary>
    /// Every user component type the editor can attach: the game's components known to the script registry
    /// plus any concrete user <see cref="Component"/> in a loaded assembly, sorted by name. Each must have a
    /// public parameterless constructor so the inspector can create it.
    /// </summary>
    public static List<Type> UserComponentTypes()
    {
        var types = new HashSet<Type>();
        foreach (ScriptDescriptor descriptor in ScriptRegistry.All)
        {
            if (typeof(Component).IsAssignableFrom(descriptor.Type))
                types.Add(descriptor.Type);
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == typeof(Component).Assembly)
                continue;

            Type[] candidates;
            try
            {
                candidates = assembly.GetTypes();
            }
            catch
            {
                continue; // A partially-loadable assembly must never take the editor down.
            }

            foreach (Type type in candidates)
            {
                if (!type.IsAbstract && !type.IsGenericTypeDefinition && type.IsSubclassOf(typeof(Component))
                    && type.GetConstructor(Type.EmptyTypes) is not null)
                {
                    types.Add(type);
                }
            }
        }

        return types.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Forgets the per-type reflection caches. Called when the editor reloads the game's scripts, so the
    /// unloaded types are neither drawn from stale metadata nor kept alive by it.
    /// </summary>
    public static void ClearTypeCaches()
    {
        _metaCache.Clear();
        _scriptFieldCache.Clear();
    }

    // ----- Public entry points ---------------------------------------------------------------------

    /// <summary>
    /// Draws a user component as its own card: the class name as the title, an Enabled toggle, and its public
    /// fields and read/write properties as editors (firing <see cref="Component.OnValidate"/> on change).
    /// </summary>
    public static void DrawUserComponent(Entity entity, Component component)
    {
        Type type = component.GetType();
        EditorGui.Component(entity, type, Humanize(type.Name), removable: true, () =>
        {
            bool enabled = component.Enabled;
            if (EditorGui.Checkbox("Enabled", ref enabled))
                component.Enabled = enabled;

            DrawScriptFields(component, entity.Scene);
        });
    }

    /// <summary>
    /// Draws one card per user component that could not be resolved: a component whose script is waiting to
    /// compile reads as pending, one with no script at all as missing. Their data is kept either way; removing
    /// the card discards it.
    /// </summary>
    public static void DrawMissingComponents(Entity entity, MissingComponents missing)
    {
        MissingComponent? toRemove = null;
        for (int i = 0; i < missing.Items.Count; i++)
        {
            MissingComponent item = missing.Items[i];
            string name = string.IsNullOrEmpty(item.TypeName) ? "Unknown Component" : item.TypeName;
            bool pending = !string.IsNullOrEmpty(item.TypeName) && EditorGui.ScriptExists(item.TypeName);
            EditorGui.Component(entity, typeof(MissingComponents), Humanize(name), removable: true, () =>
            {
                if (pending)
                {
                    ImGui.TextDisabled($"{EditorIcons.Code}  Waiting for {name}.cs to compile...");
                    ImGui.TextDisabled("It attaches automatically once the scripts reload (Ctrl+R).");
                }
                else
                {
                    ImGui.TextColored(ScriptMissingColor, $"{EditorIcons.Warning}  Script '{name}' was not found.");
                    ImGui.TextWrapped("It may have been renamed or deleted, or the scripts failed to build. Its values are kept and saved with the scene.");
                }
            }, id: $"missing{i}", onRemove: () => toRemove = item);
        }

        if (toRemove is not null)
        {
            missing.Items.Remove(toRemove);
            if (missing.Items.Count == 0)
                entity.RemoveComponent<MissingComponents>();
        }
    }

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

            if (meta.Header != null)
            {
                ImGui.Spacing();
                ImGui.TextDisabled(meta.Header);
                ImGui.Separator();
            }

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

        if (component is Collider3DComponent collider)
            DrawFitToMesh(entity, collider);
    }

    // A 3D collider can size itself to the mesh its entity draws.
    private static void DrawFitToMesh(Entity entity, Collider3DComponent collider)
    {
        if (collider is not (BoxCollider3DComponent or SphereCollider3DComponent or CapsuleCollider3DComponent))
            return;

        bool hasMesh = entity.TryGetComponent(out MeshComponent? mesh) && mesh is not null;
        ImGui.Spacing();
        ImGui.BeginDisabled(!hasMesh);
        if (ImGui.Button("Fit to Mesh", new Vector2(-1.0f, 0.0f)) && hasMesh && !ColliderFitting.FitToMesh(collider, mesh!))
            Log.Warn("The mesh on '{0}' has no geometry to fit to yet.", entity.Name);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(hasMesh
                ? "Sizes and centers this collider on the entity's mesh (exactly, for a built-in shape)."
                : "Add a Mesh Renderer to this entity to fit the collider to it.");
        }
    }

    // ----- Property metadata cache -----------------------------------------------------------------

    private sealed class PropertyMeta(PropertyInfo prop)
    {
        public PropertyInfo Prop { get; } = prop;
        public string Label { get; set; } = prop.Name;
        public string? Header { get; set; }
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

        // A static member (property or parameterless method) yielding the dropdown options for a string
        // property tagged with [InspectorOptions]; resolved once and queried each frame, since the option set
        // can change while the editor runs (mixer buses, for instance).
        public MemberInfo? OptionsMember { get; set; }
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
            meta.Header = prop.GetCustomAttribute<InspectorHeaderAttribute>()?.Header;

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

            var options = prop.GetCustomAttribute<InspectorOptionsAttribute>();
            if (options != null && prop.PropertyType == typeof(string))
            {
                const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                meta.OptionsMember = (MemberInfo?)type.GetProperty(options.OptionsMemberName, staticFlags)
                    ?? type.GetMethod(options.OptionsMemberName, staticFlags, Type.EmptyTypes);
            }

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

        // Each branch keeps the pre-widget value and reports whether the widget changed it, so the
        // tracker can collapse a whole interaction (a drag, a typing session, a color-picker visit) into
        // one named undo entry. See UndoTracker for why the boundary is taken from the change flag
        // rather than from ImGui's per-item activate/deactivate state.
        if (pt == typeof(float))
        {
            float v = (float)prop.GetValue(component)!;
            float before = v;
            float speed = meta.HasRange ? meta.Speed : 0.1f;
            float min = meta.HasRange ? meta.Min : 0.0f;
            float max = meta.HasRange ? meta.Max : 0.0f;
            bool changed = EditorGui.DragFloat(label, ref v, speed, min, max);
            if (changed)
                prop.SetValue(component, v);
            TrackEdit(entity, component, meta, before, changed);
        }
        else if (pt == typeof(bool))
        {
            bool v = (bool)prop.GetValue(component)!;
            bool before = v;
            bool changed = EditorGui.Checkbox(label, ref v);
            if (changed)
                prop.SetValue(component, v);
            TrackEdit(entity, component, meta, before, changed);
        }
        else if (pt.IsEnum)
        {
            object cur = prop.GetValue(component)!;
            object before = cur;
            int idx = Array.IndexOf(meta.EnumValues!, cur);
            if (idx < 0) idx = 0;
            bool changed = EditorGui.Combo(label, ref idx, meta.EnumNames!);
            if (changed)
                prop.SetValue(component, meta.EnumValues![idx]);
            TrackEdit(entity, component, meta, before, changed);
        }
        else if (pt == typeof(Vector2))
        {
            var v = (Vector2)prop.GetValue(component)!;
            Vector2 before = v;
            bool changed = EditorGui.Vector2Control(label, ref v, meta.HasReset ? meta.Reset : 0.0f);
            if (changed)
                prop.SetValue(component, v);
            TrackEdit(entity, component, meta, before, changed);
        }
        else if (pt == typeof(Vector3))
        {
            var v = (Vector3)prop.GetValue(component)!;
            Vector3 before = v;
            bool changed = meta.IsColor
                ? EditorGui.Color3(label, ref v)
                : EditorGui.Vector3Control(label, ref v, meta.HasReset ? meta.Reset : 0.0f);
            if (changed)
                prop.SetValue(component, v);
            TrackEdit(entity, component, meta, before, changed);
        }
        else if (pt == typeof(Vector4))
        {
            // Every Vector4 the inspector shows is a color; there is no plain 4-axis control.
            var v = (Vector4)prop.GetValue(component)!;
            Vector4 before = v;
            bool changed = EditorGui.Color4(label, ref v);
            if (changed)
                prop.SetValue(component, v);
            TrackEdit(entity, component, meta, before, changed);
        }
        else if (pt == typeof(string))
        {
            string v = (string?)prop.GetValue(component) ?? string.Empty;
            string before = v;
            bool changed = meta.OptionsMember != null
                ? DrawOptionsCombo(label, meta, ref v)
                : EditorGui.InputText(label, ref v);
            if (changed)
                prop.SetValue(component, v);
            TrackEdit(entity, component, meta, before, changed);
        }
        // Unknown/unsupported types are silently skipped.
    }

    /// <summary>
    /// Hands one inspector field edit to the undo tracker. The resulting action targets the entity by
    /// its stable id and re-resolves the component when applied, so it stays valid across everything
    /// that replaces component instances wholesale — undoing a delete, reloading scripts, leaving play
    /// mode.
    /// </summary>
    private static void TrackEdit<T>(
        Entity entity, object component, PropertyMeta meta, T before, bool changed)
    {
        // Cheap exit on the overwhelmingly common frame: nothing changed and no edit is in flight.
        if ((!changed && !UndoTracker.HasPending) || !entity.IsValid)
        {
            return;
        }

        PropertyInfo prop = meta.Prop;
        var accessor = MemberAccessor.FromProperty(prop);
        Type componentType = component.GetType();
        Scene scene = entity.Scene;
        string entityId = entity.EnsurePersistentId();
        object? document = EditorHistory.DocumentFor(scene);
        string label = $"Set {meta.Label}";

        UndoTracker.Track(
            UndoKey.For(component, prop.Name),
            label,
            before,
            () => (T)accessor.Get(component)!,
            (b, a) => new ComponentValueAction(
                label, scene, entityId, componentType, accessor, b, a, document),
            changed);
    }

    /// <summary>
    /// Draws a string property as a dropdown over the options its [InspectorOptions] member supplies. A current
    /// value that is not among them is appended and marked, so a stale reference (a renamed audio bus, say) is
    /// visible and preserved rather than quietly replaced by whatever happens to be first in the list.
    /// </summary>
    private static bool DrawOptionsCombo(string label, PropertyMeta meta, ref string value)
    {
        string[] options = ResolveOptions(meta.OptionsMember);
        string current = value;
        int index = Array.FindIndex(options, o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            var withCurrent = new string[options.Length + 1];
            options.CopyTo(withCurrent, 0);
            withCurrent[^1] = string.IsNullOrEmpty(current) ? "(none)" : $"{current}  (missing)";
            options = withCurrent;
            index = options.Length - 1;
        }

        int picked = index;
        if (!EditorGui.Combo(label, ref picked, options) || picked == index || picked >= options.Length)
            return false;

        value = options[picked];
        return true;
    }

    /// <summary>Reads the option list from a static property or method, tolerating a member that misbehaves.</summary>
    private static string[] ResolveOptions(MemberInfo? member)
    {
        try
        {
            object? raw = member switch
            {
                PropertyInfo p => p.GetValue(null),
                MethodInfo m => m.Invoke(null, null),
                _ => null,
            };
            return raw is IEnumerable<string> values ? values.ToArray() : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Log.Warn("Inspector option list failed to resolve: {0}", ex.Message);
            return Array.Empty<string>();
        }
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
        [typeof(AudioClip)] = DrawAudioClipSlot,
        [typeof(AnimatorController)] = DrawControllerSlot,
    };

    private static readonly Dictionary<Type, Action<Entity, object>> _componentDrawers = new()
    {
        [typeof(UICanvasComponent)] = DrawUICanvasComponent,
    };

    private static readonly string[] UIDocumentPatterns = { "*.sptui" };

    private static void DrawUICanvasComponent(Entity entity, object component)
    {
        var canvas = (UICanvasComponent)component;

        bool enabled = canvas.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled))
            canvas.Enabled = enabled;

        string? display = AssetDatabase.ToDisplayPath(canvas.DocumentRef);
        if (EditorGui.AssetSlot("Document", "UI_FILE", UIDocumentPatterns, display, out string? newPath))
            canvas.DocumentRef = AssetDatabase.ToGuidRef(newPath);

        if (!string.IsNullOrEmpty(canvas.DocumentRef))
        {
            if (ImGui.Button("Edit UI", new Vector2(-1.0f, 0.0f)))
            {
                string? source = AssetDatabase.ToDisplayPath(canvas.DocumentRef);
                if (!string.IsNullOrEmpty(source))
                    WidgetInspector.OpenDocumentRequested?.Invoke(source);
            }
        }
    }

    private static void DrawTextureSlot(Entity entity, object component, PropertyMeta meta)
    {
        var texture = (Texture2D?)meta.Prop.GetValue(component);
        string? stored = (string?)meta.AssetPathProp!.GetValue(component);
        string? display = AssetDatabase.ToDisplayPath(stored);

        string[] patterns = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp" };
        if (EditorGui.AssetSlot(meta.Label, "IMAGE_FILE", patterns, display, out string? newPath, builtins: BuiltinAssetKind.Texture))
        {
            try
            {
                // Store a stable guid: reference (portable across rename/move) and preview the cooked texture,
                // so what the editor shows matches what a build ships.
                string? storedRef = AssetDatabase.ToGuidRef(newPath);
                var newTexture = storedRef != null ? Texture2D.Load(storedRef) : null;

                // Built-in textures are shared by everything that uses them, so only a texture of our own is freed.
                if (!BuiltinAssets.IsBuiltin(stored))
                    texture?.Dispose();
                meta.Prop.SetValue(component, newTexture);
                meta.AssetPathProp!.SetValue(component, storedRef);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load texture: {0}", ex.Message);
            }
        }
    }

    private static void DrawAudioClipSlot(Entity entity, object component, PropertyMeta meta)
    {
        var clip = (AudioClip?)meta.Prop.GetValue(component);
        string? stored = (string?)meta.AssetPathProp!.GetValue(component);
        string? display = AssetDatabase.ToDisplayPath(stored);

        string[] patterns = { "*.wav", "*.ogg" };
        if (EditorGui.AssetSlot(meta.Label, "AUDIO_FILE", patterns, display, out string? newPath))
        {
            try
            {
                // Store a stable guid: reference, portable across rename/move, matching the texture path.
                string? storedRef = AssetDatabase.ToGuidRef(newPath);
                var newClip = storedRef != null ? AudioClip.Load(storedRef) : null;
                clip?.Dispose();
                meta.Prop.SetValue(component, newClip);
                meta.AssetPathProp!.SetValue(component, storedRef);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load audio clip: {0}", ex.Message);
            }
        }
    }

    private static void DrawModelSlot(Entity entity, object component, PropertyMeta meta)
    {
        var model = (Model?)meta.Prop.GetValue(component);
        string? stored = (string?)meta.AssetPathProp!.GetValue(component);
        string? display = AssetDatabase.ToDisplayPath(stored);

        string[] patterns = { "*.obj", "*.fbx", "*.gltf", "*.glb", "*.dae" };

        if (EditorGui.AssetSlot(meta.Label, "MODEL_FILE", patterns, display, out string? newPath, builtins: BuiltinAssetKind.Mesh))
        {
            try
            {
                string? storedRef = AssetDatabase.ToGuidRef(newPath);
                var newModel = storedRef != null ? Model.Load(storedRef) : null;
                meta.Prop.SetValue(component, newModel);
                meta.AssetPathProp!.SetValue(component, storedRef);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load model: {0}", ex.Message);
            }
        }

        if (component is MeshComponent mesh)
            DrawPrimitiveParameters(entity, mesh);
    }

    // ----- Built-in shape parameters ----------------------------------------------------------------

    // The in-progress edit of a built-in mesh's parameters. While a field is dragged, the renderer shows a
    // throwaway model of each intermediate shape (so the shared primitive cache only ever holds the shapes that
    // were kept); when the field is released the reference is committed and recorded as one undo step.
    private static MeshComponent? s_shapeMesh;
    private static PrimitiveSpec? s_shapeSpec;
    private static Model? s_shapePreview;
    private static string? s_shapeBefore;
    private static Entity s_shapeEntity;
    private static string s_shapeLabel = string.Empty;

    // Undo/redo writes a mesh renderer's model reference and reloads the model to match.
    private static readonly MemberAccessor MeshReferenceAccessor = MemberAccessor.FromDelegates(
        "ModelPath", typeof(string), "MeshComponent.SetModel",
        target => ((MeshComponent)target).ModelPath,
        (target, value) => ((MeshComponent)target).SetModel((string?)value));

    private static void DrawPrimitiveParameters(Entity entity, MeshComponent mesh)
    {
        // An edit left open on another renderer (selection moved mid-drag) is committed first.
        if (s_shapeMesh != null && !ReferenceEquals(s_shapeMesh, mesh))
            CommitShapeEdit();

        PrimitiveSpec? spec = ReferenceEquals(s_shapeMesh, mesh) ? s_shapeSpec : null;
        if (spec is null && !BuiltinAssets.TryGetPrimitive(mesh.ModelPath, out spec))
            return;

        ImGui.Spacing();
        ImGui.TextDisabled($"{spec.Shape} Shape");
        ImGui.Separator();

        PrimitiveSpec edited = spec;
        string? changedLabel = null;
        foreach (PrimitiveParameter parameter in PrimitiveSpec.ParametersOf(spec.Shape))
        {
            string label = parameter.ToString();
            float value = spec.Get(parameter);
            bool changed = PrimitiveSpec.IsCount(parameter)
                ? EditorGui.DragFloat(label, ref value, 0.2f, 1.0f, 256.0f, "%.0f")
                : EditorGui.DragFloat(label, ref value, 0.01f, 0.001f, 10000.0f, "%.3f");
            if (changed)
            {
                edited = edited.With(parameter, value);
                changedLabel = label;
            }
        }

        if (ImGui.Button("Reset Shape", new Vector2(-1.0f, 0.0f)))
        {
            edited = PrimitiveSpec.For(spec.Shape);
            changedLabel = "Shape";
        }

        if (changedLabel != null && edited != spec)
            PreviewShapeEdit(entity, mesh, edited, $"Set {spec.Shape} {changedLabel}");

        // Released (or confirmed by typing): keep the shape.
        if (ReferenceEquals(s_shapeMesh, mesh) && !ImGui.IsAnyItemActive())
            CommitShapeEdit();
    }

    private static void PreviewShapeEdit(Entity entity, MeshComponent mesh, PrimitiveSpec spec, string label)
    {
        if (!ReferenceEquals(s_shapeMesh, mesh))
        {
            s_shapeMesh = mesh;
            s_shapeEntity = entity;
            s_shapeBefore = mesh.ModelPath;
        }

        s_shapeSpec = spec;
        s_shapeLabel = label;
        try
        {
            Model preview = PrimitiveModelFactory.Create(spec);
            mesh.Model = preview;
            DisposePreview();
            s_shapePreview = preview;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to build the {0} preview: {1}", spec.Shape, ex.Message);
        }
    }

    private static void CommitShapeEdit()
    {
        MeshComponent? mesh = s_shapeMesh;
        PrimitiveSpec? spec = s_shapeSpec;
        string? before = s_shapeBefore;
        Entity entity = s_shapeEntity;
        string label = s_shapeLabel;
        s_shapeMesh = null;
        s_shapeSpec = null;
        s_shapeBefore = null;
        if (mesh is null || spec is null)
        {
            DisposePreview();
            return;
        }

        mesh.SetModel(BuiltinAssets.MeshReference(spec));
        DisposePreview();
        if (!entity.IsValid || before == mesh.ModelPath)
            return;

        Scene scene = entity.Scene;
        string entityId = entity.EnsurePersistentId();
        object? document = EditorHistory.DocumentFor(scene);
        UndoTracker.Track(
            UndoKey.For(mesh, "Shape"),
            label,
            before,
            () => mesh.ModelPath,
            (b, a) => new ComponentValueAction(label, scene, entityId, typeof(MeshComponent), MeshReferenceAccessor, b, a, document),
            changed: true);
    }

    private static void DisposePreview()
    {
        if (s_shapePreview is null)
            return;

        foreach (Mesh part in s_shapePreview.Meshes)
            part.Dispose();
        s_shapePreview = null;
    }

    private static void DrawControllerSlot(Entity entity, object component, PropertyMeta meta)
    {
        string? stored = (string?)meta.AssetPathProp!.GetValue(component);
        string? display = AssetDatabase.ToDisplayPath(stored);

        string[] patterns = { "*.sptcontroller" };
        if (EditorGui.AssetSlot(meta.Label, "CONTROLLER_FILE", patterns, display, out string? newPath))
        {
            try
            {
                // Store a portable guid reference, matching the other asset slots.
                string? storedRef = AssetDatabase.ToGuidRef(newPath);
                var newController = storedRef != null ? AnimatorController.Load(storedRef) : null;
                meta.Prop.SetValue(component, newController);
                meta.AssetPathProp!.SetValue(component, storedRef);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load animator controller '{0}': {1}", newPath, ex.Message);
            }
        }
    }

    private static void DrawMaterialSlot(Entity entity, object component, PropertyMeta meta)
    {
        var material = (Material?)meta.Prop.GetValue(component);
        string? stored = (string?)meta.AssetPathProp!.GetValue(component);
        string? display = AssetDatabase.ToDisplayPath(stored);

        string[] patterns = { "*.sptmat" };

        if (EditorGui.AssetSlot(meta.Label, "MATERIAL_FILE", patterns, display, out string? newPath, builtins: BuiltinAssetKind.Material))
        {
            try
            {
                string? storedRef = AssetDatabase.ToGuidRef(newPath);
                var newMaterial = storedRef != null ? Material.Load(storedRef) : null;
                meta.Prop.SetValue(component, newMaterial);
                meta.AssetPathProp!.SetValue(component, storedRef);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load material '{0}': {1}", newPath, ex.Message);
            }
        }
    }

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
    private static void DrawScriptFields(object script, Scene scene)
    {
        foreach (ScriptFieldMeta meta in ScriptFieldsFor(script.GetType()))
        {
            ImGui.PushID(meta.Label);
            try
            {
                // Editing a serialized field mirrors Unity's OnValidate: notify the script so it can clamp or
                // react. Guarded in the engine so a throwing handler is quarantined, never crashing the editor.
                if (DrawScriptField(script, scene, meta))
                {
                    if (script is Component component)
                        ComponentSystem.InvokeValidate(component);
                }
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

    // Draws one script field; returns true when the user changed it (so the caller can fire OnValidate).
    private static bool DrawScriptField(object script, Scene scene, ScriptFieldMeta meta)
    {
        Type t = meta.Type;
        string label = meta.Label;

        if (t == typeof(float))
        {
            float v = (float)meta.Get(script)!;
            if (EditorGui.DragFloat(label, ref v, meta.HasRange ? meta.Speed : 0.1f, meta.HasRange ? meta.Min : 0f, meta.HasRange ? meta.Max : 0f))
            {
                meta.Set(script, v);
                return true;
            }
        }
        else if (t == typeof(int))
        {
            float v = (int)meta.Get(script)!;
            float speed = meta.HasRange ? meta.Speed : 1f;
            if (EditorGui.DragFloat(label, ref v, speed, meta.HasRange ? meta.Min : 0f, meta.HasRange ? meta.Max : 0f, "%.0f"))
            {
                meta.Set(script, (int)MathF.Round(v));
                return true;
            }
        }
        else if (t == typeof(bool))
        {
            bool v = (bool)meta.Get(script)!;
            if (EditorGui.Checkbox(label, ref v))
            {
                meta.Set(script, v);
                return true;
            }
        }
        else if (t.IsEnum)
        {
            object cur = meta.Get(script)!;
            int idx = Array.IndexOf(meta.EnumValues!, cur);
            if (idx < 0) idx = 0;
            if (EditorGui.Combo(label, ref idx, meta.EnumNames!))
            {
                meta.Set(script, meta.EnumValues![idx]);
                return true;
            }
        }
        else if (t == typeof(Vector2))
        {
            var v = (Vector2)meta.Get(script)!;
            if (EditorGui.Vector2Control(label, ref v, meta.HasReset ? meta.Reset : 0f))
            {
                meta.Set(script, v);
                return true;
            }
        }
        else if (t == typeof(Vector3))
        {
            var v = (Vector3)meta.Get(script)!;
            bool changed = meta.IsColor
                ? EditorGui.Color3(label, ref v)
                : EditorGui.Vector3Control(label, ref v, meta.HasReset ? meta.Reset : 0f);
            if (changed)
            {
                meta.Set(script, v);
                return true;
            }
        }
        else if (t == typeof(Vector4))
        {
            var v = (Vector4)meta.Get(script)!;
            if (EditorGui.Color4(label, ref v))
            {
                meta.Set(script, v);
                return true;
            }
        }
        else if (t == typeof(string))
        {
            string v = (string?)meta.Get(script) ?? string.Empty;
            if (EditorGui.InputText(label, ref v))
            {
                meta.Set(script, v);
                return true;
            }
        }
        else if (t == typeof(Entity))
        {
            var v = (Entity)meta.Get(script)!;
            if (EditorGui.EntityField(label, scene, ref v))
            {
                meta.Set(script, v);
                return true;
            }
        }

        return false;
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
            if (prop.DeclaringType == typeof(Component)) continue; // Enabled is drawn separately.
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
        t.IsEnum || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) ||
        t == typeof(Entity);
}
