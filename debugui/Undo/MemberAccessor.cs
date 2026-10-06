using System.Reflection;

namespace Spot.DebugUI.Undo;

/// <summary>
/// Reads and writes one piece of authored state on an object, hiding how it is declared. Components
/// expose their data as properties, UI widgets as public fields, and script fields arrive through the
/// inspector's own get/set delegates — the undo system needs a single handle covering all three.
/// </summary>
public sealed class MemberAccessor : IEquatable<MemberAccessor>
{
    private readonly Func<object, object?> _get;
    private readonly Action<object, object?> _set;

    // Identity for comparing two accessors: the PropertyInfo/FieldInfo itself, or a caller-supplied
    // key for delegate-based members. Two accessors to the same member must compare equal so the
    // tracker can confirm a commit belongs to the edit it started.
    private readonly object _identity;

    private MemberAccessor(string name, Type memberType, object identity,
        Func<object, object?> get, Action<object, object?> set)
    {
        Name = name;
        MemberType = memberType;
        _identity = identity;
        _get = get;
        _set = set;
    }

    /// <summary>The member's name, used to build an action label ("Set Intensity").</summary>
    public string Name { get; }

    /// <summary>The declared type of the member's value.</summary>
    public Type MemberType { get; }

    /// <summary>Wraps a property (the shape every <see cref="Spot.Engine.Component"/> field takes).</summary>
    public static MemberAccessor FromProperty(PropertyInfo property) => new(
        property.Name, property.PropertyType, property,
        t => property.GetValue(t), (t, v) => property.SetValue(t, v));

    /// <summary>Wraps a public field (the shape every UI <see cref="Spot.Engine.UI.Widget"/> field takes).</summary>
    public static MemberAccessor FromField(FieldInfo field) => new(
        field.Name, field.FieldType, field,
        t => field.GetValue(t), (t, v) => field.SetValue(t, v));

    /// <summary>
    /// Wraps an arbitrary get/set pair, for state that is not a plain member — a script field reached
    /// through the inspector's own accessors, or a static global. <paramref name="identity"/> must be
    /// stable and unique for the member so two accessors to it compare equal.
    /// </summary>
    public static MemberAccessor FromDelegates(
        string name, Type memberType, object identity,
        Func<object, object?> get, Action<object, object?> set) =>
        new(name, memberType, identity, get, set);

    /// <summary>Reads the member's current value.</summary>
    public object? Get(object target) => _get(target);

    /// <summary>Writes the member's value.</summary>
    public void Set(object target, object? value) => _set(target, value);

    /// <inheritdoc />
    public bool Equals(MemberAccessor? other) => other is not null && _identity.Equals(other._identity);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as MemberAccessor);

    /// <inheritdoc />
    public override int GetHashCode() => _identity.GetHashCode();
}
