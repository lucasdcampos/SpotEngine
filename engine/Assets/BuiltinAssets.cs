using System.Numerics;
using Spot.Framework;
using Spot.Framework.Graphics;

namespace Spot.Engine.Assets;

/// <summary>The kinds of asset the engine ships built in.</summary>
public enum BuiltinAssetKind
{
    /// <summary>A procedural mesh (see <see cref="PrimitiveShape"/>), loaded as a <see cref="Model"/>.</summary>
    Mesh,

    /// <summary>A generated texture.</summary>
    Texture,

    /// <summary>A material.</summary>
    Material,
}

/// <summary>
/// One entry of the <see cref="BuiltinAssets"/> catalog.
/// </summary>
/// <param name="Reference">The canonical reference, such as <c>builtin:Mesh/Capsule</c>.</param>
/// <param name="Kind">What kind of asset it is.</param>
/// <param name="Name">The display name, such as <c>Capsule</c>.</param>
/// <param name="Description">A one-line description for tooltips.</param>
public sealed record BuiltinAsset(string Reference, BuiltinAssetKind Kind, string Name, string Description);

/// <summary>
/// The assets every project has without importing anything: procedural meshes (cube, sphere, capsule, cylinder,
/// cone, plane, quad), utility textures (white, black, flat normal, checker, grid, soft dot) and materials
/// (default, checker, grid). They are generated in code — nothing to cook, ship or fetch, the same on every
/// platform — and referenced like any asset, by a stable <c>builtin:</c> reference such as
/// <c>builtin:Material/Grid</c>.
/// </summary>
/// <remarks>
/// <para>
/// A mesh reference may carry parameters in the <see cref="PrimitiveSpec"/> text form, such as
/// <c>builtin:Mesh/Capsule?radius=0.3&amp;height=1.7</c>. Older references are read as aliases:
/// <c>primitive:Cube</c> is <c>builtin:Mesh/Cube</c> and <c>editor:Checkerboard</c> is
/// <c>builtin:Material/Checker</c>; <see cref="Canonicalize"/> rewrites them.
/// </para>
/// <para>
/// Loaded built-ins are shared and read-only: one instance per reference, rebuilt if the graphics device changes.
/// Don't dispose or edit them — copy one into the project to customize it.
/// </para>
/// </remarks>
public static class BuiltinAssets
{
    /// <summary>The reference prefix of built-in assets.</summary>
    public const string Scheme = "builtin:";

    private const string LegacyPrimitivePrefix = PrimitiveModelFactory.ReferencePrefix;
    private const string LegacyEditorPrefix = "editor:";
    private const string LegacyCheckerboard = "editor:Checkerboard";

    private static readonly Dictionary<string, Texture2D> s_textures = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Material> s_materials = new(StringComparer.Ordinal);
    private static IGraphicsDevice? s_device;

    /// <summary>Gets every built-in asset: meshes, then textures, then materials.</summary>
    public static IReadOnlyList<BuiltinAsset> All { get; } = BuildCatalog();

    /// <summary>Gets the built-in assets of one kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The matching entries, in catalog order.</returns>
    public static IEnumerable<BuiltinAsset> OfKind(BuiltinAssetKind kind) => All.Where(a => a.Kind == kind);

