using System.Collections;

namespace Spot.Scenes;

/// <summary>
/// A handle to a running coroutine, returned by <see cref="EntityBehaviour.StartCoroutine"/>. Hold it
/// to stop the coroutine later with <see cref="EntityBehaviour.StopCoroutine"/> or to poll
/// <see cref="IsRunning"/>. Coroutines are owned by the script that started them, so they stop
/// automatically when the entity is destroyed or its scene is left.
/// </summary>
public sealed class Coroutine
{
    // A stack of enumerators supports nested coroutines: when a routine yields another IEnumerator,
    // the nested one is pushed and runs to completion before its parent (below it) resumes.
    internal Stack<IEnumerator> Stack { get; } = new();

    /// <summary>The instruction the coroutine is currently suspended on, if any.</summary>
    internal YieldInstruction? Wait { get; set; }

    /// <summary>Set once the coroutine's body runs to completion.</summary>
    internal bool Finished { get; set; }

    /// <summary>Set when the coroutine is stopped early (by the user or by a thrown exception).</summary>
    internal bool Stopped { get; set; }

    internal Coroutine(IEnumerator routine) => Stack.Push(routine);

    /// <summary>
    /// Gets a value indicating whether the coroutine is still active (neither finished nor stopped).
    /// </summary>
    public bool IsRunning => !Finished && !Stopped;
}
