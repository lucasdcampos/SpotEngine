namespace Spot.Events;

/// <summary>
/// Raised once per tick.
/// </summary>
public sealed class AppTickEvent : Event
{
    /// <inheritdoc />
    public override EventType Type => EventType.AppTick;

    /// <inheritdoc />
    public override string Name => "AppTick";

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Application;
}

/// <summary>
/// Raised once per update.
/// </summary>
public sealed class AppUpdateEvent : Event
{
    /// <inheritdoc />
    public override EventType Type => EventType.AppUpdate;

    /// <inheritdoc />
    public override string Name => "AppUpdate";

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Application;
}

/// <summary>
/// Raised once per render.
/// </summary>
public sealed class AppRenderEvent : Event
{
    /// <inheritdoc />
    public override EventType Type => EventType.AppRender;

    /// <inheritdoc />
    public override string Name => "AppRender";

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Application;
}
