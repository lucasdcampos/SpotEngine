# Scripting

In Spot, an entity is just a list of **components**. The engine's components (Transform, Mesh Renderer,
Camera, Physics Body, …) describe *what an entity is*; your own components describe *what it does*. A
script is simply a component you write.

## The idea

If you've used Unity, a Spot component is the direct analog of a `MonoBehaviour`; if you've used s&box,
it is its `Component`. You write a class that derives from `Component`, override a few lifecycle hooks,
and attach it to an entity. The engine runs it automatically while its scene is active.

```csharp
using System.Numerics;
using Spot.Engine.Scenes;

namespace MyGame;

public class PlayerMovement : Component
{
    public float Speed = 5.0f;

    public override void OnUpdate(float deltaTime)
    {
        var transform = GetComponent<TransformComponent>();
        transform.Position += new Vector3(Speed * deltaTime, 0, 0);
    }
}
```

From inside a component you have its `Entity` (and so every other component on it) and its `Scene`, so
you can read and modify components, create entities, find others by name or tag, or destroy them.

### Creating one in the editor

Select an entity, click **Add Component**, then **New Component…**, and name it. The editor writes
`Assets/Scripts/<Name>.cs` (a class deriving from `Component`, with its `.meta` sidecar), opens it in
your code editor, and attaches it to the entity right away — it shows as *waiting to compile* until the
scripts reload, then turns into the real component. Typing a name that matches nothing in the search box
and pressing `Enter` does the same in one step. Existing components are listed under **Scripts**, the
first group of the same menu (put `[ComponentMenu("Display Name", Category = "Gameplay")]` on a component to
give it a label and file it under another group), and dropping a script from the Asset Browser onto **Add Component** attaches it.

### In code

```csharp
Entity player = Scene.Instantiate("Player");
player.AddComponent<PlayerMovement>();                  // a new instance
player.AddComponent(new Health { Max = 150 });          // one you configured
```

An entity holds **one component of each type**; adding a second replaces the first.

## Finding components

`GetComponent<T>()`, `TryGetComponent<T>(out …)`, `HasComponent<T>()` and `RemoveComponent<T>()` accept a
concrete component type, a **base class or an interface**, so components that work together don't need to
know each other's concrete types:

```csharp
public interface IDamageable { void TakeDamage(float amount); }

if (hit.Entity.GetComponentInParent<IDamageable>() is { } target)
    target.TakeDamage(10);
```

- `entity.GetComponents<T>()` — every component on the entity assignable to `T`, in the order added.
- `entity.Components` — all of an entity's components, in order.
- `GetComponentInChildren<T>()` / `GetComponentInParent<T>()` — search down or up the hierarchy.
- `scene.GetComponents<T>()` — every component of `T` in the scene (disabled ones included).

The engine's own components are `sealed`; derive from `Component` (or from another component of yours).

## Lifecycle

A component hooks into these moments:

- **OnStart** — called once, on the first frame after the component is attached. Use it to initialize.
- **OnEnable** — called when the component becomes active and enabled: right after `OnStart` on the
  first activation, and again every time the entity or the component is re-enabled.
- **OnUpdate** — called every frame with the elapsed time. This is where most gameplay logic lives:
  reading input, moving the entity, checking game state.
- **OnFixedUpdate** — called once per physics step, *before* the simulation integrates, so logic that
  applies forces or moves bodies runs in step with the solver rather than at a frame-rate-dependent
  moment.
