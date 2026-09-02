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

A script hooks into a set of moments (the same names Unity uses, prefixed with `On`):

- **OnCreate** — called once, on the first frame after the script is attached. Use it to initialize.
- **OnEnable** — called when the script becomes active and enabled: right after `OnCreate` on the
  first activation, and again every time the entity or its script component is re-enabled.
- **OnUpdate** — called every frame with the elapsed time. This is where most gameplay logic lives:
  reading input, moving the entity, checking game state.
- **OnFixedUpdate** — called once per physics step, *before* the simulation integrates, so logic that
  applies forces or moves bodies runs in step with the solver rather than at a frame-rate-dependent
  moment.
- **OnLateUpdate** — called after *every* script's `OnUpdate` has run this frame, so it observes their
  changes (a follow camera reads its target's already-moved position here).
- **OnDisable** — called when the script stops being active and enabled (the entity or its component
  was disabled), and once more before `OnDestroy`. Pairs with `OnEnable`.
- **OnValidate** — called in the **editor** when one of the script's serialized fields is changed in
  the inspector, so the script can clamp or react to authored values. Never called at runtime.
- **OnDestroy** — called when the entity is destroyed or its scene is left. Use it to clean up.
- **OnImGuiRender** — an optional per-frame hook for drawing immediate-mode UI.

Scripts also receive **physics callbacks** — collision enter/stay/exit for solid contacts, and
trigger enter/stay/exit for overlap volumes. See [Physics](physics.md).

## Tunable fields

A script's `public` fields (and read/write properties) of supported types show up in the inspector and
are saved with the scene, so you can tune behavior per-entity without recompiling. Supported types are
`bool`, `int`, `float`, `string`, enums, `Vector2/3/4`, `string[]`, and **`Entity` references**.

An `Entity` field lets one script point at another entity — a spawner's spawn point, a camera's
target — by dragging that entity from the hierarchy onto the field. The reference is stored by the
target's **stable id** (the same identity scheme scripts and assets use), so it survives renaming and
reordering, and is re-resolved after the whole scene finishes loading (a reference to a deleted entity
is simply left unset rather than throwing).

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

When a scene is saved, each entity remembers its scripts by a **stable guid** — the same
guid+`.meta` identity the [asset pipeline](assets.md) uses. The editor writes a `<script>.cs.meta`
sidecar next to each script the first time you attach it, and the scene stores that guid (plus the
class name as a human-readable fallback). When the scene loads, the engine resolves the guid back to
the script type and reattaches it. Because the reference is the guid, **renaming a script class no
longer breaks the scenes that use it**, and two scripts with the same class name in different
namespaces no longer collide. Older scenes that stored only a class name still load, and are upgraded
to a guid the next time they're saved.

## How scripts are resolved (registry + generator)

Gameplay scripts live in your game's project, not the engine, so the engine has to find them at
runtime. A **source generator** (`Spot.ScriptGen`, wired into the generated project as an analyzer)
scans your scripts at build time and emits a reflection-free registry: for each script it records its
guid (read from the `.cs.meta` sidecar), class name, type, and a construction factory, and registers
them with the engine as the assembly loads. Script resolution consults this registry first — by guid,
then by name — and only falls back to scanning loaded assemblies when the registry misses. This keeps
resolution allocation-light and, crucially, **trimming/AOT-safe for the browser build**, while a
project built without the generator still works through the reflection fallback.

## Editing scripts without restarting (hot reload)

The editor loads your project's compiled scripts into a **reloadable** load context, so you can edit a
script, add a field, or add a whole new script and see it in the running editor without restarting.
Save your `.cs` file and the editor rebuilds the project and swaps in the new assembly; auto-reload is
on by default (toggle it under **Project ▸ Auto-Reload Scripts**), or trigger it manually with
**Project ▸ Reload Scripts** (`Ctrl+R`). Across a reload the editor preserves every live script's
authored field values and entity references, and the inspector immediately reflects new fields and
scripts. A build error aborts the swap and leaves the current scripts running, and — like everything
else — a failure here logs and keeps the editor alive rather than crashing it.

## Related

- [Entities & Components](entities-and-components.md) — what scripts operate on
- [Input](input.md) — reading keys and named actions from a script's update
- [Physics](physics.md) — the collision and trigger callbacks scripts receive
- [Scenes](scenes.md) — the lifecycle that drives scripts
