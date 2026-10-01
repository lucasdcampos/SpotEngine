namespace Spot.DebugUI.Undo;

/// <summary>
/// Identity for an in-progress edit, so the tracker can tell whether the change it sees this frame
/// belongs to the edit it already has open or to a different control the user just moved to.
/// </summary>
/// <remarks>
/// Deliberately not the ImGui item id: the controls that matter most here (a color picker, a vector
/// row, an asset slot) are built from several ImGui items whose ids change mid-edit, which is exactly
/// why edit boundaries are not taken from ImGui's per-item state. A key names the *data* being edited
/// instead, which stays stable for as long as the edit lasts.
/// </remarks>
/// <param name="Owner">The object holding the edited value, or its declaring type for a static.</param>
/// <param name="Member">The member's name.</param>
public readonly record struct UndoKey(object Owner, string Member)
{
    /// <summary>A member of a specific object — a component property, a widget field.</summary>
    public static UndoKey For(object owner, string member) => new(owner, member);

    /// <summary>A static global, keyed by its declaring type.</summary>
    public static UndoKey Static(Type declaringType, string member) => new(declaringType, member);

    /// <inheritdoc />
    public override string ToString() => $"{Owner.GetType().Name}.{Member}";
}
