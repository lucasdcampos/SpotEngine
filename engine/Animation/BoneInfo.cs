using System.Numerics;

namespace Spot.Animation;

/// <summary>
/// One bone of a skinned submesh: the name of the node that drives it and its inverse bind (offset) matrix.
/// A skinned vertex stores up to four bone indices into the submesh's bone list; at draw time each bone's
/// skinning matrix is <c>InverseBind * boneWorld</c>, which maps the vertex from bind-pose mesh space into
/// the bone's current world space.
/// </summary>
/// <remarks>
/// <see cref="InverseBind"/> is Assimp's <c>mOffsetMatrix</c> transposed into the engine's row-vector
/// convention (the same transpose applied to node transforms in <see cref="Assets.AssimpModelImporter"/>).
/// </remarks>
public readonly struct BoneInfo
{
    /// <summary>Initializes a bone binding from the driving node's name and its inverse bind matrix.</summary>
    /// <param name="name">The name of the node (entity) that poses this bone.</param>
    /// <param name="inverseBind">The inverse bind (offset) matrix, in engine convention.</param>
    public BoneInfo(string name, Matrix4x4 inverseBind)
    {
        Name = name;
        InverseBind = inverseBind;
    }

    /// <summary>Gets the name of the node (entity) that drives this bone.</summary>
    public string Name { get; }

    /// <summary>Gets the inverse bind (offset) matrix, in engine (row-vector) convention.</summary>
    public Matrix4x4 InverseBind { get; }
}
