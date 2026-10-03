using System.Numerics;
using Spot.Framework.Graphics;

namespace Spot.Framework.Animation;

/// <summary>
/// A model's node hierarchy, flattened so parents come before their children — the mechanism that turns an
/// <see cref="AnimationClip"/> into a pose and a pose into a skinning palette, with no scene or entities involved.
/// Build one from a model's node tree (<see cref="ModelSceneInfo.Root"/>), sample a clip into global transforms
/// each frame, then compute each skinned submesh's palette for <see cref="BasicRenderer3D.DrawSkinnedMesh"/>.
/// </summary>
public sealed class Skeleton
{
    private readonly string[] _names;
    private readonly int[] _parents;
    private readonly Matrix4x4[] _bindLocal;
    private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);

    // Channel index per node for the clip sampled last; rebuilt when a different clip is sampled.
    private AnimationClip? _cachedClip;
    private int[] _cachedChannels = Array.Empty<int>();

    private Skeleton(List<(string Name, int Parent, Matrix4x4 Local)> nodes)
    {
        _names = nodes.Select(n => n.Name).ToArray();
        _parents = nodes.Select(n => n.Parent).ToArray();
        _bindLocal = nodes.Select(n => n.Local).ToArray();
        for (int i = 0; i < _names.Length; i++)
        {
            // First occurrence wins, matching how the engine binds bones to entities by name.
            _indexByName.TryAdd(BoneName.Normalize(_names[i]), i);
        }
    }

    /// <summary>Gets the number of nodes.</summary>
    public int NodeCount => _names.Length;

    /// <summary>Gets the node names, parents first.</summary>
    public IReadOnlyList<string> NodeNames => _names;

    /// <summary>Gets each node's parent index (-1 for the root).</summary>
    public IReadOnlyList<int> Parents => _parents;

    /// <summary>Gets each node's rest (bind) local transform.</summary>
    public IReadOnlyList<Matrix4x4> BindLocalTransforms => _bindLocal;

    /// <summary>
    /// Flattens a node tree depth-first, so every node follows its parent.
    /// </summary>
    /// <param name="root">The root node (e.g. <see cref="ModelSceneInfo.Root"/>).</param>
    /// <returns>The skeleton.</returns>
    public static Skeleton FromModelNodes(ModelNodeInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var nodes = new List<(string, int, Matrix4x4)>();
        var stack = new Stack<(ModelNodeInfo Node, int Parent)>();
        stack.Push((root, -1));
        while (stack.Count > 0)
        {
            (ModelNodeInfo node, int parent) = stack.Pop();
            int index = nodes.Count;
            nodes.Add((node.Name, parent, node.LocalTransform));
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((node.Children[i], index));
            }
        }

        return new Skeleton(nodes);
    }

    /// <summary>
    /// Finds a node by name, ignoring exporter namespace differences (e.g. <c>mixamorig5:Hips</c> matches
    /// <c>mixamorig:Hips</c>; see <see cref="BoneName.Normalize"/>).
    /// </summary>
    /// <param name="name">The node or bone name.</param>
    /// <returns>The node index, or -1 when absent.</returns>
    public int IndexOf(string name) =>
        _indexByName.TryGetValue(BoneName.Normalize(name), out int index) ? index : -1;

    /// <summary>
    /// Writes every node's global (model-space) transform for a clip at a time. Nodes the clip does not animate
    /// keep their rest pose; with no clip the whole skeleton is in its rest pose.
    /// </summary>
    /// <param name="clip">The clip to sample, or <see langword="null"/> for the rest pose.</param>
    /// <param name="time">The play time in seconds.</param>
    /// <param name="loop">Wrap the time around the clip (otherwise it holds the last frame).</param>
    /// <param name="globalTransforms">Receives one transform per node; must hold <see cref="NodeCount"/>.</param>
    /// <exception cref="ArgumentException">The destination is too small.</exception>
    public void SamplePose(AnimationClip? clip, float time, bool loop, Span<Matrix4x4> globalTransforms)
    {
        if (globalTransforms.Length < NodeCount)
        {
            throw new ArgumentException($"Need room for {NodeCount} transforms.", nameof(globalTransforms));
        }

        int[] channels = ChannelsFor(clip);
        float local = clip?.WrapTime(time, loop) ?? 0.0f;

        for (int i = 0; i < NodeCount; i++)
        {
            Matrix4x4 transform = _bindLocal[i];
            if (clip is not null && channels[i] >= 0)
            {
                AnimationChannel channel = clip.Channels[channels[i]];
                Matrix4x4.Decompose(transform, out Vector3 scale, out Quaternion rotation, out Vector3 position);
                if (channel.TrySamplePosition(local, out Vector3 p))
                {
                    position = p;
                }

                if (channel.TrySampleRotation(local, out Quaternion r))
                {
                    rotation = r;
                }

                if (channel.TrySampleScale(local, out Vector3 s))
                {
                    scale = s;
                }

                transform = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation)
                            * Matrix4x4.CreateTranslation(position);
            }

            globalTransforms[i] = _parents[i] < 0 ? transform : transform * globalTransforms[_parents[i]];
        }
    }

    /// <summary>
    /// Computes a skinned submesh's palette: per bone, <c>InverseBind * boneGlobal * world</c> — world-space
    /// matrices, the convention the skinned shaders expect. A bone missing from the skeleton follows
    /// <paramref name="world"/> alone.
    /// </summary>
    /// <param name="bones">The submesh's bones (see <see cref="Model.BonesFor"/>).</param>
    /// <param name="globalTransforms">The pose from <see cref="SamplePose"/>.</param>
    /// <param name="world">The model's world transform.</param>
    /// <param name="palette">Receives one matrix per bone; must hold <c>bones.Count</c>.</param>
    /// <exception cref="ArgumentException">The palette is too small.</exception>
    public void ComputeSkinningPalette(IReadOnlyList<BoneInfo> bones, ReadOnlySpan<Matrix4x4> globalTransforms,
        Matrix4x4 world, Span<Matrix4x4> palette)
    {
        ArgumentNullException.ThrowIfNull(bones);
        if (palette.Length < bones.Count)
        {
            throw new ArgumentException($"Need room for {bones.Count} matrices.", nameof(palette));
        }

        for (int i = 0; i < bones.Count; i++)
        {
            int node = IndexOf(bones[i].Name);
            palette[i] = node >= 0 && node < globalTransforms.Length
                ? bones[i].InverseBind * globalTransforms[node] * world
                : world;
        }
    }

    // Maps each node to the clip channel that animates it (-1 for none), cached for the last clip.
    private int[] ChannelsFor(AnimationClip? clip)
    {
        if (clip is null)
        {
            return _cachedChannels;
        }

        if (ReferenceEquals(clip, _cachedClip) && _cachedChannels.Length == NodeCount)
        {
            return _cachedChannels;
        }

        int[] channels = new int[NodeCount];
        Array.Fill(channels, -1);
        for (int c = 0; c < clip.Channels.Count; c++)
        {
            if (_indexByName.TryGetValue(clip.Channels[c].NormalizedNodeName, out int node) && channels[node] < 0)
            {
                channels[node] = c;
            }
        }

        _cachedClip = clip;
        _cachedChannels = channels;
        return channels;
    }
}
