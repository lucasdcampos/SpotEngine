# The Editor

The **editor** is a visual application for building your game. Instead of creating scenes and
entities purely in code, you assemble them interactively — placing entities, adjusting their
components, importing assets, and testing the result — then save them as scene files your game loads.

## What it's for

The editor is where day-to-day content work happens: laying out levels, wiring up cameras and
lights, tweaking transforms and materials, importing models and audio, and previewing how a scene
looks and plays before shipping it.

## The launcher

Opening the editor first shows a compact **launcher** for picking a project to work on:

- **New Project** — names a project and a location on disk, previews the folder it will create, and
  scaffolds it (folder, `Assets/`, `.sptproj`, and build files) before opening it.
- **Open Project** — browses for an existing `.sptproj` file.
- **Recent Projects** — a searchable list of the projects you've opened before. Each entry shows the
  project name, its path, when it was **last opened**, and the **engine version** that opened it.
  Click a card to open it; hover it for quick actions (open the containing folder, or remove it from
  the list), or right-click for the same menu.

The recent list is stored per-user and drops entries whose files no longer exist, so it stays
current on its own. Choosing a project shows a loading screen while the editor grows to its working
size and the scene is built.

## The workspace

The editor is organized into dockable panels you can rearrange and save into a layout:

- **Scene view** — the interactive viewport where you see and navigate your scene, with a free-fly
  editor camera and on-screen transform gizmos for moving, rotating, and scaling entities. Left-click an
  entity to select it: 2D sprites, 3D meshes (both procedural primitives and imported models), and the
  billboards of invisible entities (cameras, lights, sky) are all clickable, and clicking empty space
  clears the selection. With the viewport hovered, `W`/`E`/`R` switch the gizmo between move/rotate/scale
  and `F` frames the selected entity (double-clicking an entity in the hierarchy does the same). Hold
  `Ctrl` while dragging a gizmo to snap in increments (1 unit / 15° / 0.25×).
