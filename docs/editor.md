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
- **Console** — engine and game log output, plus a command line (Enter to submit). Rendered with the
  editor theme so it reads as a native panel; the standalone in-game console keeps its own overlay look.
- **Asset browser** — the content in your project (scenes, models, textures, audio, prefabs), where
  you import and organize assets.
- **Project settings** — project-wide configuration such as the start scene.

## The menu bar

Along the top, the menu bar groups project, edit, view, and help actions, with the play/stop control
centered in it. Under **Help → About** is a dialog that identifies the build and gathers useful
reference material in one place:

- **About** — a short overview of the engine, a list of feature highlights, and quick links to the
  project's GitHub repository, documentation, and issue tracker.
- **System** — the host environment (engine version, .NET runtime, operating system, architecture,
  and the active GPU and OpenGL version). **Copy to clipboard** puts these details on the clipboard,
  formatted for pasting into a bug report.
- **Credits** — the open-source libraries Spot is built on, with their licenses, plus the copyright
  and license notice.

## Importing models into a scene

To place a model (FBX, OBJ, glTF, ...) in your scene, **drag it from the asset browser** onto the
scene view or into the hierarchy — or right-click it and choose **Add to Scene (with materials)**.
Dropping onto an entity in the hierarchy adds the model as a child of that entity; dropping onto empty
space or the viewport adds it at the root.

The import does two things automatically:

- **Rebuilds the hierarchy.** The model's node tree becomes an entity hierarchy — one entity per node,
  each with its own transform, and each mesh part as its own renderer. You can then move, hide, or
  restyle individual parts.
- **Applies the materials.** The model's materials (base color and base texture, including textures
  embedded in the file) are extracted into a `<Model>_Materials` folder next to the source and assigned
  to the matching parts, so the model shows up textured without any manual wiring.

Right-clicking a model also still offers **Extract Materials (Embedded)**, which only writes the
embedded textures and materials out to the folder without adding anything to the scene.

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
