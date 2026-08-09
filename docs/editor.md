# The Editor

The **editor** is a visual application for building your game. Instead of creating scenes and
entities purely in code, you assemble them interactively — placing entities, adjusting their
components, importing assets, and testing the result — then save them as scene files your game loads.

## What it's for

The editor is where day-to-day content work happens: laying out levels, wiring up cameras and
lights, tweaking transforms and materials, importing models and audio, and previewing how a scene
looks and plays before shipping it.

## The workspace

The editor is organized into dockable panels you can rearrange and save into a layout:

- **Scene view** — the interactive viewport where you see and navigate your scene, with a free-fly
  editor camera and on-screen transform gizmos for moving, rotating, and scaling entities.
- **Hierarchy** — the list of entities in the current scene, including their parent/child structure.
  You create, delete, and reparent entities here.
- **Inspector** — shows the components of the selected entity (or asset) and lets you edit their
  values. The inspector is generated from the components themselves, so custom components appear
  automatically.
- **Console** — engine and game log output.
- **Asset browser** — the content in your project (scenes, models, textures, audio, prefabs), where
  you import and organize assets.
- **Project settings** — project-wide configuration such as the start scene.

## Edit mode and play mode

The editor has two modes:

- **Edit mode** is where you build. Changes you make are to the scene you're authoring.
- **Play mode** runs your game inside the editor so you can test it — scripts, physics, and audio all
  come alive. When you stop, the scene is restored exactly as it was before you pressed play, so
  anything that happened during play is discarded and testing never disturbs your work.

## Managing projects

From the editor you can create a new project, open an existing one, and produce a distributable build
of your game. It uses the same build tooling described in
[Projects & Building a Game](projects-and-building.md), running it in-process.

## Related

- [Scenes](scenes.md) — what the editor builds and saves
- [Entities & Components](entities-and-components.md) — what you edit in the hierarchy and inspector
- [Assets](assets.md) — importing and referencing content
- [Projects & Building a Game](projects-and-building.md) — turning your project into a shippable app
