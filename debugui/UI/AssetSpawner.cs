using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.Engine.Assets;
using Spot.Engine.Scenes;
using Spot.Engine;
using Spot.Engine.Audio;
using Spot.Engine.Graphics;

namespace Spot.DebugUI.UI;

/// <summary>What an asset becomes when it is dropped into a scene.</summary>
public enum AssetSpawnKind
{
    /// <summary>The asset does not stand for an entity (a material, a script, a scene, ...).</summary>
    None,

    /// <summary>A <c>.sptprefab</c>: its entity tree, linked back to the prefab.</summary>
    Prefab,

    /// <summary>A model file or built-in mesh: its node hierarchy, with the model's materials applied.</summary>
    Model,

    /// <summary>An image or built-in texture: a <see cref="Sprite2DComponent"/> drawing it.</summary>
    Sprite,

    /// <summary>An audio clip: an <see cref="AudioSourceComponent"/> playing it.</summary>
    AudioSource,

    /// <summary>A <c>.sptui</c> document: a <see cref="UICanvasComponent"/> showing it.</summary>
    UICanvas,
}

/// <summary>
/// Turns an asset into the entity it stands for, so dragging it from the Asset Browser into the scene (the
/// viewport or the Hierarchy) builds the finished object in one step instead of creating an empty entity and
/// adding components by hand: a prefab instantiates, a model imports its hierarchy, an image becomes a sprite,
/// an audio clip an audio source and a UI document a canvas.
/// </summary>
/// <remarks>
/// <see cref="Spawn"/> is plain scene editing with no ImGui involved; <see cref="AcceptDrop"/> is the drag-drop
/// glue the panels share. Per the never-crash rule a broken asset logs and spawns nothing (or an entity whose
/// asset failed to load), never throws.
/// </remarks>
public static class AssetSpawner
{
    private static readonly string[] s_imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };
    private static readonly string[] s_modelExtensions = { ".obj", ".fbx", ".gltf", ".glb", ".dae", ".ply", ".stl" };
    private static readonly string[] s_audioExtensions = { ".wav", ".ogg" };

    /// <summary>The drag-drop payload types (as the Asset Browser produces them) that spawn an entity.</summary>
    public static IReadOnlyList<string> PayloadTypes { get; } =
        new[] { "PREFAB_FILE", "MODEL_FILE", "IMAGE_FILE", "AUDIO_FILE", "UI_FILE" };

    /// <summary>
    /// Every asset in the drag the Asset Browser is running, so dropping a multi-selection spawns all of it. A
    /// drag-drop payload carries only the asset under the cursor; the browser publishes the rest here. Only
    /// trusted while it contains the delivered payload's path, so a stale list can never leak into a later drop.
    /// </summary>
    public static IReadOnlyList<string> DraggedPaths { get; set; } = Array.Empty<string>();

    /// <summary>Classifies what <paramref name="path"/> spawns, by its extension or its built-in kind.</summary>
    /// <param name="path">A source path or a built-in reference (<c>builtin:…</c>).</param>
    public static AssetSpawnKind KindOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return AssetSpawnKind.None;
        }

        if (BuiltinAssets.IsBuiltin(path))
        {
            if (!BuiltinAssets.TryGet(path, out BuiltinAsset builtin)) return AssetSpawnKind.None;
            return builtin.Kind switch
            {
                BuiltinAssetKind.Mesh => AssetSpawnKind.Model,
                BuiltinAssetKind.Texture => AssetSpawnKind.Sprite,
                _ => AssetSpawnKind.None,
            };
        }

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".sptprefab") return AssetSpawnKind.Prefab;
        if (ext == ".sptui") return AssetSpawnKind.UICanvas;
        if (s_modelExtensions.Contains(ext)) return AssetSpawnKind.Model;
        if (s_imageExtensions.Contains(ext)) return AssetSpawnKind.Sprite;
        if (s_audioExtensions.Contains(ext)) return AssetSpawnKind.AudioSource;
        return AssetSpawnKind.None;
    }

    /// <summary>
    /// Creates the entity <paramref name="path"/> stands for in <paramref name="scene"/>, under an optional parent,
    /// and returns its root, or <see langword="null"/> when the asset spawns nothing or could not be read.
    /// </summary>
    /// <param name="scene">The scene to add the entity to.</param>
    /// <param name="path">A source path or a built-in reference.</param>
    /// <param name="parent">The entity to parent the new root under, or <see langword="null"/> for a root entity.</param>
    public static Entity? Spawn(Scene scene, string path, Entity? parent = null)
    {
        try
        {
            return KindOf(path) switch
            {
                AssetSpawnKind.Prefab => SpawnPrefab(scene, path, parent),
                AssetSpawnKind.Model => ModelInstantiator.Instantiate(scene, path, parent),
                AssetSpawnKind.Sprite => SpawnSprite(scene, path, parent),
                AssetSpawnKind.AudioSource => SpawnAudioSource(scene, path, parent),
                AssetSpawnKind.UICanvas => SpawnUICanvas(scene, path, parent),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            Log.Error("Failed to add '{0}' to the scene: {1}", path, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Spawns every asset in <paramref name="paths"/> that stands for an entity and returns the new roots, in order.
    /// </summary>
    /// <param name="scene">The scene to add the entities to.</param>
    /// <param name="paths">Source paths or built-in references; ones that spawn nothing are skipped.</param>
    /// <param name="parent">The entity to parent the new roots under, or <see langword="null"/> for root entities.</param>
    public static List<Entity> SpawnAll(Scene scene, IEnumerable<string> paths, Entity? parent = null)
    {
        var spawned = new List<Entity>();
        foreach (string path in paths)
        {
            if (Spawn(scene, path, parent) is Entity root) spawned.Add(root);
        }
        return spawned;
    }

    /// <summary>
    /// Accepts any spawnable asset payload on the current drag-drop target (call between
    /// <c>ImGui.BeginDragDropTarget</c> and <c>ImGui.EndDragDropTarget</c>).
    /// </summary>
    /// <param name="paths">
    /// The dragged assets that spawn something: the whole Asset Browser selection when it is being dragged,
    /// otherwise the one payload asset.
    /// </param>
    /// <param name="delivered">Whether the mouse was released, i.e. the assets were dropped here this frame.</param>
    /// <param name="preview">
    /// Also report the payload while it merely hovers the target (with <paramref name="delivered"/> false), so the
    /// caller can show where the drop will land.
    /// </param>
    /// <returns>Whether a spawnable payload is over (or was dropped on) the target.</returns>
    public static bool AcceptDrop(out IReadOnlyList<string> paths, out bool delivered, bool preview = false)
    {
        paths = Array.Empty<string>();
        delivered = false;

        ImGuiDragDropFlags flags = preview ? ImGuiDragDropFlags.AcceptBeforeDelivery : ImGuiDragDropFlags.None;
        foreach (string type in PayloadTypes)
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(type, flags);
            unsafe
            {
                if (payload.NativePtr == null) continue;
            }

            string? path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
            if (string.IsNullOrEmpty(path)) return false;

            paths = ResolveDragged(path);
            delivered = payload.IsDelivery();
            return paths.Count > 0;
        }

        return false;
    }

    // The payload's asset plus the rest of the selection dragged with it, keeping only what spawns.
    private static IReadOnlyList<string> ResolveDragged(string payloadPath)
    {
        IReadOnlyList<string> dragged = DraggedPaths;
        if (!dragged.Contains(payloadPath, StringComparer.OrdinalIgnoreCase))
        {
            dragged = new[] { payloadPath };
        }
        return dragged.Where(p => KindOf(p) != AssetSpawnKind.None).ToList();
    }

    /// <summary>A short, user-facing name for what an asset spawns ("Prefab", "Sprite", ...).</summary>
    public static string Describe(AssetSpawnKind kind) => kind switch
    {
        AssetSpawnKind.Prefab => "Prefab",
        AssetSpawnKind.Model => "Model",
        AssetSpawnKind.Sprite => "Sprite",
        AssetSpawnKind.AudioSource => "Audio Source",
        AssetSpawnKind.UICanvas => "UI Canvas",
        _ => "Asset",
    };

    /// <summary>The undo label for adding <paramref name="paths"/>: "Add Sprite 'hero'", or "Add 3 Assets".</summary>
    public static string AddLabel(IEnumerable<string> paths)
    {
        List<string> spawnable = paths.Where(p => KindOf(p) != AssetSpawnKind.None).ToList();
        return spawnable.Count == 1
            ? $"Add {Describe(KindOf(spawnable[0]))} '{NameFor(spawnable[0])}'"
            : $"Add {spawnable.Count} Assets";
    }

    /// <summary>The entity name for an asset: its file name without extension, or a built-in's own name.</summary>
    public static string NameFor(string path) =>
        BuiltinAssets.TryGet(path, out BuiltinAsset builtin) ? builtin.Name : Path.GetFileNameWithoutExtension(path);

    // The prefab's entity tree, marked as an instance of it so the Hierarchy tints it.
    private static Entity? SpawnPrefab(Scene scene, string path, Entity? parent)
    {
        Entity? root = Prefab.InstantiateFile(scene, path, parent);
        root?.AddComponent(new PrefabComponent { PrefabRef = AssetDatabase.ToGuidRef(path) });
        return root;
    }

    private static Entity SpawnSprite(Scene scene, string path, Entity? parent)
    {
        Entity entity = NewEntity(scene, path, parent);

        // Store a stable guid: reference (portable across rename/move) and preview the cooked texture, exactly
        // as assigning the texture in the Inspector does.
        string? reference = AssetDatabase.ToGuidRef(path);
        var sprite = new Sprite2DComponent { TexturePath = reference };
        if (reference != null)
        {
            try
            {
                sprite.Texture = Texture2D.Load(reference);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load texture '{0}': {1}", path, ex.Message);
            }
        }
        entity.AddComponent(sprite);

        // The sprite quad is a unit square: stretch it to the picture's proportions so it isn't distorted.
        if (sprite.Texture is { Width: > 0, Height: > 0 } texture)
        {
            entity.GetComponent<TransformComponent>().Scale = new Vector3((float)texture.Width / texture.Height, 1.0f, 1.0f);
        }

        return entity;
    }

    private static Entity SpawnAudioSource(Scene scene, string path, Entity? parent)
    {
        Entity entity = NewEntity(scene, path, parent);

        string? reference = AssetDatabase.ToGuidRef(path);
        var source = new AudioSourceComponent { ClipPath = reference };
        if (reference != null)
        {
            try
            {
                source.Clip = AudioClip.Load(reference);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load audio clip '{0}': {1}", path, ex.Message);
            }
        }
        entity.AddComponent(source);
        return entity;
    }

    private static Entity SpawnUICanvas(Scene scene, string path, Entity? parent)
    {
        Entity entity = NewEntity(scene, path, parent);
        entity.AddComponent(new UICanvasComponent { DocumentRef = AssetDatabase.ToGuidRef(path) });
        return entity;
    }

    private static Entity NewEntity(Scene scene, string path, Entity? parent)
    {
        Entity entity = scene.Instantiate(NameFor(path));
        entity.SetParent(parent);
        return entity;
    }
}
