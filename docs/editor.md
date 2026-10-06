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

- **New Project** (`Ctrl+N`) — names a project and a location on disk, previews the folder it will
  create, and scaffolds it (folder, `Assets/`, `.sptproj`, and build files) before opening it. It
  won't create into a folder that already has files in it (that would overwrite its project file and
  start scene); it suggests a free name instead (`MyProject2`, ...). `Enter` creates, `Esc` cancels.
- **Open Project** (`Ctrl+O`) — browses for an existing `.sptproj` file.
- **Projects** — the projects you've opened before, as a **grid** of cards or a compact **list**
  (toggle on the header; the launcher remembers your choice). Each card shows a **thumbnail** of the
  project, its name, its folder (shortened in the middle, full path in the tooltip), when it was
  **last opened**, and the **engine version** that last opened it, in amber when it differs from this
  editor's. Search by name or path (`Ctrl+F`), and sort by last opened or by name.

Click a card to select it and double-click (or press `Enter`) to open it. The `...` button, or a
right-click, opens its menu: **Open**, **Show in Explorer** (Finder on macOS, the containing folder
elsewhere), **Copy Path**, and **Remove from List**, which only forgets the entry and leaves the
files alone. From the keyboard, the arrow keys, `Home` and `End` move the selection, `Delete` removes
it from the list, `Ctrl+C` copies its path, and `Esc` clears the search, then the selection.

