using System.Collections;
using System.Numerics;
using Spot.Core;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// Base class for per-entity scripts (the counterpart to Unity's MonoBehaviour). Derive from it,
/// override the lifecycle hooks, and attach it with <see cref="Entity.AddScript{T}()"/>. The engine
/// runs it automatically for the active scene.
/// </summary>
public abstract class EntityBehaviour
{
    // Lazily created the first time the script schedules a coroutine or invoke, so scripts that never
    // use scheduling pay nothing (no allocation, no per-frame tick).
    private ScriptScheduler? _scheduler;

    /// <summary>
    /// Gets the entity this script is attached to.
    /// </summary>
    public Entity Entity { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="OnCreate"/> has run.
    /// </summary>
    internal bool Started { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the script threw from a lifecycle hook and was
    /// disabled by the engine. A faulted script is skipped on subsequent frames so one broken script
    /// neither crashes the engine nor spams the log, while other scripts keep running.
    /// </summary>
    internal bool Faulted { get; set; }

    /// <summary>
    /// Gets the scene the script's entity belongs to.
    /// </summary>
    protected Scene Scene => Entity.Scene;

    /// <summary>
    /// Gets the scene's screen-space UI root. Build HUDs and menus from here — for example
    /// <c>UI.Button("Play").OnClick += StartGame;</c>. The engine lays it out, routes pointer input and
    /// draws it every frame; see <see cref="Spot.UI.UIRoot"/>.
    /// </summary>
    protected Spot.UI.UIRoot UI => Scene.UI;

    /// <summary>
    /// Gets the attached entity's component of the given type.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>The component.</returns>
    protected T GetComponent<T>()
        where T : class => Entity.GetComponent<T>();

    /// <summary>
    /// Creates a new entity in this script's scene.
    /// </summary>
    /// <param name="name">The entity name.</param>
    /// <returns>The new entity.</returns>
    protected Entity Instantiate(string name = "Entity") => Scene.Instantiate(name);

    /// <summary>
    /// Finds the first entity in this script's scene with the given name, or <see langword="null"/>.
    /// </summary>
    /// <param name="name">The entity name to search for.</param>
    protected Entity? Find(string name) => Scene.Find(name);

    /// <summary>
    /// Finds the first entity in this script's scene tagged <paramref name="tag"/>, or <see langword="null"/>.
    /// </summary>
    /// <param name="tag">The tag to search for.</param>
    protected Entity? FindByTag(string tag) => Scene.FindByTag(tag);

    /// <summary>
    /// Marks the given entity for destruction at the end of the frame.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    protected void Destroy(Entity entity) => Scene.Destroy(entity);

    /// <summary>
    /// Marks this script's own entity for destruction at the end of the frame.
    /// </summary>
    protected void Destroy() => Scene.Destroy(Entity);

    private ScriptScheduler Scheduler => _scheduler ??= new ScriptScheduler(this);

    /// <summary>
    /// Starts a coroutine: a method returning <see cref="IEnumerator"/> that <c>yield return</c>s to
    /// suspend itself across frames. Yield <see langword="null"/> to wait one frame, a
    /// <see cref="YieldInstruction"/> (such as <see cref="WaitForSeconds"/>, <see cref="WaitUntil"/>) to
    /// wait for a condition, or another <see cref="IEnumerator"/> to run it as a nested coroutine before
    /// resuming. The coroutine takes its first step on the next frame and stops automatically when the
    /// entity is destroyed or its scene is left.
    /// </summary>
    /// <param name="routine">The coroutine body.</param>
    /// <returns>A handle for stopping the coroutine or polling <see cref="Coroutine.IsRunning"/>.</returns>
    protected Coroutine StartCoroutine(IEnumerator routine) => Scheduler.StartCoroutine(routine);

    /// <summary>
    /// Stops a coroutine previously started on this script. A no-op if it already finished.
    /// </summary>
    /// <param name="coroutine">The handle returned by <see cref="StartCoroutine"/>.</param>
    protected void StopCoroutine(Coroutine coroutine) => _scheduler?.StopCoroutine(coroutine);

    /// <summary>
    /// Stops every coroutine currently running on this script.
    /// </summary>
    protected void StopAllCoroutines() => _scheduler?.StopAllCoroutines();

    /// <summary>
    /// Schedules <paramref name="action"/> to run once after <paramref name="delay"/> seconds on the
    /// scaled clock (so it respects pause and slow motion).
    /// </summary>
    /// <param name="action">The callback to invoke.</param>
    /// <param name="delay">The delay in scaled seconds before the callback runs.</param>
    protected void Invoke(Action action, float delay) => Scheduler.Invoke(action, delay);

    /// <summary>
    /// Schedules <paramref name="action"/> to run after <paramref name="delay"/> seconds and then
    /// repeatedly every <paramref name="interval"/> seconds until <see cref="CancelInvoke()"/> or the
    /// entity is destroyed. Timing is on the scaled clock.
    /// </summary>
    /// <param name="action">The callback to invoke.</param>
    /// <param name="delay">The delay in scaled seconds before the first call.</param>
    /// <param name="interval">The interval in scaled seconds between subsequent calls.</param>
    protected void InvokeRepeating(Action action, float delay, float interval) =>
        Scheduler.InvokeRepeating(action, delay, interval);

    /// <summary>
    /// Cancels every pending invocation scheduled on this script.
    /// </summary>
    protected void CancelInvoke() => _scheduler?.CancelInvoke();

    /// <summary>
    /// Cancels every pending invocation whose callback is <paramref name="action"/>.
    /// </summary>
    /// <param name="action">The callback whose scheduled invocations should be cancelled.</param>
    protected void CancelInvoke(Action action) => _scheduler?.CancelInvoke(action);

    /// <summary>
    /// Gets a value indicating whether any invocation scheduled via <see cref="Invoke"/> or
    /// <see cref="InvokeRepeating"/> is still pending.
    /// </summary>
    /// <returns><see langword="true"/> if at least one invocation is pending.</returns>
    protected bool IsInvoking() => _scheduler?.IsInvoking() ?? false;

    /// <summary>
    /// Advances this script's coroutines and scheduled invocations by one frame. Called by the
    /// <see cref="ScriptSystem"/> after <see cref="OnUpdate"/>; does nothing until the script schedules
    /// its first piece of work.
    /// </summary>
    internal void TickScheduling(float scaledDelta, float unscaledDelta) =>
        _scheduler?.Tick(scaledDelta, unscaledDelta);

    /// <summary>
    /// Tweens a scalar from <paramref name="from"/> to <paramref name="to"/> over
    /// <paramref name="duration"/> seconds, calling <paramref name="onUpdate"/> with the eased value
    /// each frame. Runs as a coroutine, so stop it with the returned handle or let it end with the entity.
    /// </summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="duration">The duration in seconds; non-positive snaps straight to <paramref name="to"/>.</param>
    /// <param name="onUpdate">Receives the interpolated value each frame.</param>
    /// <param name="ease">The easing curve. Defaults to <see cref="Ease.Linear"/>.</param>
    /// <param name="onComplete">Optional callback run once the tween reaches its end.</param>
    /// <param name="unscaled">When true, advances on the real clock, ignoring pause and slow motion.</param>
    /// <returns>A handle to the running tween coroutine.</returns>
    protected Coroutine Tween(
        float from,
        float to,
        float duration,
        Action<float> onUpdate,
        Ease ease = Ease.Linear,
        Action? onComplete = null,
        bool unscaled = false) =>
        StartCoroutine(TweenRoutine(
            t => onUpdate(from + (to - from) * t), duration, ease, onComplete, unscaled));

    /// <summary>
    /// Tweens a vector from <paramref name="from"/> to <paramref name="to"/> over
    /// <paramref name="duration"/> seconds, calling <paramref name="onUpdate"/> each frame.
    /// </summary>
    /// <param name="from">The starting vector.</param>
    /// <param name="to">The ending vector.</param>
    /// <param name="duration">The duration in seconds; non-positive snaps straight to <paramref name="to"/>.</param>
    /// <param name="onUpdate">Receives the interpolated vector each frame.</param>
    /// <param name="ease">The easing curve. Defaults to <see cref="Ease.Linear"/>.</param>
    /// <param name="onComplete">Optional callback run once the tween reaches its end.</param>
    /// <param name="unscaled">When true, advances on the real clock, ignoring pause and slow motion.</param>
    /// <returns>A handle to the running tween coroutine.</returns>
    protected Coroutine Tween(
        Vector3 from,
        Vector3 to,
        float duration,
        Action<Vector3> onUpdate,
        Ease ease = Ease.Linear,
        Action? onComplete = null,
        bool unscaled = false) =>
        StartCoroutine(TweenRoutine(
            t => onUpdate(Vector3.Lerp(from, to, t)), duration, ease, onComplete, unscaled));

    /// <summary>
    /// Tweens this entity's local <see cref="TransformComponent.Position"/> to <paramref name="to"/>.
    /// </summary>
    /// <param name="to">The target local position.</param>
    /// <param name="duration">The duration in seconds.</param>
    /// <param name="ease">The easing curve.</param>
    /// <param name="onComplete">Optional callback run when the tween finishes.</param>
    /// <param name="unscaled">When true, ignores pause and slow motion.</param>
    /// <returns>A handle to the running tween coroutine.</returns>
    protected Coroutine TweenPosition(
        Vector3 to, float duration, Ease ease = Ease.Linear, Action? onComplete = null, bool unscaled = false)
    {
        var transform = GetComponent<TransformComponent>();
        return Tween(transform.Position, to, duration, v => transform.Position = v, ease, onComplete, unscaled);
    }

    /// <summary>
    /// Tweens this entity's <see cref="TransformComponent.Scale"/> to <paramref name="to"/>.
    /// </summary>
    /// <param name="to">The target scale.</param>
    /// <param name="duration">The duration in seconds.</param>
    /// <param name="ease">The easing curve.</param>
    /// <param name="onComplete">Optional callback run when the tween finishes.</param>
    /// <param name="unscaled">When true, ignores pause and slow motion.</param>
    /// <returns>A handle to the running tween coroutine.</returns>
    protected Coroutine TweenScale(
        Vector3 to, float duration, Ease ease = Ease.Linear, Action? onComplete = null, bool unscaled = false)
    {
        var transform = GetComponent<TransformComponent>();
        return Tween(transform.Scale, to, duration, v => transform.Scale = v, ease, onComplete, unscaled);
    }

    /// <summary>
    /// Tweens this entity's <see cref="TransformComponent.Rotation"/> (Euler degrees) to
    /// <paramref name="to"/>, interpolating each axis linearly.
    /// </summary>
    /// <param name="to">The target Euler rotation in degrees.</param>
    /// <param name="duration">The duration in seconds.</param>
    /// <param name="ease">The easing curve.</param>
    /// <param name="onComplete">Optional callback run when the tween finishes.</param>
    /// <param name="unscaled">When true, ignores pause and slow motion.</param>
    /// <returns>A handle to the running tween coroutine.</returns>
    protected Coroutine TweenRotation(
        Vector3 to, float duration, Ease ease = Ease.Linear, Action? onComplete = null, bool unscaled = false)
    {
        var transform = GetComponent<TransformComponent>();
        return Tween(transform.Rotation, to, duration, v => transform.Rotation = v, ease, onComplete, unscaled);
    }

    // Shared tween body: drives normalized eased progress from 0 to 1 over the duration, applying it via
    // apply, then snaps to exactly 1 at the end so the target is hit precisely regardless of frame timing.
    private static IEnumerator TweenRoutine(
        Action<float> apply, float duration, Ease ease, Action? onComplete, bool unscaled)
    {
        if (duration <= 0.0f)
        {
            apply(1.0f);
            onComplete?.Invoke();
            yield break;
        }

        float elapsed = 0.0f;
        while (elapsed < duration)
        {
            apply(Easing.Evaluate(ease, elapsed / duration));
            yield return null;
            elapsed += unscaled ? Time.UnscaledDeltaTime : Time.DeltaTime;
        }

        apply(1.0f);
        onComplete?.Invoke();
    }

    /// <summary>
    /// Called once, on the first frame after the script is attached.
    /// </summary>
    public virtual void OnCreate()
    {
    }

    /// <summary>
    /// Called every frame while the script's scene is active.
    /// </summary>
    /// <param name="deltaTime">The elapsed time in seconds since the previous frame.</param>
    public virtual void OnUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Called when the entity is destroyed or its scene is left (only if <see cref="OnCreate"/> ran).
    /// </summary>
    public virtual void OnDestroy()
    {
    }

    /// <summary>
    /// Called every frame to render UI with ImGui.
    /// </summary>
    public virtual void OnImGuiRender()
    {
    }

    /// <summary>
    /// Called on the physics step when this entity's (non-trigger) collider begins touching another.
    /// </summary>
    /// <param name="collision">The contact, including the other entity and a contact normal/point.</param>
    public virtual void OnCollisionEnter(Physics.Collision collision)
    {
    }

    /// <summary>
    /// Called on each physics step while this entity's collider keeps touching another.
    /// </summary>
    /// <param name="collision">The contact, including the other entity and a contact normal/point.</param>
    public virtual void OnCollisionStay(Physics.Collision collision)
    {
    }

    /// <summary>
    /// Called on the physics step when this entity's collider stops touching another.
    /// </summary>
    /// <param name="collision">The contact that ended (the other entity; normal/point are the last known values).</param>
    public virtual void OnCollisionExit(Physics.Collision collision)
    {
    }

    /// <summary>
    /// Called on the physics step when another collider enters this entity's trigger volume (or this
    /// entity's collider enters another's trigger). Triggers overlap without a physical response.
    /// </summary>
    /// <param name="other">The other entity in the overlap.</param>
    public virtual void OnTriggerEnter(Entity other)
    {
    }

    /// <summary>
    /// Called on each physics step while another collider stays within this entity's trigger overlap.
    /// </summary>
    /// <param name="other">The other entity in the overlap.</param>
    public virtual void OnTriggerStay(Entity other)
    {
    }

    /// <summary>
    /// Called on the physics step when a collider leaves this entity's trigger overlap.
    /// </summary>
    /// <param name="other">The other entity that left the overlap.</param>
    public virtual void OnTriggerExit(Entity other)
    {
    }
}