- **OnLateUpdate** — called after *every* component's `OnUpdate` has run this frame, so it observes their
  changes (a follow camera reads its target's already-moved position here).
- **OnDisable** — called when the component stops being active and enabled (the entity or the component
  was disabled), and once more before `OnDestroy`. Pairs with `OnEnable`.
- **OnValidate** — called in the **editor** when one of the component's serialized fields is changed in
  the inspector, so it can clamp or react to authored values. Never called at runtime.
- **OnDestroy** — called when the component is removed, its entity is destroyed, or its scene is left.
  Use it to clean up.
- **OnImGuiRender** — an optional per-frame hook for drawing immediate-mode UI.

Components also receive **physics callbacks** — collision enter/stay/exit for solid contacts, and
trigger enter/stay/exit for overlap volumes. See [Physics](physics.md).

Components run in the order they were added. Hooks run only in play mode (and in a running game); the
engine's own components have no hooks of their own — its systems drive them.

## Tunable fields

A component's `public` fields (and read/write properties) of supported types show up in the inspector and
are saved with the scene, so you can tune behavior per-entity without recompiling. Supported types are
`bool`, `int`, `float`, `string`, enums, `Vector2/3/4`, `string[]`, and **`Entity` references**.

An `Entity` field lets one component point at another entity — a spawner's spawn point, a camera's
target — by dragging that entity from the hierarchy onto the field. The reference is stored by the
target's **stable id** (the same identity scheme scripts and assets use), so it survives renaming and
reordering, and is re-resolved after the whole scene finishes loading (a reference to a deleted entity
is simply left unset rather than throwing).

## Coroutines, timers, and tweens

For behavior that plays out over time, components have built-in scheduling so you don't have to track
timers by hand:

- **Coroutines** — a method that runs across many frames, suspending itself with `yield`: wait one
  frame, wait for a number of seconds, wait until a condition is true, or run a nested coroutine.
- **Invoke** — run a callback once after a delay, or repeatedly on an interval.
- **Tweens** — smoothly interpolate a value (or an entity's position, rotation, or scale) from one
  value to another over a duration, with a choice of **easing** curves.

All of these run on the scaled game clock by default — so they pause and slow down with the game —
and stop automatically when the entity is destroyed or its scene is left.

## Fault isolation

If a component throws from any of its hooks, Spot logs it and **disables just that component** — it
won't run again, but the rest of the game keeps going. One broken component never crashes the engine and
never floods the log by throwing every frame. This lets you keep working while you track down the
problem, and is a core part of the engine's [resilience](introduction.md#a-note-on-resilience).

## Components in saved scenes

A scene stores an entity's components under `"Components"`, in order: each entry holds the class name,
the script's **stable guid** — the same guid+`.meta` identity the [asset pipeline](assets.md) uses —
its enabled state and its field values. The editor writes a `<script>.cs.meta` sidecar next to each
script it creates or attaches. When the scene loads, the engine resolves the guid back to the type (the
class name is the fallback), so **renaming a component class doesn't break the scenes that use it**, and
two classes with the same name in different namespaces don't collide.

A component whose type can't be found — a script that hasn't compiled yet, failed to build, or was
deleted — isn't dropped: its data is kept as is (in `MissingComponents`), shown in the inspector as
*waiting to compile* or *not found*, written back unchanged when the scene is saved, and turned back into
the real component as soon as the type is available.

## How components are resolved (registry + generator)

Your components live in your game's project, not the engine, so the engine has to find them at runtime. A
**source generator** (`Spot.ScriptGen`, wired into the generated project as an analyzer) scans your
project at build time and emits a reflection-free registry: for each concrete `Component` subclass it
records its guid (read from the `.cs.meta` sidecar), class name, type, and a construction factory, and
registers them with the engine as the assembly loads. Resolution consults this registry first — by guid,
then by name — and only falls back to scanning loaded assemblies when the registry misses. This keeps
resolution allocation-light and, crucially, **trimming/AOT-safe for the browser build**, while a
project built without the generator still works through the reflection fallback.

## Editing scripts without restarting (hot reload)

The editor loads your project's compiled scripts into a **reloadable** load context, so you can edit a
component, add a field, or add a whole new component and see it in the running editor without
restarting. Save your `.cs` file and the editor rebuilds the project and swaps in the new assembly;
auto-reload is on by default (toggle it under **Project ▸ Auto-Reload Scripts**), or trigger it manually
with **Project ▸ Reload Scripts** (`Ctrl+R`). Across a reload the editor turns your components back into
scene data, unloads the old assembly, and rebuilds them from the new one — so their authored field values
and entity references carry over, and the inspector immediately reflects new fields and components. A
build error aborts the swap and leaves the current components in place, and — like everything else — a
failure here logs and keeps the editor alive rather than crashing it. If an earlier load of your scripts can't be released after a
reload — something, such as a static field or an event handler outside your scripts, still references one of
their types — the console says so, since each such load stays in memory.

## Related

- [Entities & Components](entities-and-components.md) — the model components live in
- [Input](input.md) — reading keys and named actions from a component's update
- [Physics](physics.md) — the collision and trigger callbacks components receive
- [Scenes](scenes.md) — the lifecycle that drives components
