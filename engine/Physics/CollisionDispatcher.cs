using System.Collections.Generic;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// Turns the flat list of overlapping pairs a physics backend reports each step into the enter/stay/exit
/// script callbacks (<see cref="EntityBehaviour.OnCollisionEnter"/>, <see cref="EntityBehaviour.OnTriggerEnter"/>,
/// and their counterparts). One instance lives per scene for the duration of a play session, remembering the
/// previous step's pairs so it can diff them against the current step. Every callback is delivered through the
/// script system's guard, so a throwing handler is quarantined rather than crashing the step.
/// </summary>
internal sealed class CollisionDispatcher
{
    private Dictionary<PairId, ContactPair> _previous = new();
    private Dictionary<PairId, ContactPair> _current = new();

    /// <summary>
    /// Diffs <paramref name="contacts"/> against the previous step and raises the corresponding callbacks:
    /// pairs new this step fire Enter, pairs present in both fire Stay, and pairs gone this step fire Exit.
    /// </summary>
    public void Dispatch(IReadOnlyList<ContactPair> contacts)
    {
        _current.Clear();
        foreach (ContactPair pair in contacts)
        {
            if (!pair.A.IsValid || !pair.B.IsValid) continue;
            // Last write wins if a pair somehow appears twice in a step; either instance is equivalent.
            _current[new PairId(pair.A.Id, pair.B.Id)] = pair;
        }

        foreach ((PairId id, ContactPair pair) in _current)
        {
            bool continuing = _previous.ContainsKey(id);
            if (pair.IsTrigger)
            {
                DispatchTrigger(pair, continuing ? Phase.Stay : Phase.Enter);
            }
            else
            {
                DispatchCollision(pair, continuing ? Phase.Stay : Phase.Enter);
            }
        }

        foreach ((PairId id, ContactPair pair) in _previous)
        {
            if (_current.ContainsKey(id)) continue;
            if (pair.IsTrigger)
            {
                DispatchTrigger(pair, Phase.Exit);
            }
            else
            {
                DispatchCollision(pair, Phase.Exit);
            }
        }

        // Swap the buffers so this step's pairs become next step's baseline without reallocating.
        (_previous, _current) = (_current, _previous);
    }

    /// <summary>Forgets all remembered pairs, so a fresh session does not fire spurious exits.</summary>
    public void Reset()
    {
        _previous.Clear();
        _current.Clear();
    }

    private static void DispatchCollision(ContactPair pair, Phase phase)
    {
        // The stored normal points from B toward A; each side receives it oriented toward itself.
        var toA = new Collision(pair.B, pair.Normal, pair.Point);
        var toB = new Collision(pair.A, -pair.Normal, pair.Point);
        Deliver(pair.A, phase, "collision", s => Call(s, phase, toA));
        Deliver(pair.B, phase, "collision", s => Call(s, phase, toB));
    }

    private static void DispatchTrigger(ContactPair pair, Phase phase)
    {
        Deliver(pair.A, phase, "trigger", s => Call(s, phase, pair.B, trigger: true));
        Deliver(pair.B, phase, "trigger", s => Call(s, phase, pair.A, trigger: true));
    }

    private static void Deliver(Entity entity, Phase phase, string kind, System.Action<EntityBehaviour> call) =>
        ScriptSystem.ForEachLiveScript(entity, kind + ":" + phase, call);

    private static void Call(EntityBehaviour script, Phase phase, Collision collision)
    {
        switch (phase)
        {
            case Phase.Enter: script.OnCollisionEnter(collision); break;
            case Phase.Stay: script.OnCollisionStay(collision); break;
            case Phase.Exit: script.OnCollisionExit(collision); break;
        }
    }

    private static void Call(EntityBehaviour script, Phase phase, Entity other, bool trigger)
    {
        switch (phase)
        {
            case Phase.Enter: script.OnTriggerEnter(other); break;
            case Phase.Stay: script.OnTriggerStay(other); break;
            case Phase.Exit: script.OnTriggerExit(other); break;
        }
    }

    private enum Phase
    {
        Enter,
        Stay,
        Exit,
    }

    // An unordered pair of entity ids: (a, b) and (b, a) are the same contact, so it keys the diff
    // consistently regardless of the order the backend reports the two collidables in.
    private readonly struct PairId : System.IEquatable<PairId>
    {
        private readonly int _low;
        private readonly int _high;

        public PairId(int a, int b)
        {
            (_low, _high) = a <= b ? (a, b) : (b, a);
        }

        public bool Equals(PairId other) => _low == other._low && _high == other._high;

        public override bool Equals(object? obj) => obj is PairId other && Equals(other);

        public override int GetHashCode() => System.HashCode.Combine(_low, _high);
    }
}