- **Hierarchy** — the list of entities in the current scene, including their parent/child structure.
  You create, delete, and reparent entities here. Select several at once with `Ctrl`+click (toggle one)
  and `Shift`+click (range), then delete, duplicate, reparent (drag any one of them), or reorder
  (**Move Up**/**Move Down**) the whole selection together. It is also the UI widget tree: clicking the
  **UI Canvas** switches it to the open document's widgets, and clicking a scene viewport switches it back
  to the scene's entities — automatically, following whichever view you're working in.
- **Inspector** — shows the components of the selected entity (or asset) and lets you edit their
  values. The inspector is generated from the components themselves, so custom components appear
  automatically. Asset reference fields (mesh, material, texture, ...) show a preview tile — an image
  thumbnail, or a live-rendered sphere for materials — and open a searchable, thumbnailed picker when
  clicked, so you can pick an asset without dragging. The picker lists the matching
  [built-in assets](assets.md#built-in-assets) first, then the project's files; a built-in mesh with custom
  parameters shows them next to its name (*Capsule (radius 0.3, height 1.7)*). Selecting a built-in asset
  shows a read-only preview with **Copy to Project** and **Copy Reference** buttons. A Mesh Renderer drawing
  a built-in shape lists the shape's parameters (size, radius, height, segments, rings) below its model slot:
  dragging one reshapes the mesh live, releasing it keeps the change as a single undo step, and **Reset
  Shape** restores the defaults. A 3D collider added to an entity with a mesh starts out fitted to it, and
  **Fit to Mesh** refits it at any time.
- **Console** — engine and game log output, plus a command line (Enter to submit). Rendered with the
  editor theme so it reads as a native panel; the standalone in-game console keeps its own overlay look.
  The `'` key brings this panel forward and puts the caret in the prompt, from anywhere in the editor —
  including the Game panel during play, where it also hands the cursor and input back to the editor
  (same as `Esc`), so what you type doesn't drive the game as well. Click the Game panel to take
  control again. The editor owns this window, so the engine does not also draw its floating overlay.
- **Asset browser** — the content in your project (scenes, models, textures, audio, prefabs), where
  you import and organize assets. Textures show their image, materials render a live sphere preview, and
  3D models render a live thumbnail (a neutral-shaded, auto-framed view of the geometry) so you can tell
  models apart at a glance without dropping them into a scene. Model thumbnails load in the background and
  are cached per folder. Select several files at once with `Ctrl`+click and `Shift`+click, then delete or
  drag the whole selection into a folder together. The project root starts with a read-only **Built-in**
  folder holding the engine's [built-in assets](assets.md#built-in-assets) — meshes, textures and materials —
  which you drag onto slots, the scene or the hierarchy like any other asset. They can't be renamed, moved
  or deleted; to customize one, copy it into the project: `Ctrl`+`D` (or **Copy to Project**) saves an
  editable copy into the last project folder you visited, dragging it onto a project folder saves it there,
  and `Ctrl`+`C` then `Ctrl`+`V` pastes a copy wherever you like. `Enter` shows the selected built-in in the
  Inspector.
- **UI Canvas** — a screen-space surface for authoring a game UI (`.sptui`) document, separate from the
  scene viewport; the shared **Hierarchy** panel shows its widgets while it is focused. See
  [Runtime UI](ui.md#authoring-in-the-editor).
- **Audio Mixer** (`Ctrl`+`M`) — the project's volume groups as a row of channel strips, one per bus:
  a fader with a live level meter, mute (**M**) and solo (**S**), and the bus it feeds. Drag a fader while
  the game is playing and you hear it immediately. Add a bus with **Ctrl**+**N** (or the toolbar), rename
  with **F2** or a double-click, remove with **Delete**, move between strips with **←**/**→**, nudge a level
  with **↑**/**↓**, and reset one to unity with **0**. Levels and routing are saved into the project; solo is
  an audition tool and is not. See [Audio](audio.md#the-mixer-buses-and-volume-groups).
- **History** (`Ctrl`+`H`) — the list of everything you have done, newest first, with a marker on the
  current state. Click any entry to jump straight to that point (undoing or redoing however many steps
  that takes); entries ahead of the marker are the redo branch and are dimmed rather than hidden. The
  footer shows how many actions are held and how much memory they use. See
  [Undo and history](#undo-and-history).
- **Project settings** — project-wide configuration such as the start scene.

The editor remembers your working session **per project**. When you reopen a project it restores the
scene, UI, and animator tabs you had open (and refocuses the one that was active), the panel visibility
from **View → Panels**, and each viewport's editor camera — so you continue exactly where you left off
rather than back at the project's start scene. Tabs whose files were deleted or renamed since are quietly
skipped. The session is saved on exit to `Library/editor_session.json` inside the project (an editor-only
cache, safe to delete or leave out of version control); a brand-new project with no saved session opens on
its start scene as before.

## Navigating the Scene view

In 3D mode, hold the **right mouse button** to fly: move the mouse to look around, `W`/`A`/`S`/`D` to move
on the view plane, `Q`/`E` to drop and rise, and hold `Shift` for 4x speed. The mouse wheel moves the
camera along its forward axis. In 2D mode, drag with the **middle** or **right** button to pan and use the
wheel to zoom.

While flying, the cursor is hidden and confined to the viewport: it is warped back to the centre whenever
it approaches a border, so it can never escape into another panel. Between those warps the look is driven
by the cursor's plain frame-to-frame movement, which is what keeps it smooth — recentring on *every* frame
instead would discard the motion the mouse made during that frame's update and render, making the camera
feel both sluggish and jittery.

The **Camera** button in the viewport toolbar opens sliders for **Look sensitivity** (a multiplier on the
base look rate) and **Fly speed** (world units per second), plus a **Reset to defaults** button. Both are
global to the editor and persist with the window layout in `editor_window.json`.

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

## Undo and history

Every change you make while authoring can be taken back. The editor keeps **one** history for the
whole application, so `Ctrl`+`Z` always undoes the last thing you did, whichever panel you did it in —
you never have to work out which tab "owns" the undo.

| Action | Shortcut |
|--------|----------|
| Undo | `Ctrl`+`Z` |
| Redo | `Ctrl`+`Shift`+`Z` or `Ctrl`+`Y` |
| Show the History panel | `Ctrl`+`H` |

The **Edit** menu names the operation rather than just saying "Undo", so you can see what is about to
be taken back — *Undo Set Intensity*, *Undo Move Cube* — and the History panel shows the whole
sequence.

**One edit is one entry.** A history entry is recorded when an edit *finishes*, not while it is
happening: dragging a slider for three seconds, scrubbing through a color picker, or dragging a gizmo
across the viewport each produce a single entry, so one `Ctrl`+`Z` takes the whole gesture back rather
than unwinding it a frame at a time. Bursts of arrow-key nudges collapse the same way.

**Typing is left alone.** While a text field has focus, `Ctrl`+`Z` goes to the field and edits the text
you are typing, exactly as you would expect; the scene is not rolled back under you. Pressing `Escape`
to abandon a field records nothing, because the value ended where it started.

**What is covered.** Entity and component edits, adding and removing components, creating, deleting,
duplicating, renaming, reparenting and reordering entities, gizmo moves, and — as those sites are
migrated — UI documents, materials, animator controllers, project settings and the mixer layout. The
editor also watches the scene for changes that no specific operation claimed, and records those too as
a single *Scene Change* entry. That means a change is never un-undoable: at worst the entry is coarse
and generically named. The History panel's footer reports how many of these generic entries a session
has produced, and the console logs a note when one happens.

**What is not covered:**

- **File operations in the Asset Browser.** Deleting, renaming, moving or importing an asset touches
  the disk, and undo does not reach outside the editor's own data. Those actions confirm before they
  destroy anything instead.
- **Play mode.** Changes made while the game is running are not recorded, because stopping play
  restores the scene to its pre-play state wholesale and discards them anyway. Your edit history from
  before you pressed Play is untouched and still there when you stop.
- **Closing a document.** Closing a scene or UI document discards its history entries (and any newer
  ones), since there would be nothing left to undo them into. The console says how many were dropped.

## Edit mode and play mode

The editor has two modes:

- **Edit mode** is where you build. Changes you make are to the scene you're authoring.
- **Play mode** runs your game directly inside the editor — scripts, physics, audio, and animation all
  tick in real time. Play starts instantly (no build step) and, when you stop, the scene is restored
  exactly as it was before you pressed Play, so testing never disturbs your work.

The **Game** view shows what the scene's primary camera sees during play. The Scene view stays open
alongside it, so you can fly around and inspect the live runtime state.

**Game panel input focus.** While in play mode the Game panel only receives keyboard and mouse input
when it is focused. Click inside the Game view to give it focus (the "Click to control" hint
disappears). From that point WASD, mouse-look, and any cursor lock the game requests all work as in
a standalone build. Press `Escape` to release focus and return the cursor to the editor — after that
you can fly the Scene camera or inspect entities without the game reacting to your input. The game's
simulation continues in the background regardless of focus.

Three controls sit centered in the menu bar:

| Button | Keyboard | Effect |
|--------|----------|--------|
| Play / Stop | — | Enters or exits play mode. On stop the scene is fully restored. |
| Pause / Resume | `Ctrl`+`P` | Freezes the simulation without discarding state; resume to continue. |
| Step | `Ctrl`+`Right` | Advances the simulation exactly one frame (only works while paused). |

While the simulation is running the scene-view gizmos and drag-drop are locked — edits belong in
edit mode. You can still save the scene file from play mode (`Ctrl`+`S`), which saves the
pre-play (authored) version since any runtime changes are ephemeral.

## Managing projects

From the editor you can create a new project, open an existing one, and produce a distributable build
of your game. It uses the same build tooling described in
[Projects & Building a Game](projects-and-building.md), running it in-process.

## Related

- [Scenes](scenes.md) — what the editor builds and saves
- [Entities & Components](entities-and-components.md) — what you edit in the hierarchy and inspector
- [Assets](assets.md) — importing and referencing content
- [Projects & Building a Game](projects-and-building.md) — turning your project into a shippable app