    /// <summary>
    /// Gets whether a reference names a built-in asset, in the current form (<c>builtin:</c>) or a legacy one
    /// (<c>primitive:</c>, <c>editor:</c>). It only checks the prefix; see <see cref="TryGet"/> to look one up.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <returns><see langword="true"/> for a built-in reference.</returns>
    public static bool IsBuiltin(string? reference) =>
        reference is not null
        && (reference.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith(LegacyPrimitivePrefix, StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith(LegacyEditorPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the canonical form of a built-in reference: legacy aliases become <c>builtin:</c> references, kind
    /// and name take the catalog's spelling, and mesh parameters are normalized. Anything else — files, guid
    /// references, unknown built-ins — is returned unchanged.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The canonical reference.</returns>
    public static string? Canonicalize(string? reference)
    {
        if (!IsBuiltin(reference))
        {
            return reference;
        }

        if (TryGetPrimitive(reference, out PrimitiveSpec spec))
        {
            return MeshReference(spec);
        }

        return TryGet(reference, out BuiltinAsset asset) ? asset.Reference : reference;
    }

    /// <summary>Looks up the catalog entry a reference names (a mesh's parameters are ignored).</summary>
    /// <param name="reference">The reference, current or legacy.</param>
    /// <param name="asset">Receives the entry.</param>
    /// <returns><see langword="true"/> when the reference names a built-in asset.</returns>
    public static bool TryGet(string? reference, out BuiltinAsset asset)
    {
        asset = All[0];
        if (!IsBuiltin(reference))
        {
            return false;
        }

        if (string.Equals(reference, LegacyCheckerboard, StringComparison.OrdinalIgnoreCase))
        {
            return TryFind(BuiltinAssetKind.Material, "Checker", out asset);
        }

        if (TryGetPrimitive(reference, out PrimitiveSpec spec))
        {
            return TryFind(BuiltinAssetKind.Mesh, spec.Shape.ToString(), out asset);
        }

        if (!reference!.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string path = reference[Scheme.Length..];
        int query = path.IndexOf('?');
        if (query >= 0)
        {
            path = path[..query];
        }

        int slash = path.IndexOf('/');
        return slash > 0
            && Enum.TryParse(path[..slash], ignoreCase: true, out BuiltinAssetKind kind)
            && Enum.IsDefined(kind)
            && TryFind(kind, path[(slash + 1)..], out asset);
    }

    /// <summary>
    /// Reads the primitive a mesh reference describes — <c>builtin:Mesh/…</c> or the legacy <c>primitive:…</c> —
    /// including its parameters.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <param name="spec">Receives the primitive.</param>
    /// <returns><see langword="true"/> for a known mesh reference.</returns>
    public static bool TryGetPrimitive(string? reference, out PrimitiveSpec spec)
    {
        spec = PrimitiveSpec.For(PrimitiveShape.Cube);
        if (reference is null)
        {
            return false;
        }

        const string meshPrefix = Scheme + "Mesh/";
        if (reference.StartsWith(meshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return PrimitiveSpec.TryParse(reference[meshPrefix.Length..], out spec);
        }

        return PrimitiveModelFactory.TryParseReference(reference, out spec);
    }

    /// <summary>Returns the canonical reference of a primitive mesh, parameters included.</summary>
    /// <param name="spec">The primitive.</param>
    /// <returns>A reference such as <c>builtin:Mesh/Capsule?radius=0.3</c>.</returns>
    public static string MeshReference(PrimitiveSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return Scheme + "Mesh/" + spec.Normalize();
    }

    /// <summary>Loads a built-in mesh as a shared model.</summary>
    /// <param name="reference">The mesh reference, current or legacy, with any parameters.</param>
    /// <returns>The shared model.</returns>
    /// <exception cref="ArgumentException">The reference is not a known built-in mesh.</exception>
    public static Model LoadModel(string reference) =>
        TryGetPrimitive(reference, out PrimitiveSpec spec)
            ? PrimitiveModelFactory.Get(spec)
            : throw new ArgumentException($"Unknown built-in mesh '{reference}'.", nameof(reference));

    /// <summary>Loads a built-in texture, shared.</summary>
    /// <param name="reference">The texture reference.</param>
    /// <returns>The shared texture; do not dispose it.</returns>
    /// <exception cref="ArgumentException">The reference is not a known built-in texture.</exception>
    public static Texture2D LoadTexture(string reference)
    {
        BuiltinAsset asset = Require(reference, BuiltinAssetKind.Texture);
        SyncDevice();

        // A texture someone disposed by mistake (its handle is cleared) is rebuilt rather than handed out dead.
        if (!s_textures.TryGetValue(asset.Reference, out Texture2D? texture) || texture.Handle.Id == 0)
        {
            texture = CreateImage(asset.Reference).ToTexture();
            s_textures[asset.Reference] = texture;
        }

        return texture;
    }

    /// <summary>Loads a built-in material, shared.</summary>
    /// <param name="reference">The material reference, current or legacy.</param>
    /// <returns>The shared material; do not edit it.</returns>
    /// <exception cref="ArgumentException">The reference is not a known built-in material.</exception>
    public static Material LoadMaterial(string reference)
    {
        BuiltinAsset asset = Require(reference, BuiltinAssetKind.Material);
        SyncDevice();
        if (s_materials.TryGetValue(asset.Reference, out Material? material)
            && (material.Texture is null || material.Texture.Handle.Id != 0))
        {
            return material;
        }

        material = CreateMaterial(asset);
        s_materials[asset.Reference] = material;
        return material;
    }

    /// <summary>
    /// Generates the pixels of a built-in texture — for saving a copy into the project, or for previews.
    /// </summary>
    /// <param name="reference">The texture reference.</param>
    /// <returns>A new image the caller owns.</returns>
    /// <exception cref="ArgumentException">The reference is not a known built-in texture.</exception>
    public static Image CreateImage(string reference) => Require(reference, BuiltinAssetKind.Texture).Name switch
    {
        "White" => ProceduralImages.Solid(Vector4.One),
        "Black" => ProceduralImages.Solid(new Vector4(0, 0, 0, 1)),
        "FlatNormal" => ProceduralImages.FlatNormal(),
        "Checker" => ProceduralImages.Checkerboard(),
        "Grid" => ProceduralImages.Grid(),
        "SoftDot" => ProceduralImages.SoftDot(),
        string name => throw new ArgumentException($"Built-in texture '{name}' has no generator.", nameof(reference)),
    };

    /// <summary>
    /// Creates a new, editable material with a built-in material's settings — the starting point for saving a copy
    /// into the project.
    /// </summary>
    /// <param name="reference">The material reference, current or legacy.</param>
    /// <returns>A new material the caller owns; its textures stay built-in references.</returns>
    /// <exception cref="ArgumentException">The reference is not a known built-in material.</exception>
    public static Material CreateMaterial(string reference) => CreateMaterial(Require(reference, BuiltinAssetKind.Material));

    /// <summary>
    /// Saves an editable copy of a built-in asset into a folder — a mesh as a Wavefront <c>.obj</c> with its
    /// parameters baked in, a texture as a <c>.png</c>, a material as an <c>.sptmat</c> (whose textures stay
    /// built-in references). The file is named after the asset, numbered if the name is taken.
    /// </summary>
    /// <param name="reference">The built-in reference, with any mesh parameters.</param>
    /// <param name="directory">The destination folder; created if missing.</param>
    /// <returns>The full path of the new file.</returns>
    /// <exception cref="ArgumentException">The reference is not a known built-in asset.</exception>
    public static string Export(string reference, string directory)
    {
        if (!TryGet(reference, out BuiltinAsset asset))
        {
            throw new ArgumentException($"Unknown built-in asset '{reference}'.", nameof(reference));
        }

        Directory.CreateDirectory(directory);
        switch (asset.Kind)
        {
            case BuiltinAssetKind.Mesh:
            {
                TryGetPrimitive(reference, out PrimitiveSpec spec);
                string path = UniquePath(directory, asset.Name, ".obj");
                File.WriteAllText(path, MeshExport.ToObj(spec.Build(), asset.Name));
                return path;
            }

            case BuiltinAssetKind.Texture:
            {
                string path = UniquePath(directory, asset.Name, ".png");
                CreateImage(asset.Reference).SavePng(path);
                return path;
            }

            default:
            {
                string path = UniquePath(directory, asset.Name, ".sptmat");
                CreateMaterial(asset).Save(path);
                return path;
            }
        }
    }

    // A file path in the folder that doesn't exist yet: the name, then "Name 1", "Name 2", ...
    private static string UniquePath(string directory, string name, string extension)
    {
        string path = Path.GetFullPath(Path.Combine(directory, name + extension));
        for (int i = 1; File.Exists(path); i++)
        {
            path = Path.GetFullPath(Path.Combine(directory, $"{name} {i}{extension}"));
        }

        return path;
    }

    private static Material CreateMaterial(BuiltinAsset asset)
    {
        var material = new Material { SourcePath = asset.Reference };
        switch (asset.Name)
        {
            case "Checker":
                // Tiling 0.25 over the 8-square texture gives half-unit squares, whatever the object's scale.
                material.SetTexture(Scheme + "Texture/Checker");
                material.AutoTile = true;
                material.Tiling = new Vector2(0.25f, 0.25f);
                break;
            case "Grid":
                // Tiling 0.125 over the 8-cell texture gives one-unit cells, with a heavier line every 8 units.
                material.SetTexture(Scheme + "Texture/Grid");
                material.AutoTile = true;
                material.Tiling = new Vector2(0.125f, 0.125f);
                break;
        }

        return material;
    }

    private static BuiltinAsset Require(string reference, BuiltinAssetKind kind) =>
        TryGet(reference, out BuiltinAsset asset) && asset.Kind == kind
            ? asset
            : throw new ArgumentException($"Unknown built-in {kind.ToString().ToLowerInvariant()} '{reference}'.", nameof(reference));

    private static bool TryFind(BuiltinAssetKind kind, string name, out BuiltinAsset asset)
    {
        foreach (BuiltinAsset candidate in All)
        {
            if (candidate.Kind == kind && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                asset = candidate;
                return true;
            }
        }

        asset = All[0];
        return false;
    }

    // Shared GPU resources belong to one device; a new device (a test, a restarted renderer) rebuilds them.
    private static void SyncDevice()
    {
        IGraphicsDevice device = Renderer.Device;
        if (ReferenceEquals(s_device, device))
        {
            return;
        }

        s_textures.Clear();
        s_materials.Clear();
        s_device = device;
    }

    private static IReadOnlyList<BuiltinAsset> BuildCatalog()
    {
        var catalog = new List<BuiltinAsset>
        {
            Mesh(PrimitiveShape.Cube, "A 1-unit cube."),
            Mesh(PrimitiveShape.Sphere, "A sphere of radius 0.5."),
            Mesh(PrimitiveShape.Capsule, "A capsule 2 units tall, radius 0.5."),
            Mesh(PrimitiveShape.Cylinder, "A capped cylinder 2 units tall, radius 0.5."),
            Mesh(PrimitiveShape.Cone, "A cone 1 unit tall, base radius 0.5."),
            Mesh(PrimitiveShape.Plane, "A flat 10 × 10 ground plane facing up."),
            Mesh(PrimitiveShape.Quad, "A flat 1 × 1 card facing +Z."),
            Texture("White", "Plain white."),
            Texture("Black", "Plain black."),
            Texture("FlatNormal", "A normal map that leaves the surface flat."),
            Texture("Checker", "A soft gray checkerboard."),
            Texture("Grid", "A prototyping grid: 8 cells per tile."),
            Texture("SoftDot", "A white dot with a soft edge, for particles and billboards."),
            new(Scheme + "Material/Default", BuiltinAssetKind.Material, "Default", "Plain white, lit."),
            new(Scheme + "Material/Checker", BuiltinAssetKind.Material, "Checker", "The checkerboard, tiled by world size (half-unit squares)."),
            new(Scheme + "Material/Grid", BuiltinAssetKind.Material, "Grid", "The prototyping grid, tiled by world size (one-unit cells)."),
        };
        return catalog;

        static BuiltinAsset Mesh(PrimitiveShape shape, string description) =>
            new(Scheme + "Mesh/" + shape, BuiltinAssetKind.Mesh, shape.ToString(), description);

        static BuiltinAsset Texture(string name, string description) =>
            new(Scheme + "Texture/" + name, BuiltinAssetKind.Texture, name, description);
    }
}
