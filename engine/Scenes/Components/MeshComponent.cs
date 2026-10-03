using System.Numerics;
using Spot.Engine.Assets;
using Spot.Framework.Graphics;

namespace Spot.Engine.Scenes;

/// <summary>
/// A component that marks an entity as a drawable 3D model. Like <see cref="Sprite2DComponent"/> it is
/// data-only: it holds what to draw (a <see cref="Assets.Model"/> and a color) and a render system
/// draws it together with the entity's <see cref="TransformComponent"/>.
/// </summary>
[ComponentMenu("Mesh Renderer", Order = 20)]
[SceneComponent("MeshRenderer")]
public sealed class MeshComponent : Component
{
    private string? _modelPath;
    private string? _materialPath;

    /// <summary>
    /// Gets or sets the model to draw. When <see langword="null"/>, nothing is drawn.
    /// </summary>
    [AssetReference(nameof(ModelPath))]
    public Model? Model { get; set; }

    /// <summary>
    /// Gets or sets the model reference, used for serialization: a model file, a <c>guid:</c> reference, or a
    /// built-in mesh such as <c>builtin:Mesh/Capsule?radius=0.3</c>. Legacy built-in forms
    /// (<c>primitive:Cube</c>) are stored in their current form.
    /// </summary>
    [HideInInspector]
    public string? ModelPath
    {
        get => _modelPath;
        set => _modelPath = BuiltinAssets.Canonicalize(value);
    }

    /// <summary>
    /// Points the renderer at another model reference and drops the loaded <see cref="Model"/> so the new one is
    /// used: a built-in mesh loads at once, anything else resolves the next time the renderer draws.
    /// </summary>
    /// <param name="reference">The model reference, or <see langword="null"/> to draw nothing.</param>
    public void SetModel(string? reference)
    {
        ModelPath = reference;
        Model = null;
        if (BuiltinAssets.TryGetPrimitive(ModelPath, out PrimitiveSpec spec))
        {
            try
            {
                Model = PrimitiveModelFactory.Get(spec);
            }
            catch (InvalidOperationException)
            {
                // No graphics device yet (headless tools); the renderer resolves the reference when it draws.
            }
        }
    }

    /// <summary>
    /// Gets or sets which submesh of the <see cref="Model"/> this renderer draws. A value of
    /// <c>-1</c> (the default) draws every submesh — the whole model on one entity. A value
    /// <c>&gt;= 0</c> draws only that single submesh, which is how an imported model is spread across an
    /// entity hierarchy: one entity per part, each pointing at the same model but a different submesh.
    /// Out-of-range values draw nothing.
    /// </summary>
    /// <remarks>
    /// Set in code by <see cref="ModelInstantiator"/> when a model is spread across an entity hierarchy, so it
    /// is <see cref="SerializeHiddenAttribute">serialized despite being hidden</see> — otherwise a multi-part
    /// model would lose which submesh each part draws on save/load (and a skinned part could not resolve its
    /// bones).
    /// </remarks>
    [HideInInspector]
    [SerializeHidden]
    public int SubmeshIndex { get; set; } = -1;

    /// <summary>
    /// Gets or sets the material applied to the model. When set, its color and texture are used; when
    /// <see langword="null"/>, the model falls back to the plain <see cref="Color"/>.
    /// </summary>
    [AssetReference(nameof(MaterialPath))]
    public Material? Material { get; set; }

    /// <summary>
    /// Gets or sets the material reference, used for serialization: a <c>.sptmat</c> file, a <c>guid:</c>
    /// reference, or a built-in material such as <c>builtin:Material/Grid</c> (legacy forms are stored in their
    /// current form).
    /// </summary>
    [HideInInspector]
    public string? MaterialPath
    {
        get => _materialPath;
        set => _materialPath = BuiltinAssets.Canonicalize(value);
    }

    /// <summary>
    /// Gets or sets the fallback color used when no <see cref="Material"/> is assigned. Defaults to opaque white.
    /// </summary>
    [InspectorColor]
    public Vector4 Color { get; set; } = Vector4.One;

    /// <summary>
    /// Gets or sets whether this mesh blocks the view of what is behind it, so occlusion culling can skip
    /// the geometry it hides. Off by default: marking a mesh an occluder is a promise about its shape, and
    /// only the scene's author can make it.
    /// </summary>
    /// <remarks>
    /// The occluder used is the mesh's <b>bounding box</b>, not its triangles — that is what keeps the cost
    /// flat no matter how detailed the model is. So mark geometry whose box is solid all the way through: a
    /// wall, a floor slab, a closed crate, a cliff. Do <b>not</b> mark something you can see into or past,
    /// like a hollow building shell, a doorway frame, a fence or a tree — its box would cover the opening
    /// and hide what should show through. Skinned and see-through (alpha or water) meshes are ignored as
    /// occluders even when flagged. The global switch is <c>RenderSettings.OcclusionCulling</c>.
    /// </remarks>
    public bool Occluder { get; set; }
}
