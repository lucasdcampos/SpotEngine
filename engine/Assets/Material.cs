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
    }

    private static readonly Dictionary<string, Material> s_cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the base color, multiplied with the texture when one is set. Defaults to opaque white.</summary>
    public Vector4 Color { get; set; } = Vector4.One;
    
    /// <summary>Gets or sets the shader used for rendering this material.</summary>
    public MaterialShaderType ShaderType { get; set; } = MaterialShaderType.Standard;

    // Water specific properties
    public float WaveSpeed { get; set; } = 1.0f;
    public float WaveScale { get; set; } = 1.0f;
    public float WaveStrength { get; set; } = 0.3f;
    public float SpecularPower { get; set; } = 64.0f;

    /// <summary>Gets the texture sampled across the surface, or <see langword="null"/> for a solid color.</summary>
    public Texture2D? Texture { get; private set; }

    /// <summary>Gets the path to the texture file, used for serialization.</summary>
    public string? TexturePath { get; private set; }

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

        Texture = new Texture2D(path);
        TexturePath = path;
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
            s_cache[path] = checker;
            return checker;
        }

        string full = Path.GetFullPath(path);
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
                
                material.WaveSpeed = data.WaveSpeed;
                material.WaveScale = data.WaveScale;
                material.WaveStrength = data.WaveStrength;
                material.SpecularPower = data.SpecularPower;
            }
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to load material '{0}': {1}", full, ex.Message);
        }

        s_cache[full] = material;
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
            TexturePath = TexturePath,
            ShaderType = ShaderType.ToString(),
            WaveSpeed = WaveSpeed,
            WaveScale = WaveScale,
            WaveStrength = WaveStrength,
            SpecularPower = SpecularPower
        };

        File.WriteAllText(full, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        SourcePath = full;
        s_cache[full] = this;
    }
}
