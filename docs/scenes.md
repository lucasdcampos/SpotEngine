# Scenes

A **scene** is the container for everything that exists at a given moment in your game: all of its
entities and their components. It's also a *screen* — a self-contained slice of your game such as a
main menu, a level, or a test area. A game is typically made of several scenes you switch between.

## Two roles

A scene plays two roles at once:

1. **A container** of entities and components. You populate a scene with entities, and the engine's
   systems (rendering, scripting, physics, audio) operate on whatever the scene contains.
2. **A screen with a lifecycle.** A scene is told when it becomes active, updated every frame while
   it's active, asked to render, and told when it's being replaced. You can hook into these moments
   to set up and tear down your content.

## The scene lifecycle

While a scene is active, the engine drives it through a predictable sequence:

- **Enter** — the scene has just become active. Create your entities and load resources here.
- **Update** — called every frame with the elapsed time. Per-frame game logic runs here, alongside
  the scene's systems (scripts, physics, audio).
- **Render** — the scene is drawn through its primary camera. Most scenes let the engine draw their
  entities automatically, but you can also render directly for full control.
- **ImGui render** — a chance to draw immediate-mode UI on top of the scene each frame.
- **Exit** — the scene is being replaced. Clean up resources here.

You don't call these yourself — the engine calls them at the right time.

## Querying entities

A scene lets you find the entities you care about: look up a **view** of every entity that has a
given component (or combination of components) — this is how systems iterate — or find an entity by
**name** or by **tag**. Views return a snapshot, so it's always safe to create or destroy entities
while iterating.

## Custom systems

Each scene runs an ordered set of **systems** every play-mode frame — the built-in physics, animation,
particle, audio, and script systems. You can extend that set with your own: implement `ISystem` (or wrap
a callback in `DelegateSystem`) and register it with `Scene.RegisterSystem`, choosing where it runs
relative to the built-ins with the `SystemOrder` slots. This is the home for simulation that spans many
entities and doesn't belong on a single script. A system that throws is logged once and skipped, in
keeping with the engine's never-crash rule. See [Architecture](architecture.md#systems).

## Switching scenes

Only one scene is active at a time. You request a switch to another scene, and the change takes
effect cleanly at the start of the next frame — the old scene exits and the new one enters. Because
the switch happens at a frame boundary rather than mid-frame, it's always safe to trigger from
anywhere, such as a menu button or the end of a level.

**Persistent objects.** By default a scene switch destroys everything in the old scene. An entity
can be marked to survive the switch (the engine's `DontDestroyOnLoad`); its whole subtree carries
over into the next scene with live component and script state intact. This is how you keep things
like a music player, a score manager, or the player across level changes.

## Saving and loading scenes

Scenes can be saved to and loaded from disk as `.sptscene` files. This is how the editor stores the
scenes you build and how a shipped game loads its content at runtime. A scene file captures the
entities, their components, their parent/child relationships, and the scripts attached to each
entity (by type name). Loading catches and logs bad or missing data rather than throwing, so a
broken scene never crashes the engine.

Parent/child relationships are stored as nested `Children` arrays, so a scene file can be as deeply
nested as its entity hierarchy. Deep hierarchies are common — a rigged model dragged in from an FBX
brings in a full bone tree — so scene and prefab JSON is read and written with a raised nesting limit
rather than the default cap of 64.

## Related

- [Entities & Components](entities-and-components.md) — what lives inside a scene
- [Architecture](architecture.md) — how the loop drives the lifecycle
- [The Editor](editor.md) — how scenes are built visually
