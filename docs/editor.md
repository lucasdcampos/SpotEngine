# The Editor

The **editor** is a visual application for building your game. Instead of creating scenes and entities
purely in code, you assemble them interactively — placing entities, adjusting their components,
importing assets, and testing the result — then save them as scene files your game loads.

## What it's for

The editor is where day-to-day content work happens: laying out levels, wiring up cameras and lights,
tweaking transforms and materials, and previewing how a scene looks and plays before shipping it.

## The workspace

The editor is organized into dockable panels you can rearrange and save into a layout:

- **Scene view** — the interactive viewport where you see and navigate your scene. You can have
  several scenes open at once, each in its own tab.
- **Hierarchy** — the list of entities in the current scene, including their parent/child structure.
  You create, delete, and reparent entities here.
- **Inspector** — shows the components of the selected entity (or asset) and lets you edit their
  values.
- **Console** — engine and game log output.
- **Asset browser** — the files in your project, such as scenes, models, and textures.

## Edit mode and play mode

The editor has two modes:

- **Edit mode** is where you build. Changes you make are to the scene you're authoring.
- **Play mode** runs your game inside the editor so you can test it. When you stop, the scene is
  restored exactly as it was before you pressed play — anything that happened during play is
  discarded, so testing never disturbs your work.

## Managing projects

From the editor you can create a new project, open an existing one, and produce a distributable build
of your game. It uses the same build tooling described in
[Projects & Building a Game](projects-and-building.md).

## Related

- [Scenes](scenes.md) — what the editor builds and saves
- [Entities & Components](entities-and-components.md) — what you edit in the hierarchy and inspector
- [Projects & Building a Game](projects-and-building.md) — turning your project into a shippable app
