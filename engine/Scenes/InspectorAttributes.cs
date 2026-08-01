using System;

namespace Spot.Scenes;

/// <summary>
/// Marks a <see cref="Component"/> type as user-facing and tells the editor how to present it: the
/// title shown on its inspector header, whether it appears in the "Add Component" menu, and whether
/// it can be removed. Components without this attribute (e.g. internal ones like
/// <see cref="RelationshipComponent"/>) are not drawn by the reflection-based inspector.
/// </summary>
/// <remarks>
/// This is pure metadata read by the editor via reflection; the engine itself never inspects it, so
/// it carries no runtime dependency on any UI toolkit.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentMenuAttribute : Attribute
{
    /// <summary>Initializes the attribute with the component's display name.</summary>
    /// <param name="displayName">The title shown on the inspector header and the add-component menu.</param>
    public ComponentMenuAttribute(string displayName)
    {
        DisplayName = displayName;
    }

    /// <summary>Gets the title shown on the inspector header and the add-component menu.</summary>
    public string DisplayName { get; }

    /// <summary>Gets or sets whether the component appears in the "Add Component" menu. Defaults to <see langword="true"/>.</summary>
    public bool Addable { get; set; } = true;

    /// <summary>Gets or sets whether the component can be removed from the inspector. Defaults to <see langword="true"/>.</summary>
    public bool Removable { get; set; } = true;

    /// <summary>Gets or sets the sort order among components (lower is drawn first). Ties break by display name.</summary>
    public int Order { get; set; }
}

/// <summary>Hides a component property from the reflection-based inspector (e.g. serialized paths or computed values).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HideInInspectorAttribute : Attribute
{
}

/// <summary>Overrides the label the inspector derives from a property's name.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InspectorLabelAttribute : Attribute
{
    /// <summary>Initializes the attribute with the label to display.</summary>
    /// <param name="label">The label shown in place of the auto-derived one.</param>
    public InspectorLabelAttribute(string label)
    {
        Label = label;
    }

    /// <summary>Gets the label shown in place of the auto-derived one.</summary>
    public string Label { get; }
}

/// <summary>
/// Configures the drag range and step for a <see cref="float"/> property, matching the parameters of
/// the editor's drag-float widget.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InspectorRangeAttribute : Attribute
{
    /// <summary>Initializes the attribute with the drag bounds and step.</summary>
    /// <param name="min">The minimum value (0 for both min and max means unbounded).</param>
    /// <param name="max">The maximum value (0 for both min and max means unbounded).</param>
    /// <param name="speed">The drag step per pixel.</param>
    public InspectorRangeAttribute(float min, float max, float speed = 0.1f)
    {
        Min = min;
        Max = max;
        Speed = speed;
    }

    /// <summary>Gets the minimum value.</summary>
    public float Min { get; }

    /// <summary>Gets the maximum value.</summary>
    public float Max { get; }

    /// <summary>Gets the drag step per pixel.</summary>
    public float Speed { get; }
}

/// <summary>Draws a <c>Vector3</c>/<c>Vector4</c> property as a color picker rather than a vector field.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InspectorColorAttribute : Attribute
{
}

/// <summary>Sets the value a vector field resets each axis to when its axis badge is clicked (e.g. 1 for scale).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InspectorResetAttribute : Attribute
{
    /// <summary>Initializes the attribute with the per-axis reset value.</summary>
    /// <param name="value">The value each axis resets to.</param>
    public InspectorResetAttribute(float value)
    {
        Value = value;
    }

    /// <summary>Gets the value each axis resets to.</summary>
    public float Value { get; }
}

/// <summary>
/// Shows the property only when another property on the same component currently equals one of the
/// given values. Used for type-dependent fields (e.g. a point light's range, a perspective camera's
/// field of view). The referenced property is typically an enum or a bool.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ShowIfAttribute : Attribute
{
    /// <summary>Initializes the attribute with the gating property and the values that reveal this one.</summary>
    /// <param name="propertyName">The name of the property to test on the same component.</param>
    /// <param name="values">The values of that property for which this property is shown.</param>
    public ShowIfAttribute(string propertyName, params object[] values)
    {
        PropertyName = propertyName;
        Values = values;
    }

    /// <summary>Gets the name of the property to test on the same component.</summary>
    public string PropertyName { get; }

    /// <summary>Gets the values of that property for which this property is shown.</summary>
    public object[] Values { get; }
}

/// <summary>
/// Marks a property whose type is an asset reference (texture, model, material) and names the sibling
/// string property that stores its serialized path, so the editor's asset slot can update both when
/// the reference changes.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AssetReferenceAttribute : Attribute
{
    /// <summary>Initializes the attribute with the sibling path property's name.</summary>
    /// <param name="pathPropertyName">The name of the string property holding the asset's serialized path.</param>
    public AssetReferenceAttribute(string pathPropertyName)
    {
        PathPropertyName = pathPropertyName;
    }

    /// <summary>Gets the name of the string property holding the asset's serialized path.</summary>
    public string PathPropertyName { get; }
}
