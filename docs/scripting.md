# Scripting

Components describe *what an entity is*; **scripts** describe *what an entity does*. A script is a
piece of custom behavior you write and attach to an entity.

## The idea

If you've used Unity, Spot's scripts are the direct analog of `MonoBehaviour`. You write a class that
derives from the engine's script base type (`EntityBehaviour`), override a few lifecycle hooks, and
attach it to an entity. The engine runs it automatically while its scene is active. From inside a
script you have access to its entity (and therefore its components) and its scene, so you can read
and modify components, create new entities, find others by name or tag, or destroy entities.

## Script lifecycle

A script hooks into a small set of moments:

- **Create** — called once, on the first frame after the script is attached. Use it to initialize.
- **Update** — called every frame with the elapsed time. This is where most gameplay logic lives:
  reading input, moving the entity, checking game state.
- **Destroy** — called when the entity is destroyed or its scene is left. Use it to clean up.
- **ImGui render** — an optional per-frame hook for drawing immediate-mode UI.

Scripts also receive **physics callbacks** — collision enter/stay/exit for solid contacts, and
trigger enter/stay/exit for overlap volumes. See [Physics](physics.md).

## Coroutines, timers, and tweens

For behavior that plays out over time, scripts have built-in scheduling so you don't have to track
timers by hand:

- **Coroutines** — a method that runs across many frames, suspending itself with `yield`: wait one
  frame, wait for a number of seconds, wait until a condition is true, or run a nested coroutine.
- **Invoke** — run a callback once after a delay, or repeatedly on an interval.
- **Tweens** — smoothly interpolate a value (or an entity's position, rotation, or scale) from one
  value to another over a duration, with a choice of **easing** curves.

All of these run on the scaled game clock by default — so they pause and slow down with the game —
and stop automatically when the entity is destroyed or its scene is left.

## Fault isolation

If a script throws from any of its hooks, Spot logs it and **disables just that script** — it won't
run again, but the rest of the game keeps going. One broken script never crashes the engine and
never floods the log by throwing every frame. This lets you keep working while you track down the
problem, and is a core part of the engine's [resilience](introduction.md#a-note-on-resilience).

## Scripts in saved scenes

When a scene is saved, each entity remembers its scripts **by type name**. When the scene is loaded,
the engine finds the matching script types in your game's code and reattaches them. This is why
gameplay scripts live in your game's project rather than in the engine itself.

## Related

- [Entities & Components](entities-and-components.md) — what scripts operate on
- [Physics](physics.md) — the collision and trigger callbacks scripts receive
- [Scenes](scenes.md) — the lifecycle that drives scripts
