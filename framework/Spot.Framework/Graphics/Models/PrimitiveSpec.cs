using System.Globalization;
using System.Text;

namespace Spot.Framework.Graphics;

/// <summary>
/// The procedural shapes <see cref="PrimitiveMeshes"/> can generate.
/// </summary>
public enum PrimitiveShape
{
    /// <summary>A box of <see cref="PrimitiveSpec.Width"/> × <see cref="PrimitiveSpec.Height"/> × <see cref="PrimitiveSpec.Depth"/>.</summary>
    Cube,

    /// <summary>A UV sphere of <see cref="PrimitiveSpec.Radius"/>.</summary>
    Sphere,

    /// <summary>A capsule: a cylinder capped by two hemispheres, <see cref="PrimitiveSpec.Height"/> tall in total.</summary>
    Capsule,

    /// <summary>A capped cylinder of <see cref="PrimitiveSpec.Radius"/> and <see cref="PrimitiveSpec.Height"/>.</summary>
    Cylinder,

    /// <summary>A capped cone of base <see cref="PrimitiveSpec.Radius"/> and <see cref="PrimitiveSpec.Height"/>, apex up.</summary>
    Cone,

    /// <summary>A flat, subdivided ground plane on XZ facing up.</summary>
    Plane,

    /// <summary>A single flat rectangle on XY facing +Z.</summary>
    Quad,
}

/// <summary>
/// A parameter of a <see cref="PrimitiveSpec"/>. Each shape uses a subset; see <see cref="PrimitiveSpec.ParametersOf"/>.
/// </summary>
public enum PrimitiveParameter
{
    /// <summary>The size along X.</summary>
    Width,

    /// <summary>The size along Y (for a capsule, the total height including both caps).</summary>
    Height,

    /// <summary>The size along Z.</summary>
    Depth,

    /// <summary>The radius.</summary>
    Radius,

    /// <summary>The number of divisions around the vertical axis.</summary>
    Segments,

    /// <summary>The number of latitude bands (per hemisphere for a capsule).</summary>
    Rings,

    /// <summary>The number of grid cells along each side of a plane.</summary>
    Subdivisions,
}

/// <summary>
/// Describes one procedural primitive — its <see cref="Shape"/> and the parameters that shape uses — and gives it
/// a canonical text form such as <c>Capsule?radius=0.3&amp;height=1.7</c>, which lists only the parameters that
/// differ from the shape's defaults. Equal specs produce identical meshes and identical text, so either works as a
/// cache key.
/// </summary>
/// <remarks>
/// Parameters a shape does not use stay at zero, and every value is kept in range (sizes at least 0.001, at least
/// three segments, a capsule at least as tall as its two caps) and rounded to four decimals. Use
/// <see cref="With"/> or <see cref="Parse"/> to get a normalized spec; a hand-built <c>with</c> expression is
/// normalized when the mesh is generated.
/// </remarks>
public sealed record PrimitiveSpec
{
    private const float MinSize = 0.001f;
    private const float MaxSize = 10000.0f;

    private static readonly PrimitiveParameter[] BoxParameters = { PrimitiveParameter.Width, PrimitiveParameter.Height, PrimitiveParameter.Depth };
    private static readonly PrimitiveParameter[] SphereParameters = { PrimitiveParameter.Radius, PrimitiveParameter.Segments, PrimitiveParameter.Rings };
    private static readonly PrimitiveParameter[] CapsuleParameters = { PrimitiveParameter.Radius, PrimitiveParameter.Height, PrimitiveParameter.Segments, PrimitiveParameter.Rings };
    private static readonly PrimitiveParameter[] RoundParameters = { PrimitiveParameter.Radius, PrimitiveParameter.Height, PrimitiveParameter.Segments };
    private static readonly PrimitiveParameter[] PlaneParameters = { PrimitiveParameter.Width, PrimitiveParameter.Depth, PrimitiveParameter.Subdivisions };
    private static readonly PrimitiveParameter[] QuadParameters = { PrimitiveParameter.Width, PrimitiveParameter.Height };

    private PrimitiveSpec(PrimitiveShape shape)
    {
        Shape = shape;
    }

    /// <summary>Gets the shape.</summary>
    public PrimitiveShape Shape { get; }

    /// <summary>Gets the size along X (cube, plane, quad).</summary>
    public float Width { get; init; }

    /// <summary>Gets the size along Y (cube, quad, cylinder, cone; the total height of a capsule).</summary>
    public float Height { get; init; }

    /// <summary>Gets the size along Z (cube, plane).</summary>
    public float Depth { get; init; }

    /// <summary>Gets the radius (sphere, capsule, cylinder, cone).</summary>
    public float Radius { get; init; }

