using System.Numerics;
using System.Text.Json;
using Spot.Core;
using Spot.Rendering;

namespace Spot.Assets;

public enum MaterialShaderType
{
    Standard = 0,
    Water = 1
}


/// <summary>
/// A simple surface material: a base color and an optional texture. Materials are assets on disk
/// (".sptmat" JSON) that can be shared by many models. When a texture is set the surface samples it
/// and multiplies by <see cref="Color"/>; with no texture the surface is a solid <see cref="Color"/>.
/// </summary>
/// <remarks>
/// This is the high-level surface asset. <see cref="Load"/> caches by path, so a material edited in
/// the editor is the same instance every model referencing it uses — edits show up everywhere live.
/// Lighting is still a fixed directional light in <see cref="Renderer3D"/>; that changes later.
/// </remarks>
public sealed class Material
{
    private sealed class MaterialData
    {
        public float[] Color { get; set; } = new float[4] { 1, 1, 1, 1 };
        public string? TexturePath { get; set; }
        public string? ShaderType { get; set; }
        public float WaveSpeed { get; set; } = 1.0f;
        public float WaveScale { get; set; } = 1.0f;
        public float WaveStrength { get; set; } = 0.3f;
        public float SpecularPower { get; set; } = 64.0f;
        public float Metallic { get; set; } = 0.0f;
        public float[] Emissive { get; set; } = new float[3] { 0.0f, 0.0f, 0.0f };
        public float EmissiveIntensity { get; set; } = 1.0f;
        public string? NormalMapPath { get; set; }
        public float[] Tiling { get; set; } = new float[2] { 1.0f, 1.0f };
        public bool AutoTile { get; set; } = false;
    }

    private static readonly Dictionary<string, Material> s_cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the base color, multiplied with the texture when one is set. Defaults to opaque white.</summary>
    public Vector4 Color { get; set; } = Vector4.One;
    
    /// <summary>Gets or sets the shader used for rendering this material.</summary>
    public MaterialShaderType ShaderType { get; set; } = MaterialShaderType.Standard;

