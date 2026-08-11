using System.Collections;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// Per-script bookkeeping for coroutines and delayed invocations. Created lazily by an
/// <see cref="EntityBehaviour"/> the first time it schedules work, and ticked once per frame by the
/// <see cref="ScriptSystem"/> after the owner's <see cref="EntityBehaviour.OnUpdate"/>. A coroutine or
/// invoke callback that throws is stopped and logged (never crashing the engine or faulting the whole
/// script), honoring the engine's never-crash directive.
/// </summary>
internal sealed class ScriptScheduler
{
    private readonly EntityBehaviour _owner;
    private readonly List<Coroutine> _coroutines = new();
    private readonly List<ScheduledInvoke> _invokes = new();

    internal ScriptScheduler(EntityBehaviour owner) => _owner = owner;

    /// <summary>Registers a coroutine. Its first step runs on the next scheduler tick, not immediately.</summary>
    internal Coroutine StartCoroutine(IEnumerator routine)
    {
        var coroutine = new Coroutine(routine);
        _coroutines.Add(coroutine);
        return coroutine;
    }

    /// <summary>Stops a coroutine started on this script; a no-op if it already finished or is unknown.</summary>
    internal void StopCoroutine(Coroutine coroutine)
    {
        if (_coroutines.Contains(coroutine))
        {
            coroutine.Stopped = true;
        }
    }

    /// <summary>Stops every coroutine currently running on this script.</summary>
    internal void StopAllCoroutines()
    {
        foreach (Coroutine coroutine in _coroutines)
        {
            coroutine.Stopped = true;
        }
    }

    /// <summary>Schedules <paramref name="action"/> to run once after <paramref name="delay"/> scaled seconds.</summary>
    internal void Invoke(Action action, float delay) =>
        _invokes.Add(new ScheduledInvoke(action, MathF.Max(0.0f, delay), 0.0f));

    /// <summary>
    /// Schedules <paramref name="action"/> to run after <paramref name="delay"/> scaled seconds and then
    /// repeatedly every <paramref name="interval"/> seconds until cancelled.
    /// </summary>
    internal void InvokeRepeating(Action action, float delay, float interval) =>
        _invokes.Add(new ScheduledInvoke(action, MathF.Max(0.0f, delay), MathF.Max(0.0001f, interval)));

    /// <summary>Cancels every pending invocation on this script.</summary>
    internal void CancelInvoke()
    {
        foreach (ScheduledInvoke invoke in _invokes)
        {
            invoke.Cancelled = true;
        }
    }

    /// <summary>Cancels every pending invocation whose callback is <paramref name="action"/>.</summary>
    internal void CancelInvoke(Action action)
    {
        foreach (ScheduledInvoke invoke in _invokes)
        {
            if (invoke.Action == action)
            {
                invoke.Cancelled = true;
            }
        }
    }

    /// <summary>Reports whether any invocation is still pending on this script.</summary>
    internal bool IsInvoking()
    {
        foreach (ScheduledInvoke invoke in _invokes)
        {
            if (!invoke.Cancelled)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Advances all coroutines and pending invocations by one frame.</summary>
    internal void Tick(float scaledDelta, float unscaledDelta)
    {
        TickCoroutines(scaledDelta, unscaledDelta);
        TickInvokes(scaledDelta);
    }

    private void TickCoroutines(float scaledDelta, float unscaledDelta)
    {
        if (_coroutines.Count == 0)
        {
            return;
        }

        // Snapshot the count so coroutines started during this tick (by a resuming routine or an invoke
        // callback) take their first step next frame instead of running twice.
        int count = _coroutines.Count;
        for (int i = 0; i < count; i++)
        {
            Coroutine coroutine = _coroutines[i];
            if (coroutine.Stopped || coroutine.Finished)
            {
                continue;
            }

            try
            {
                if (!Advance(coroutine, scaledDelta, unscaledDelta))
                {
                    coroutine.Finished = true;
                }
            }
            catch (Exception ex)
            {
                coroutine.Stopped = true;
                Log.CoreError("Coroutine on script '{0}' threw and was stopped. {1}", _owner.GetType().Name, ex);
            }
        }

        _coroutines.RemoveAll(static c => c.Finished || c.Stopped);
    }

    // Steps a single coroutine forward until it hits a real suspension point (a yield instruction, a
    // one-frame wait, or completion). Returns true while the coroutine is still running.
    private static bool Advance(Coroutine coroutine, float scaledDelta, float unscaledDelta)
    {
        if (coroutine.Wait is not null)
        {
            if (coroutine.Wait.Tick(scaledDelta, unscaledDelta))
            {
                return true;
            }

            coroutine.Wait = null;
        }

        while (coroutine.Stack.Count > 0)
        {
            IEnumerator top = coroutine.Stack.Peek();
            if (!top.MoveNext())
            {
                // This routine finished; pop it and let the parent (if any) resume this same frame.
                coroutine.Stack.Pop();
                continue;
            }

            switch (top.Current)
            {
                case YieldInstruction instruction:
                    coroutine.Wait = instruction;
                    return true;
                case IEnumerator nested:
                    coroutine.Stack.Push(nested);
                    continue;
                default:
                    // Null (wait one frame) or any unrecognized yield value: resume next frame.
                    return true;
            }
        }

        return false;
    }

    private void TickInvokes(float scaledDelta)
    {
        if (_invokes.Count == 0)
        {
            return;
        }

        int count = _invokes.Count;
        for (int i = 0; i < count; i++)
        {
            ScheduledInvoke invoke = _invokes[i];
            if (invoke.Cancelled)
            {
                continue;
            }

            invoke.TimeLeft -= scaledDelta;
            if (invoke.TimeLeft > 0.0f)
            {
                continue;
            }

            try
            {
                invoke.Action();
            }
            catch (Exception ex)
            {
                invoke.Cancelled = true;
                Log.CoreError("Invoke callback on script '{0}' threw and was cancelled. {1}", _owner.GetType().Name, ex);
                continue;
            }

            if (invoke.Interval > 0.0f && !invoke.Cancelled)
            {
                // Advance from the fire time (not from zero) so a repeating invoke keeps steady cadence.
                invoke.TimeLeft += invoke.Interval;
            }
            else
            {
                invoke.Cancelled = true;
            }
        }

        _invokes.RemoveAll(static i => i.Cancelled);
    }

    private sealed class ScheduledInvoke
    {
        public ScheduledInvoke(Action action, float delay, float interval)
        {
            Action = action;
            TimeLeft = delay;
            Interval = interval;
        }

        public Action Action { get; }

        public float TimeLeft { get; set; }

        public float Interval { get; }

        public bool Cancelled { get; set; }
    }
}