A project's thumbnail is a snapshot of the scene viewport, taken whenever you save the active scene or
close the editor and stored as `Library/thumbnail.png` inside the project (the `Library` folder holds
editor-only caches and stays out of version control). Projects without one yet show a placeholder,
tinted per project so each keeps a recognizable color.

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
  `Ctrl` while dragging a gizmo to snap in increments (1 unit / 15° / 0.25×). An infinite grid marks the
  ground plane (y = 0 in 3D, the XY plane in 2D): its spacing follows the zoom in powers of ten — finer near
  the camera, coarser in the distance, every tenth line stronger — and its lines through the origin are the
  world axes in the theme's X/Y/Z colors, with the Y axis rising from the origin in 3D. Geometry in front of
  the grid hides it, but a surface lying on the ground plane (a floor at y = 0) keeps showing the grid on top,
  and the grid runs all the way to the camera however low it flies. It is also where the game
  runs: Play turns the viewport into the game, and `F8` switches between the game camera and the editor
  camera (see [Edit mode and play mode](#edit-mode-and-play-mode)). A floating toolbar in its top-left
  corner holds two light, translucent clusters: the transform tools (move `W`, rotate `E`, scale `R`), then
  the view — a **2D** toggle, **colliders**, **fullbright** and **wireframe** overlay toggles, and the
  camera settings. Each is a compact icon that lights up in the accent while selected or on and names
  itself (and its shortcut) in a tooltip; clicking the toolbar never picks or deselects in the scene
  behind it.
- **Hierarchy** — the list of entities in the current scene, including their parent/child structure.
  You create, delete, and reparent entities here. Select several at once with `Ctrl`+click (toggle one)
  and `Shift`+click (range), then delete, duplicate, reparent (drag any one of them), or reorder
  (**Move Up**/**Move Down**) the whole selection together. It is also the UI widget tree: clicking the
  **UI Canvas** switches it to the open document's widgets, and clicking a scene viewport switches it back
  to the scene's entities — automatically, following whichever view you're working in. Rows are uniform
  full-width bands, thin guide lines connect each parent to its children, and each entity's icon is tinted
  by kind (mesh, light, camera, particles, ...); entities without a specific visual kind use a neutral cube
  icon. Disabled entities are dimmed and prefab instances tinted.
- **Inspector** — shows the components of the selected entity (or asset) and lets you edit their
  values. The inspector is generated from the components themselves, so custom components appear
  automatically. Each component is a card with a title strip (click it to fold the card; **⋮** removes the
  component); your own components follow the engine's, in the order you added them. **Add Component** opens a
  searchable menu: **New Component…** at the top, then every component grouped by category (your own under
  **Scripts** first, then Rendering, Environment, Effects, Physics, Physics 2D, Audio, Animation, UI and
  Network), each with its icon; groups fold, a search opens every group that matches, and `Enter` adds the
  only match. **New Component…** writes a new script deriving from `Component`, opens it and attaches it (see
  [Scripting](scripting.md#creating-one-in-the-editor)); typing a name that matches nothing and pressing
  `Enter` creates it in one step, and dropping a script on the button attaches it. A component whose script is
  still compiling, or is missing, shows as a card that keeps its values until it resolves. Labels sit in a column about a third of the panel wide, so every value lines up; a label too
  long for it is cut with an ellipsis and shown whole on hover. Vector fields put a colored X/Y/Z cap on
  each number box — click the cap to reset that axis. Asset reference fields (mesh, material, texture, ...) show a preview tile — an image
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
  **Clear** (`Ctrl+L` while the panel is focused) removes all retained entries without clearing command
  history. Click the **Trace** (bug), **Info** (information circle), **Warning** (triangle) and **Error**
  (crossed circle) icon buttons to show or hide each type. Each button includes its count; enabled
  filters have a tinted background and border, while hidden types are dimmed. Hover for the type name,
  count and current state. Counts cover the retained output, including hidden entries. The compact toolbar
  places **Search messages** (`Ctrl+F`) beside the level icons, filling the remaining width (wrapping
  in narrow docks). Search combines with the type filters and matches text without case sensitivity;
  `Esc` clears the search. The panel keeps the latest 500 entries.
  Click a log to select it, `Ctrl+click` to add/remove individual entries,
  or `Shift+click` to select a visible range (`Ctrl+Shift+click` adds the range to the selection).
  With the log list focused, `Ctrl+C` copies selected entries in their displayed order, `Ctrl+A`
  selects all visible entries, and `Esc` clears the selection. Each multiline message is one row.
  Changing filters removes hidden entries from the selection; incoming logs keep existing selections
  until those entries leave the 500-entry buffer. Right-click an entry to select it (preserving an
  existing multi-selection) and **Copy selected**; the output menu also offers **Copy all**,
  **Copy filtered**, **Select all visible**, **Clear selection**, and **Clear**.
  The `'` key brings this panel forward and puts the caret in the prompt, from anywhere in the editor —
  including while the game has the controls during play, where it also hands the cursor and input back to
  the editor (same as `Esc`), so what you type doesn't drive the game as well. Click the game view to take
  control again. The editor owns this window, so the engine does not also draw its floating overlay.
- **Asset browser** — the content in your project (scenes, models, textures, audio, prefabs), where
  you import and organize assets. Textures show their image, materials render a live sphere preview, and
  3D models render a live thumbnail (a neutral-shaded, auto-framed view of the geometry) so you can tell
  models apart at a glance without dropping them into a scene. Previews sit directly on the tile, with no
  backdrop. Model thumbnails load in the background and are cached per folder. Everything else gets a
  painted icon: files are a paper page with a colored type badge (**C#** for scripts, **UI** for UI
  documents, a waveform for audio, a state graph for animator controllers, the extension for other files),
  scenes a small 3D viewport, prefabs a blue cube, and folders show papers when they hold anything. Under
  each file's name, a colored line and a caption give its type (C# Script, Scene, Texture, Model,
  Material, ...), so a material sphere and a sphere model are easy to tell apart. Files sit on a soft card;
  folders have none until hovered or selected. Select several files at once with `Ctrl`+click and `Shift`+click, then delete or
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

Saved camera positions, orientations, projection modes and zoom levels apply before the first viewport
render, without clicking or moving the camera. Without a saved pose, the 3D camera starts at `(6, 4, 10)`
looking toward the origin, above and away from the grid axes; the 2D view starts centered on the origin.

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

While the cursor is hidden for flying, entity icons and transform gizmo handles do not react to mouse
hover or show entity tooltips. Hover feedback resumes when you release the right mouse button.

The gear button at the end of the viewport toolbar opens sliders for **Look sensitivity** (a multiplier on
the base look rate) and **Fly speed** (world units per second), plus a **Reset to defaults** button. Both
are global to the editor and persist with the window layout in `editor_window.json`.

## Look and feel

The default **Spot Dark** theme is built from a few close layers of neutral gray — the menu bar and tab
strips darkest, then inset regions (console output, asset grid), docked panels, component cards and, on
top, menus and popups — with one calm blue accent kept for what matters: the selection, focus, and
anything switched on (the active tool, an enabled overlay, a paused game). Docked panels are separated by
thin dark seams that light up in the accent when you hover or drag them to resize. The panel that has
keyboard focus shows an accent line along the top of its tab; the other tabs in a strip recede until
hovered. Everything that floats over a viewport — the toolbar, the camera readout, hints and drop labels,
the camera preview — shares the same light, translucent overlay so the scene stays visible around it.

**View → Theme** switches between the built-in themes (Spot Dark, Spot Light, Nord, Cherry, All Black).
Themes are a semantic palette plus spacing and rounding metrics (`EditorTheme`, `EditorPalette`,
`EditorStyleMetrics` in `Spot.DebugUI`), so a custom theme only has to fill in meaningful colors.

When writing a panel, build its rows with the `EditorGui` helpers and keep every ImGui push/pop pair
(`PushID`/`PopID`, `TreeNode`/`TreePop`, `Begin`/`End`) balanced on every path. The native ImGui library
ships with its asserts compiled out, so a mismatch is not reported: one extra `PopID` corrupts memory and
the editor closes a minute later, with no message. `EditorGuiIdStackTests` checks that each row helper
leaves the ID stack as it found it.

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

## Adding assets to a scene

You never have to create an empty entity and add components by hand to use an asset: **drag it from the
asset browser** into the scene and the editor builds the finished entity for you.

| Asset | Becomes |
|-------|---------|
| Prefab (`.sptprefab`) | the prefab's entity tree, marked as an instance of it |
| Model (FBX, OBJ, glTF, ...) or built-in mesh | the model's entity hierarchy with its materials applied (see below) |
| Image (PNG, JPG, ...) or built-in texture | a **Sprite**, scaled to the picture's proportions |
| Audio clip (WAV, OGG) | an **Audio Source** playing it |
| UI document (`.sptui`) | a **UI Canvas** showing it |

- **Onto the scene view**, the new entity lands where the cursor points. In 3D that is the first surface
  under the cursor (a sprite, or the bounds of a mesh you are looking at from outside) or the ground plane
  (y = 0), whichever is nearer; looking at the sky or at something far away, it lands a short way in front
  of the camera. In 2D it is the cursor's spot on the z = 0 plane, keeping the asset's own depth. While you
  drag, a ring marks the landing spot (lying in the ground plane, so its size shows the distance) with a
  label naming what will be created. Assets dropped on a scene tab go into that scene, which becomes the
  active one.
- **Onto the hierarchy**, dropping on an entity adds the new entity as its child; dropping on empty space
  adds it at the root, at the asset's own position.
- **Right-click → Add to Scene** in the asset browser does the same as dropping on the hierarchy's empty
  space.

Dragging a multi-selection adds every asset in it that stands for an entity (other files are skipped).
The new entities are selected, so `W`/`E`/`R` and `F` act on them straight away, and `Ctrl`+`Z` takes the
whole drop back.

**Materials** (`.sptmat` or built-in) are applied rather than added: drag one onto a mesh to restyle it.

- **Onto the scene view**, it goes to the mesh under the cursor: that mesh's bounds are outlined while you
  drag, with a label naming the material and the mesh (or saying there is no mesh there). For an imported
  model that is the one part you point at, since each part is its own mesh. As with placing, meshes whose
  bounds contain the camera are passed over, so from inside a room you paint what you point at, not the room.
- **Onto an entity in the hierarchy**, it goes to that entity's mesh or, for an entity without one (such as
  a model's root), to every mesh beneath it. Entities with no mesh anywhere below don't take the drop.

Each drop is one undo step, however many meshes it painted.

### Importing models

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

There is no separate Game panel: **the scene becomes the game**. Pressing Play switches the active
scene's viewport to the scene's game camera (its primary camera) and marks its tab with a play icon. The
game gets the controls straight away, so WASD, mouse-look and any cursor lock the game requests work as in
a standalone build. The game sees the viewport as its screen: its UI, its mouse position and the screen size
it reads are the viewport's, so hovering and clicking its widgets — or anything a script picks under the
pointer — line up with the picture. If the scene has no camera that would render, the viewport says so.

**Game camera and editor camera (`F8`).** As in Unreal's *eject*/*possess*, `F8` switches the playing
viewport between the two cameras:

- **Eject** (game camera → editor camera): the game keeps running but stops receiving input, the cursor is
  freed, and the editor camera starts at the game camera's view. Fly around, click entities, and inspect or
  tweak them in the Inspector while the simulation runs; the viewport shows an *Editor camera* hint.
  Anything you change is discarded on Stop, like every play-mode change.
- **Possess** (editor camera → game camera): the viewport shows the game again and hands it the controls.

`Esc` gives the cursor and input back to the editor without leaving the game camera; the viewport then
shows *Click to control*, and clicking it hands the controls back to the game. The game's simulation
continues regardless of who has the controls. The game's screen-space UI is hidden while the viewport
uses the editor camera and reappears when you press `F8` to return to the game camera. Releasing input
with `Escape` keeps the UI visible, as does the game camera preview. Stopping play returns the viewport
to the editor camera and discards everything the game built while it ran — its entities, and beside them its UI and its custom render
passes — so nothing of it is left drawing over the scene you edit.

In edit mode `F8` previews the active scene through its game camera, without running anything; press it
again to return to the editor camera. While a viewport shows the game camera its gizmos, picking and
editor icons are off, since they work in the editor camera's space.

Four controls sit centered in the menu bar:

| Button | Keyboard | Effect |
|--------|----------|--------|
| Play / Stop | — | Enters or exits play mode. On stop the scene is fully restored. |
| Pause / Resume | `Ctrl`+`P` | Freezes the simulation without discarding state; resume to continue. |
| Step | `Ctrl`+`Right` | Advances the simulation exactly one frame (only works while paused). |
| Game / editor camera | `F8` | Switches the viewport between the game camera (gamepad icon, highlighted) and the editor camera. |

You can still save the scene file from play mode (`Ctrl`+`S`), which saves the
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
