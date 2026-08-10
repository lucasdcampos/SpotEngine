using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Spot.Assets;
using Spot.Audio;
using Spot.Core;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// Reflection-driven conversion of components to and from JSON, so the scene serializer needs no
/// per-component code. A component opts in with <see cref="SceneComponentAttribute"/>; its public
/// read/write properties of supported types are then written and read automatically.
/// </summary>
/// <remarks>
/// A property is serialized when it is inspector-visible (public getter and setter, a supported type,
/// not <see cref="HideInInspectorAttribute"/>) or it is the path sibling of an
/// <see cref="AssetReferenceAttribute"/> (e.g. <c>ModelPath</c>). Asset paths are made relative on
/// write; <see cref="Texture2D"/> references are eager-loaded on read so sprites render immediately,
/// while models and materials stay lazily resolved at draw time. Supported value types map to JSON as:
/// vectors → number arrays, enums → their integer value, and primitives/strings directly — matching
/// the format the hand-written serializer used, so existing scenes load unchanged.
/// </remarks>
internal static class ComponentSerialization
{
    private sealed class Member
    {
        public Member(PropertyInfo prop, bool isAssetPath)
        {
            Prop = prop;
            IsAssetPath = isAssetPath;
        }

        public PropertyInfo Prop { get; }
        public bool IsAssetPath { get; }
    }

    private sealed record AssetRef(PropertyInfo Asset, PropertyInfo Path);

    private static Dictionary<string, Type>? s_keyToType;
    private static List<(Type Type, string Key)>? s_writeOrder;
    private static readonly Dictionary<Type, Member[]> s_memberCache = new();
    private static readonly Dictionary<Type, AssetRef[]> s_assetRefCache = new();

    /// <summary>
    /// The registered component types paired with their on-disk keys, ordered for deterministic output
    /// (by inspector order, then key). Used by the serializer to walk an entity's components on save.
    /// </summary>
    public static IReadOnlyList<(Type Type, string Key)> WriteOrder
    {
        get
        {
            EnsureRegistry();
            return s_writeOrder!;
        }
    }

    /// <summary>Resolves an on-disk component key (including legacy aliases) to its component type.</summary>
    public static bool TryResolveKey(string key, [NotNullWhen(true)] out Type? type)
    {
        EnsureRegistry();
        return s_keyToType!.TryGetValue(key, out type);
    }

    /// <summary>Writes a component's serializable properties into a new JSON object.</summary>
    public static JsonObject Serialize(object component)
    {
        var obj = new JsonObject();
        foreach (Member member in MembersFor(component.GetType()))
        {
            object? value = member.Prop.GetValue(component);
            if (member.IsAssetPath && value is string path && !string.IsNullOrEmpty(path))
            {
                value = AssetPath.MakeRelative(path);
            }

            obj[member.Prop.Name] = ToNode(value);
        }

        return obj;
    }

    /// <summary>
    /// Creates a component of the given type and fills it from the JSON object. Unknown or unconvertible
    /// fields are skipped (the property keeps its default) so a hand-edited or older scene never throws.
    /// </summary>
    public static Component Deserialize(Type type, JsonObject data)
    {
        var component = (Component)Activator.CreateInstance(type)!;

        foreach (Member member in MembersFor(type))
        {
            if (!data.TryGetPropertyValue(member.Prop.Name, out JsonNode? node) || node is null)
            {
                continue;
            }

            try
            {
                object? value = FromNode(node, member.Prop.PropertyType);
                if (value != null)
                {
                    member.Prop.SetValue(component, value);
                }
            }
            catch (Exception ex)
            {
                Log.CoreWarn("Skipping property '{0}.{1}' while loading: {2}", type.Name, member.Prop.Name, ex.Message);
            }
        }

        // Eager-load texture references (sprites) so they render on the first frame, and audio clips so a
        // PlayOnAwake source is ready when the scene starts. Model/material references stay lazy, resolved
        // at draw time.
        foreach (AssetRef assetRef in AssetRefsFor(type))
        {
            if (assetRef.Path.GetValue(component) is not string p || string.IsNullOrEmpty(p)) continue;

            try
            {
                if (assetRef.Asset.PropertyType == typeof(Texture2D))
                {
                    assetRef.Asset.SetValue(component, Texture2D.LoadRef(p));
                }
                else if (assetRef.Asset.PropertyType == typeof(AudioClip))
                {
                    assetRef.Asset.SetValue(component, AudioClip.LoadRef(p));
                }
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to load asset '{0}': {1}", p, ex.Message);
            }
        }

        return component;
    }

