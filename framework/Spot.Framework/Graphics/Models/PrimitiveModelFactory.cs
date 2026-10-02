namespace Spot.Framework.Graphics;

/// <summary>
/// Builds drawable <see cref="Model"/>s of procedural primitives (see <see cref="PrimitiveShape"/>). Use
/// <see cref="Get(PrimitiveSpec)"/> for a shared, cached model — every caller asking for the same spec gets the
/// same instance, so identical primitives batch together — or <see cref="Create(PrimitiveSpec)"/> for a model of
/// your own.
/// </summary>
/// <remarks>
/// <see cref="ModelImporter"/> also loads primitives by reference: <c>primitive:</c> followed by a spec's text
/// form, such as <c>primitive:Cube</c> or <c>primitive:Capsule?radius=0.3&amp;height=1.7</c>.
/// </remarks>
public static class PrimitiveModelFactory
{
    /// <summary>The reference prefix <see cref="ModelImporter"/> loads primitives from.</summary>
    public const string ReferencePrefix = "primitive:";

    private static readonly Dictionary<PrimitiveSpec, Model> s_cache = new();
    private static IGraphicsDevice? s_device;

    /// <summary>Creates a new model of a primitive.</summary>
    /// <param name="spec">The primitive.</param>
    /// <returns>A model the caller owns.</returns>
    public static Model Create(PrimitiveSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        PrimitiveSpec normalized = spec.Normalize();
        MeshData data = normalized.Build();
        return new Model(new[] { new Mesh(data.Vertices, data.Indices) })
        {
            SourcePath = ReferencePrefix + normalized,
        };
    }

    /// <summary>Creates a new model of a primitive from its text form, such as <c>cube</c> or <c>Capsule?radius=0.3</c>.</summary>
    /// <param name="spec">The primitive's text form (see <see cref="PrimitiveSpec.TryParse"/>).</param>
    /// <returns>A model the caller owns.</returns>
    /// <exception cref="ArgumentException">The shape is not recognized.</exception>
    public static Model Create(string spec) =>
        PrimitiveSpec.TryParse(spec, out PrimitiveSpec parsed) ? Create(parsed) : throw new ArgumentException($"Unknown primitive: {spec}", nameof(spec));

    /// <summary>
    /// Gets the shared model of a primitive, building it on first use. The cache is dropped when the graphics
    /// device changes.
    /// </summary>
    /// <param name="spec">The primitive.</param>
    /// <returns>The shared model; do not dispose it.</returns>
    public static Model Get(PrimitiveSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!ReferenceEquals(s_device, Renderer.Device))
        {
            s_cache.Clear();
            s_device = Renderer.Device;
        }

        PrimitiveSpec normalized = spec.Normalize();
        if (!s_cache.TryGetValue(normalized, out Model? model))
        {
            model = Create(normalized);
            s_cache[normalized] = model;
        }

        return model;
    }

    /// <summary>Gets the shared model of a primitive from its text form. See <see cref="Get(PrimitiveSpec)"/>.</summary>
    /// <param name="spec">The primitive's text form.</param>
    /// <returns>The shared model.</returns>
    /// <exception cref="ArgumentException">The shape is not recognized.</exception>
    public static Model Get(string spec) =>
        PrimitiveSpec.TryParse(spec, out PrimitiveSpec parsed) ? Get(parsed) : throw new ArgumentException($"Unknown primitive: {spec}", nameof(spec));

    /// <summary>Returns the canonical <c>primitive:</c> reference for a spec.</summary>
    /// <param name="spec">The primitive.</param>
    /// <returns>The reference, such as <c>primitive:Capsule?radius=0.3</c>.</returns>
    public static string ReferenceOf(PrimitiveSpec spec) => ReferencePrefix + spec.Normalize();

    // The ModelImporter provider for primitive: references.
    internal static Model LoadReference(string reference) =>
        TryParseReference(reference, out PrimitiveSpec spec) ? Get(spec) : throw new ArgumentException($"Unknown primitive reference '{reference}'.");

    /// <summary>Tries to read a <c>primitive:</c> reference.</summary>
    /// <param name="reference">The reference.</param>
    /// <param name="spec">Receives the primitive.</param>
    /// <returns><see langword="true"/> when the reference names a known primitive.</returns>
    public static bool TryParseReference(string? reference, out PrimitiveSpec spec)
    {
        spec = PrimitiveSpec.For(PrimitiveShape.Cube);
        return reference is not null
            && reference.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase)
            && PrimitiveSpec.TryParse(reference[ReferencePrefix.Length..], out spec);
    }
}