    /// <summary>Gets the divisions around the vertical axis (sphere, capsule, cylinder, cone).</summary>
    public int Segments { get; init; }

    /// <summary>Gets the latitude bands (sphere; per hemisphere for a capsule).</summary>
    public int Rings { get; init; }

    /// <summary>Gets the grid cells along each side (plane).</summary>
    public int Subdivisions { get; init; }

    /// <summary>Gets every shape, in declaration order.</summary>
    public static IReadOnlyList<PrimitiveShape> Shapes { get; } = Enum.GetValues<PrimitiveShape>();

    /// <summary>
    /// Returns a shape with its default parameters: a 1-unit cube, quad and 0.5-radius sphere; a 2-unit-tall
    /// capsule and cylinder and a 1-unit-tall cone of radius 0.5; and a 10 × 10 plane in 10 × 10 cells.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <returns>The default spec.</returns>
    public static PrimitiveSpec For(PrimitiveShape shape) => shape switch
    {
        PrimitiveShape.Cube => new(shape) { Width = 1, Height = 1, Depth = 1 },
        PrimitiveShape.Sphere => new(shape) { Radius = 0.5f, Segments = 32, Rings = 16 },
        PrimitiveShape.Capsule => new(shape) { Radius = 0.5f, Height = 2, Segments = 32, Rings = 8 },
        PrimitiveShape.Cylinder => new(shape) { Radius = 0.5f, Height = 2, Segments = 32 },
        PrimitiveShape.Cone => new(shape) { Radius = 0.5f, Height = 1, Segments = 32 },
        PrimitiveShape.Plane => new(shape) { Width = 10, Depth = 10, Subdivisions = 10 },
        PrimitiveShape.Quad => new(shape) { Width = 1, Height = 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown primitive shape."),
    };

    /// <summary>Gets the parameters a shape uses, in canonical order.</summary>
    /// <param name="shape">The shape.</param>
    /// <returns>The shape's parameters.</returns>
    public static IReadOnlyList<PrimitiveParameter> ParametersOf(PrimitiveShape shape) => shape switch
    {
        PrimitiveShape.Cube => BoxParameters,
        PrimitiveShape.Sphere => SphereParameters,
        PrimitiveShape.Capsule => CapsuleParameters,
        PrimitiveShape.Cylinder or PrimitiveShape.Cone => RoundParameters,
        PrimitiveShape.Plane => PlaneParameters,
        PrimitiveShape.Quad => QuadParameters,
        _ => Array.Empty<PrimitiveParameter>(),
    };

    /// <summary>Gets whether a parameter is a whole number (segments, rings, subdivisions).</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns><see langword="true"/> for counts.</returns>
    public static bool IsCount(PrimitiveParameter parameter) =>
        parameter is PrimitiveParameter.Segments or PrimitiveParameter.Rings or PrimitiveParameter.Subdivisions;

    /// <summary>Gets a parameter's value.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The value (counts as whole numbers).</returns>
    public float Get(PrimitiveParameter parameter) => parameter switch
    {
        PrimitiveParameter.Width => Width,
        PrimitiveParameter.Height => Height,
        PrimitiveParameter.Depth => Depth,
        PrimitiveParameter.Radius => Radius,
        PrimitiveParameter.Segments => Segments,
        PrimitiveParameter.Rings => Rings,
        PrimitiveParameter.Subdivisions => Subdivisions,
        _ => 0,
    };

    /// <summary>
    /// Returns a normalized copy with one parameter changed. Setting a parameter the shape does not use has no
    /// effect.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="value">The new value; clamped to the parameter's range, counts rounded.</param>
    /// <returns>The changed spec.</returns>
    public PrimitiveSpec With(PrimitiveParameter parameter, float value)
    {
        if (!ParametersOf(Shape).Contains(parameter) || !float.IsFinite(value))
        {
            return Normalize();
        }

        int count = (int)MathF.Round(Math.Clamp(value, 0, 100000));
        PrimitiveSpec changed = parameter switch
        {
            PrimitiveParameter.Width => this with { Width = value },
            PrimitiveParameter.Height => this with { Height = value },
            PrimitiveParameter.Depth => this with { Depth = value },
            PrimitiveParameter.Radius => this with { Radius = value },
            PrimitiveParameter.Segments => this with { Segments = count },
            PrimitiveParameter.Rings => this with { Rings = count },
            PrimitiveParameter.Subdivisions => this with { Subdivisions = count },
            _ => this,
        };

        return changed.Normalize();
    }

    /// <summary>
    /// Returns this spec with every used parameter in range and rounded, and every unused one at zero.
    /// </summary>
    /// <returns>The normalized spec.</returns>
    public PrimitiveSpec Normalize()
    {
        PrimitiveSpec defaults = For(Shape);
        IReadOnlyList<PrimitiveParameter> used = ParametersOf(Shape);
        float Size(PrimitiveParameter p, float v) => used.Contains(p) ? Round(Math.Clamp(Finite(v, defaults.Get(p)), MinSize, MaxSize)) : 0;
        int Count(PrimitiveParameter p, int v, int min, int max) => used.Contains(p) ? Math.Clamp(v, min, max) : 0;

        float radius = Size(PrimitiveParameter.Radius, Radius);
        float height = Size(PrimitiveParameter.Height, Height);
        if (Shape == PrimitiveShape.Capsule)
        {
            height = MathF.Max(height, Round(2 * radius));
        }

        return new PrimitiveSpec(Shape)
        {
            Width = Size(PrimitiveParameter.Width, Width),
            Height = height,
            Depth = Size(PrimitiveParameter.Depth, Depth),
            Radius = radius,
            Segments = Count(PrimitiveParameter.Segments, Segments, 3, 256),
            Rings = Count(PrimitiveParameter.Rings, Rings, Shape == PrimitiveShape.Sphere ? 2 : 1, 128),
            Subdivisions = Count(PrimitiveParameter.Subdivisions, Subdivisions, 1, 256),
        };
    }

    /// <summary>Generates the spec's geometry. See <see cref="PrimitiveMeshes"/>.</summary>
    /// <returns>The CPU mesh.</returns>
    public MeshData Build() => PrimitiveMeshes.Build(this);

    /// <summary>
    /// Returns the canonical text form: the shape name, then — only for parameters that differ from the shape's
    /// defaults — a query such as <c>?radius=0.3&amp;height=1.7</c>.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public override string ToString()
    {
        PrimitiveSpec spec = Normalize();
        PrimitiveSpec defaults = For(Shape);
        var text = new StringBuilder(Shape.ToString());
        char separator = '?';
        foreach (PrimitiveParameter parameter in ParametersOf(Shape))
        {
            float value = spec.Get(parameter);
            if (value == defaults.Get(parameter))
            {
                continue;
            }

            text.Append(separator).Append(KeyOf(parameter)).Append('=')
                .Append(value.ToString("0.####", CultureInfo.InvariantCulture));
            separator = '&';
        }

        return text.ToString();
    }

    /// <summary>
    /// Parses a spec from its text form: a shape name (any case), optionally followed by <c>?key=value</c> pairs
    /// joined with <c>&amp;</c>. Missing parameters take the shape's defaults; out-of-range values are clamped;
    /// unknown keys and malformed values are ignored.
    /// </summary>
    /// <param name="text">The text, such as <c>cube</c> or <c>Capsule?radius=0.3&amp;height=1.7</c>.</param>
    /// <param name="spec">Receives the normalized spec.</param>
    /// <returns><see langword="false"/> when the shape name is not recognized.</returns>
    public static bool TryParse(string? text, out PrimitiveSpec spec)
    {
        spec = For(PrimitiveShape.Cube);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int query = text.IndexOf('?');
        string name = (query < 0 ? text : text[..query]).Trim();
        if (!Enum.TryParse(name, ignoreCase: true, out PrimitiveShape shape) || !Enum.IsDefined(shape) || int.TryParse(name, out _))
        {
            return false;
        }

        PrimitiveSpec result = For(shape);
        if (query >= 0)
        {
            foreach (string pair in text[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0
                    || !TryKey(pair[..equals].Trim(), out PrimitiveParameter parameter)
                    || !float.TryParse(pair[(equals + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    continue;
                }

                result = result.With(parameter, value);
            }
        }

        spec = result.Normalize();
        return true;
    }

    /// <summary>Parses a spec from its text form. See <see cref="TryParse"/>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The normalized spec.</returns>
    /// <exception cref="FormatException">The shape name is not recognized.</exception>
    public static PrimitiveSpec Parse(string text) =>
        TryParse(text, out PrimitiveSpec spec) ? spec : throw new FormatException($"Unknown primitive '{text}'.");

    /// <summary>Gets the lowercase key a parameter uses in the text form.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The key, such as <c>radius</c>.</returns>
    public static string KeyOf(PrimitiveParameter parameter) => parameter.ToString().ToLowerInvariant();

    private static bool TryKey(string key, out PrimitiveParameter parameter) =>
        Enum.TryParse(key, ignoreCase: true, out parameter) && Enum.IsDefined(parameter) && !int.TryParse(key, out _);

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;

    private static float Round(float value) => MathF.Round(value, 4);
}
