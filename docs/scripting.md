# Scripting

Components describe *what an entity is*; **scripts** describe *what an entity does*. A script is a
piece of custom behavior you write and attach to an entity.

## The idea

If you've used Unity, Spot's scripts are the direct analog of `MonoBehaviour`. You write a class that
derives from the engine's script base type, override a few lifecycle methods, and attach it to an
entity. The engine then runs it automatically while its scene is active.

## Script lifecycle

A script hooks into a small set of moments:

- **Create** — called once, the first frame after the script starts running. Use it to initialize.
- **Update** — called every frame, with the elapsed time since the last frame. This is where most
  gameplay logic lives: reading input, moving the entity, checking game state.
- **Destroy** — called when the entity is destroyed or its scene is left. Use it to clean up.

From inside a script you have access to its entity (and therefore its components), and to the scene,
so you can read and modify components, create new entities, or destroy existing ones.

## Fault isolation

If a script throws an error, Spot logs it and **disables just that script** — it won't run again, but
the rest of the game keeps going. One broken script never crashes the engine and never floods the log
by throwing every frame. This lets you keep working while you track down the problem.

## Scripts in saved scenes

When a scene is saved, each entity remembers its scripts by name. When the scene is loaded, the engine
finds the matching script types in your game's code and reattaches them. This is why gameplay scripts
live in your game's project rather than in the engine itself.

## Related

- [Entities & Components](entities-and-components.md) — what scripts operate on
- [Scenes](scenes.md) — the lifecycle that drives scripts
