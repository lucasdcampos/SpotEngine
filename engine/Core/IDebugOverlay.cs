namespace Spot.Core;

/// <summary>
/// A hostable in-game debug overlay: the runtime hierarchy/inspector/time-control panels a build can toggle
/// at runtime. Supplied to the engine via <see cref="ApplicationSpec.DebugOverlay"/> and surfaced as
/// <see cref="Application.Debugger"/>. The engine keeps only this contract; the ImGui panels that implement
/// it live outside the runtime, in <c>Spot.DebugUI</c>, so a game references that assembly only if it wants
/// the overlay.
/// </summary>
public interface IDebugOverlay : IEngineService
{
    /// <summary>
    /// Whether any overlay window is currently open, so the engine can route input to it rather than the game.
    /// </summary>
    bool IsOpen { get; }
}
