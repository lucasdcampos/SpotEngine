namespace Spot.Scenes;

/// <summary>
/// Base class for the objects a coroutine can <c>yield return</c> to suspend itself until some
/// condition is met. The scheduler advances the coroutine again on the first frame the instruction
/// reports it is done waiting. Yielding <see langword="null"/> from a coroutine waits a single frame;
/// yielding a nested <see cref="System.Collections.IEnumerator"/> runs it to completion before the
/// parent resumes.
/// </summary>
public abstract class YieldInstruction
{
    /// <summary>
    /// Advances this instruction by one frame and reports whether the coroutine should keep waiting.
    /// Called once per frame by the scheduler while the coroutine is suspended on it.
    /// </summary>
    /// <param name="scaledDelta">The scaled frame delta in seconds (respects <see cref="Core.Time.TimeScale"/>).</param>
    /// <param name="unscaledDelta">The real frame delta in seconds, unaffected by time scale.</param>
    /// <returns><see langword="true"/> to keep waiting; <see langword="false"/> to resume the coroutine.</returns>
    protected internal abstract bool Tick(float scaledDelta, float unscaledDelta);
}

/// <summary>
/// Suspends a coroutine for a number of seconds on the <em>scaled</em> clock, so it pauses while the
/// game is paused (<see cref="Core.Time.TimeScale"/> = 0) and stretches during slow motion. Use
/// <see cref="WaitForSecondsRealtime"/> for a wall-clock delay that ignores time scale.
/// </summary>
public sealed class WaitForSeconds : YieldInstruction
{
    private float _remaining;

    /// <summary>Initializes the wait with the given duration in scaled seconds.</summary>
    /// <param name="seconds">How long to wait; non-positive values resume on the next frame.</param>
    public WaitForSeconds(float seconds) => _remaining = seconds;

    /// <inheritdoc />
    protected internal override bool Tick(float scaledDelta, float unscaledDelta)
    {
        _remaining -= scaledDelta;
        return _remaining > 0.0f;
    }
}

/// <summary>
/// Suspends a coroutine for a number of seconds on the <em>real</em> (unscaled) clock, so the delay is
/// unaffected by pause or slow motion. Useful for UI animation and pause menus.
/// </summary>
public sealed class WaitForSecondsRealtime : YieldInstruction
{
    private float _remaining;

    /// <summary>Initializes the wait with the given duration in real seconds.</summary>
    /// <param name="seconds">How long to wait; non-positive values resume on the next frame.</param>
    public WaitForSecondsRealtime(float seconds) => _remaining = seconds;

    /// <inheritdoc />
    protected internal override bool Tick(float scaledDelta, float unscaledDelta)
    {
        _remaining -= unscaledDelta;
        return _remaining > 0.0f;
    }
}

/// <summary>
/// Suspends a coroutine until the given predicate returns <see langword="true"/>, polled once per
/// frame. A throwing predicate is treated as "keep waiting"; the coroutine's own guard reports it.
/// </summary>
public sealed class WaitUntil : YieldInstruction
{
    private readonly Func<bool> _predicate;

    /// <summary>Initializes the wait with the predicate to poll each frame.</summary>
    /// <param name="predicate">Resumes the coroutine when this first returns <see langword="true"/>.</param>
    public WaitUntil(Func<bool> predicate) => _predicate = predicate;

    /// <inheritdoc />
    protected internal override bool Tick(float scaledDelta, float unscaledDelta) => !_predicate();
}

/// <summary>
/// Suspends a coroutine while the given predicate returns <see langword="true"/>, polled once per
/// frame, resuming once it returns <see langword="false"/>.
/// </summary>
public sealed class WaitWhile : YieldInstruction
{
    private readonly Func<bool> _predicate;

    /// <summary>Initializes the wait with the predicate to poll each frame.</summary>
    /// <param name="predicate">Keeps the coroutine suspended while this returns <see langword="true"/>.</param>
    public WaitWhile(Func<bool> predicate) => _predicate = predicate;

    /// <inheritdoc />
    protected internal override bool Tick(float scaledDelta, float unscaledDelta) => _predicate();
}

/// <summary>
/// Suspends a coroutine for a fixed number of rendered frames, independent of frame rate.
/// </summary>
public sealed class WaitForFrames : YieldInstruction
{
    private int _remaining;

    /// <summary>Initializes the wait with the number of frames to skip.</summary>
    /// <param name="frames">How many frames to wait; values below one resume on the next frame.</param>
    public WaitForFrames(int frames) => _remaining = frames;

    /// <inheritdoc />
    protected internal override bool Tick(float scaledDelta, float unscaledDelta) => --_remaining > 0;
}
