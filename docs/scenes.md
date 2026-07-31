# Scenes

A **scene** is the container for everything that exists at a given moment in your game: all of its
entities and their components. It's also a *screen* — a self-contained slice of your game such as a
main menu, a level, or a test area. A game is typically made of several scenes that you switch
between.

## Two roles

A scene plays two roles at once:

1. **A container** of entities and components. You populate a scene with entities, and the engine's
   systems (rendering, scripting, physics) operate on whatever the scene contains.
2. **A screen with a lifecycle.** A scene is told when it becomes active, updated every frame while
   it's active, asked to render, and told when it's being replaced. You can hook into these moments
   to set up and tear down your content.

## The scene lifecycle

While a scene is active, the engine drives it through a predictable sequence:

- **Enter** — the scene has just become active. This is where you create your entities and load
  resources.
- **Update** — called every frame, giving you the elapsed time since the last frame. This is where
  per-frame game logic runs, alongside scripts and physics.
- **Render** — the scene is drawn. Most scenes let the engine draw their entities automatically
  through a camera, but you can also render directly for full control.
- **Exit** — the scene is being replaced. Clean up resources here.

You don't call these yourself — the engine calls them at the right time.

## Switching scenes

Only one scene is active at a time. You request a switch to another scene, and the change takes
effect cleanly at the start of the next frame — the old scene exits and the new one enters. Because
the switch happens at a frame boundary rather than mid-frame, it's always safe to trigger one from
anywhere, such as a menu button or the end of a level.

## Saving and loading scenes

Scenes can be saved to and loaded from disk as `.sptscene` files. This is how the editor stores the
scenes you build, and how a shipped game loads its content at runtime. A scene file captures the
entities in the scene, their components, and their parent/child relationships.

## Related

- [Entities & Components](entities-and-components.md) — what lives inside a scene
- [The Editor](editor.md) — how scenes are built visually