    private static void EnsureRegistry()
    {
        if (s_keyToType != null)
        {
            return;
        }

        var keyToType = new Dictionary<string, Type>(StringComparer.Ordinal);
        var order = new List<(Type Type, string Key, int Order)>();

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                // A partially-loadable assembly must never take scene loading down; just skip it.
                continue;
            }

            foreach (Type type in types)
            {
                if (type.IsAbstract || !typeof(Component).IsAssignableFrom(type)) continue;
                var attr = type.GetCustomAttribute<SceneComponentAttribute>();
                if (attr == null) continue;

                keyToType[attr.Key] = type;
                int menuOrder = type.GetCustomAttribute<ComponentMenuAttribute>()?.Order ?? int.MaxValue;
                order.Add((type, attr.Key, menuOrder));
            }
        }

        // Legacy alias: older scenes stored a directional light under "DirectionalLight" (a separate
        // DTO) before lights were unified into LightComponent. Read it as a LightComponent — its Type
        // defaults to Directional, so the remaining fields map straight across.
        if (keyToType.TryGetValue("Light", out Type? lightType))
        {
            keyToType.TryAdd("DirectionalLight", lightType);
        }

        order.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Key, b.Key));
        s_writeOrder = order.Select(o => (o.Type, o.Key)).ToList();
        s_keyToType = keyToType;
    }

    private static Member[] MembersFor(Type type)
    {
        if (s_memberCache.TryGetValue(type, out Member[]? cached))
        {
            return cached;
        }

        var assetPathNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (AssetRef assetRef in AssetRefsFor(type))
        {
            assetPathNames.Add(assetRef.Path.Name);
        }

        var members = new List<Member>();
        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(p => p.MetadataToken))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetMethod is not { IsPublic: true } || prop.SetMethod is not { IsPublic: true }) continue;
            if (!IsSupported(prop.PropertyType)) continue;

            bool isAssetPath = assetPathNames.Contains(prop.Name);

            // Skip inspector-hidden properties (runtime state, computed values) unless they are an asset path
            // or explicitly marked SerializeHidden — hidden data that is authored in code and must persist.
            if (prop.GetCustomAttribute<HideInInspectorAttribute>() != null
                && !isAssetPath
                && prop.GetCustomAttribute<SerializeHiddenAttribute>() == null)
            {
                continue;
            }

            members.Add(new Member(prop, isAssetPath));
        }

        Member[] arr = members.ToArray();
        s_memberCache[type] = arr;
        return arr;
    }

    private static AssetRef[] AssetRefsFor(Type type)
    {
        if (s_assetRefCache.TryGetValue(type, out AssetRef[]? cached))
        {
            return cached;
        }

        var refs = new List<AssetRef>();
        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<AssetReferenceAttribute>();
            if (attr == null) continue;

            PropertyInfo? pathProp = type.GetProperty(attr.PathPropertyName, BindingFlags.Public | BindingFlags.Instance);
            if (pathProp != null)
            {
                refs.Add(new AssetRef(prop, pathProp));
            }
        }

        AssetRef[] arr = refs.ToArray();
        s_assetRefCache[type] = arr;
        return arr;
    }

    // ----- Script members (fields + properties) ----------------------------------------------------

    private sealed class ScriptMember
    {
        public ScriptMember(string name, Type type, Func<object, object?> get, Action<object, object?> set)
        {
            Name = name;
            Type = type;
            Get = get;
            Set = set;
        }

        public string Name { get; }
        public Type Type { get; }
        public Func<object, object?> Get { get; }
        public Action<object, object?> Set { get; }
    }

    private static readonly Dictionary<Type, ScriptMember[]> s_scriptMemberCache = new();

    /// <summary>
    /// Writes an object's public fields and read/write properties of supported types into a JSON object.
    /// Used for script instances (<see cref="EntityBehaviour"/>), which expose tunables as public fields.
    /// </summary>
    public static JsonObject SerializeMembers(object obj)
    {
        var json = new JsonObject();
        foreach (ScriptMember member in ScriptMembersFor(obj.GetType()))
        {
            json[member.Name] = ToNode(member.Get(obj));
        }

        return json;
    }

    /// <summary>Applies previously serialized field/property values from a JSON object onto an instance.</summary>
    public static void ApplyMembers(object obj, JsonObject data)
    {
        foreach (ScriptMember member in ScriptMembersFor(obj.GetType()))
        {
            if (!data.TryGetPropertyValue(member.Name, out JsonNode? node) || node is null)
            {
                continue;
            }

            try
            {
                object? value = FromNode(node, member.Type);
                if (value != null)
                {
                    member.Set(obj, value);
                }
            }
            catch (Exception ex)
            {
                Log.CoreWarn("Skipping script field '{0}.{1}': {2}", obj.GetType().Name, member.Name, ex.Message);
            }
        }
    }

    private static ScriptMember[] ScriptMembersFor(Type type)
    {
        if (s_scriptMemberCache.TryGetValue(type, out ScriptMember[]? cached))
        {
            return cached;
        }

        var members = new List<ScriptMember>();

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsInitOnly || field.IsLiteral) continue;
            if (!IsSupported(field.FieldType)) continue;
            if (field.GetCustomAttribute<HideInInspectorAttribute>() != null) continue;

            members.Add(new ScriptMember(field.Name, field.FieldType, o => field.GetValue(o), (o, v) => field.SetValue(o, v)));
        }

        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetMethod is not { IsPublic: true } || prop.SetMethod is not { IsPublic: true }) continue;
            if (!IsSupported(prop.PropertyType)) continue;
            if (prop.GetCustomAttribute<HideInInspectorAttribute>() != null) continue;

            members.Add(new ScriptMember(prop.Name, prop.PropertyType, o => prop.GetValue(o), (o, v) => prop.SetValue(o, v)));
        }

        ScriptMember[] arr = members.ToArray();
        s_scriptMemberCache[type] = arr;
        return arr;
    }

    private static bool IsSupported(Type t) =>
        t == typeof(bool) || t == typeof(int) || t == typeof(float) || t == typeof(string) ||
        t.IsEnum || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) ||
        t == typeof(string[]);

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        float f => JsonValue.Create(f),
        string s => JsonValue.Create(s),
        Enum e => JsonValue.Create(Convert.ToInt32(e)),
        Vector2 v => new JsonArray(v.X, v.Y),
        Vector3 v => new JsonArray(v.X, v.Y, v.Z),
        Vector4 v => new JsonArray(v.X, v.Y, v.Z, v.W),
        string[] a => ToStringArray(a),
        _ => null,
    };

    private static JsonArray ToStringArray(string[] values)
    {
        var array = new JsonArray();
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static object? FromNode(JsonNode node, Type targetType)
    {
        if (targetType == typeof(bool)) return node.GetValue<bool>();
        if (targetType == typeof(int)) return node.GetValue<int>();
        if (targetType == typeof(float)) return node.GetValue<float>();
        if (targetType == typeof(string)) return node.GetValue<string>();
        if (targetType.IsEnum) return Enum.ToObject(targetType, node.GetValue<int>());

        if (targetType == typeof(string[]))
        {
            JsonArray a = node.AsArray();
            var result = new string[a.Count];
            for (int i = 0; i < a.Count; i++)
            {
                result[i] = a[i]?.GetValue<string>() ?? string.Empty;
            }

            return result;
        }

        if (targetType == typeof(Vector2))
        {
            JsonArray a = node.AsArray();
            return new Vector2(a[0]!.GetValue<float>(), a[1]!.GetValue<float>());
        }

        if (targetType == typeof(Vector3))
        {
            JsonArray a = node.AsArray();
            return new Vector3(a[0]!.GetValue<float>(), a[1]!.GetValue<float>(), a[2]!.GetValue<float>());
        }

        if (targetType == typeof(Vector4))
        {
            JsonArray a = node.AsArray();
            return new Vector4(a[0]!.GetValue<float>(), a[1]!.GetValue<float>(), a[2]!.GetValue<float>(), a[3]!.GetValue<float>());
        }

        return null;
    }
}