    /// <summary>Gets or sets the metallic property for the material.</summary>
    public float Metallic { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the emissive color the surface radiates on its own, independent of scene lighting.
    /// Scaled by <see cref="EmissiveIntensity"/>; values above 1 push the surface into HDR and bloom.
    /// </summary>
    public Vector3 EmissiveColor { get; set; } = Vector3.Zero;

    /// <summary>Gets or sets the multiplier applied to <see cref="EmissiveColor"/>. Drive above 1 to make a surface glow.</summary>
    public float EmissiveIntensity { get; set; } = 1.0f;

    /// <summary>Gets or sets the UV tiling (scale) for the texture and normal map.</summary>
    public Vector2 Tiling { get; set; } = Vector2.One;

    /// <summary>If true, automatically tiles the texture based on the object's scale to prevent stretching.</summary>
    public bool AutoTile { get; set; } = false;

    // Water specific properties
    public float WaveSpeed { get; set; } = 1.0f;
    public float WaveScale { get; set; } = 1.0f;
    public float WaveStrength { get; set; } = 0.3f;
    public float SpecularPower { get; set; } = 64.0f;

    /// <summary>Gets the texture sampled across the surface, or <see langword="null"/> for a solid color.</summary>
    public Texture2D? Texture { get; private set; }

    /// <summary>Gets the path to the texture file, used for serialization.</summary>
    public string? TexturePath { get; private set; }

    /// <summary>Gets the normal map sampled across the surface, or <see langword="null"/>.</summary>
    public Texture2D? NormalMap { get; private set; }

    /// <summary>Gets the path to the normal map file, used for serialization.</summary>
    public string? NormalMapPath { get; private set; }

    /// <summary>Gets the file this material was loaded from or last saved to, if any.</summary>
    public string? SourcePath { get; internal set; }

    /// <summary>
    /// Sets the material's texture from an image file, replacing and disposing any previous texture.
    /// Pass <see langword="null"/> or an empty path to clear the texture.
    /// </summary>
    /// <param name="path">The image file path, or <see langword="null"/> to clear.</param>
    public void SetTexture(string? path)
    {
        Texture?.Dispose();
        if (string.IsNullOrEmpty(path))
        {
            Texture = null;
            TexturePath = null;
            return;
        }

        Texture = Texture2D.LoadRef(path);
        TexturePath = path;
    }

    /// <summary>
    /// Sets the material's normal map from an image file, replacing and disposing any previous normal map.
    /// Pass <see langword="null"/> or an empty path to clear the normal map.
    /// </summary>
    /// <param name="path">The image file path, or <see langword="null"/> to clear.</param>
    public void SetNormalMap(string? path)
    {
        NormalMap?.Dispose();
        if (string.IsNullOrEmpty(path))
        {
            NormalMap = null;
            NormalMapPath = null;
            return;
        }

        NormalMap = Texture2D.LoadRef(path);
        NormalMapPath = path;
    }

    /// <summary>
    /// Loads a material from a ".sptmat" file, caching by full path so the same file yields the same
    /// instance. A load failure logs and returns a default white material.
    /// </summary>
    /// <param name="path">The material file path.</param>
    /// <returns>The loaded (or cached) material.</returns>
    public static Material Load(string path)
    {
        if (path == "editor:Checkerboard")
        {
            if (s_cache.TryGetValue(path, out Material? checkerCached))
                return checkerCached;
                
            var checker = new Material { SourcePath = path };
            checker.Texture = Texture2D.CreateCheckerboard();
            checker.TexturePath = path;
            // Auto-tile so the checker squares keep a constant world size instead of stretching across
            // scaled objects (e.g. a 100x100 ground). Tiling 0.25 over the 8-square texture yields
            // ~0.5-unit squares; tweak Tiling in the inspector for a finer/coarser grid.
            checker.AutoTile = true;
            checker.Tiling = new Vector2(0.25f, 0.25f);
            s_cache[path] = checker;
            return checker;
        }

        // Cache by the original reference too, so repeated loads of a guid reference skip re-resolution.
        if (s_cache.TryGetValue(path, out Material? refCached))
        {
            return refCached;
        }

        // A guid reference resolves to its cooked .sptmat through the content host; anything else is a source path.
        string full;
        if (AssetRef.IsGuidRef(path))
        {
            string? cooked = AssetPath.ResolveContent(path);
            if (cooked is null)
            {
                Log.CoreError("Unresolved material reference '{0}'; using a default material.", path);
                var missing = new Material { SourcePath = path };
                s_cache[path] = missing;
                return missing;
            }

            full = Path.GetFullPath(cooked);
        }
        else
        {
            full = Path.GetFullPath(AssetPath.Resolve(path));
        }

        if (s_cache.TryGetValue(full, out Material? cached))
        {
            return cached;
        }

        var material = new Material { SourcePath = full };
        try
        {
            MaterialData? data = JsonSerializer.Deserialize<MaterialData>(File.ReadAllText(full));
            if (data != null)
            {
                if (data.Color.Length == 4)
                {
                    material.Color = new Vector4(data.Color[0], data.Color[1], data.Color[2], data.Color[3]);
                }

                if (!string.IsNullOrEmpty(data.TexturePath))
                {
                    try
                    {
                        material.SetTexture(data.TexturePath);
                    }
                    catch (Exception ex)
                    {
                        Log.CoreError("Failed to load material texture '{0}': {1}", data.TexturePath, ex.Message);
                    }
                }
                
                if (Enum.TryParse<MaterialShaderType>(data.ShaderType, out var type))
                {
                    material.ShaderType = type;
                }

                if (!string.IsNullOrEmpty(data.NormalMapPath))
                {
                    try
                    {
                        material.SetNormalMap(data.NormalMapPath);
                    }
                    catch (Exception ex)
                    {
                        Log.CoreError("Failed to load material normal map '{0}': {1}", data.NormalMapPath, ex.Message);
                    }
                }
                
                material.Metallic = data.Metallic;
                if (data.Emissive != null && data.Emissive.Length == 3)
                {
                    material.EmissiveColor = new Vector3(data.Emissive[0], data.Emissive[1], data.Emissive[2]);
                }
                material.EmissiveIntensity = data.EmissiveIntensity;

                material.WaveSpeed = data.WaveSpeed;
                material.WaveScale = data.WaveScale;
                material.WaveStrength = data.WaveStrength;
                material.SpecularPower = data.SpecularPower;
                
                if (data.Tiling != null && data.Tiling.Length == 2)
                {
                    material.Tiling = new Vector2(data.Tiling[0], data.Tiling[1]);
                }
                material.AutoTile = data.AutoTile;
            }
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to load material '{0}': {1}", full, ex.Message);
        }

        s_cache[full] = material;
        s_cache[path] = material;
        return material;
    }

    /// <summary>
    /// Writes this material to a ".sptmat" file, remembers the path as its source, and caches it so
    /// subsequent <see cref="Load"/> calls for that path return this instance.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    public void Save(string path)
    {
        string full = Path.GetFullPath(path);
        var data = new MaterialData
        {
            Color = new[] { Color.X, Color.Y, Color.Z, Color.W },
            TexturePath = TexturePath != null ? AssetPath.MakeRelative(TexturePath) : null,
            ShaderType = ShaderType.ToString(),
            WaveSpeed = WaveSpeed,
            WaveScale = WaveScale,
            WaveStrength = WaveStrength,
            SpecularPower = SpecularPower,
            Metallic = Metallic,
            Emissive = new[] { EmissiveColor.X, EmissiveColor.Y, EmissiveColor.Z },
            EmissiveIntensity = EmissiveIntensity,
            NormalMapPath = NormalMapPath != null ? AssetPath.MakeRelative(NormalMapPath) : null,
            Tiling = new float[] { Tiling.X, Tiling.Y },
            AutoTile = AutoTile
        };

        File.WriteAllText(full, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        SourcePath = full;
        s_cache[full] = this;
    }
}
