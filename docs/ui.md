# Runtime UI

Spot has a **runtime UI** for building a game's own HUDs, menus and end screens — separate from the
editor's authoring UI, so the interface you build ships with the game. It is a **retained** tree: widgets
are created and kept, the engine lays them out, routes pointer input and draws them every frame.

You can build a UI two ways, and mix them freely:

- **In the editor** — author a **UI document** (`.sptui` asset) visually on a screen-space canvas, adding
  panels, buttons, text and images just like entities in a scene. See *Authoring in the editor* below.
- **In code** — reach a scene's UI root from a script's `UI` accessor and add widgets directly.

Both operate on the *same* widget tree, so editor-authored UI and code drive the identical types.

## The tree

Each scene owns one **UI root**. A script reaches it through its `UI` accessor and adds widgets to it; the
engine ticks the root every play-mode frame to route input and draws it as the final screen-space pass.
Scenes without any widgets pay nothing.

Every widget has an **anchored rectangle**: a point on its parent (the *anchor*, in 0..1) is paired with a
point on itself (the *pivot*), plus a pixel offset and size. This is what lets a widget pin to a corner or
center and stay there across resolutions — a health readout anchored to the bottom-left, a menu pinned to
the center. Widgets nest: children are positioned relative to their parent's resolved rectangle and drawn
on top of it.

Because the tree is **retained**, you build it once (typically in a script's create hook) and then just
change widget properties — a label's text, a bar's width — as the game runs; you don't rebuild it every
frame the way immediate-mode UI does.

## Scaling

The root maps UI coordinates to the screen in one of two modes: **constant pixel** (one UI unit is one
screen pixel) or **scale-with-height** (a reference height always fills the screen, so layouts stay
proportional across resolutions). Scale-with-height is the default, so a menu designed once looks right on
any window size.

## Widgets

The built-in widgets cover the common cases:

- **Panel** — a colored or sprite-backed container other widgets sit on.
- **Image** — a texture (a tint multiplies it).
- **Text** — a string in a font at a size, aligned and optionally wrapped. See [Text & Fonts](text.md).
- **Button** — a labeled background that tints on hover/press and raises a click callback.
- **Slider** — drag a handle to pick a value in a range, raising a value-changed callback.
- **Toggle** — a checkbox with a label that flips a boolean.

Panels, images and buttons can draw their background as a **nine-slice**: with a border set, the sprite's
corners stay fixed while its edges and center stretch, so a single rounded-rect or framed sprite scales to
any size without distorting its border.

## Authoring in the editor

A **UI document** is a `.sptui` asset — a saved widget tree with its scale settings. Create one from the
Asset Browser (*New UI Document*) and double-click it to open the authoring panels:

- **UI Canvas** — a screen-space surface, distinct from the 3D scene viewport, that renders the document
  with the real UI renderer (so it is WYSIWYG). Click a widget to select it; drag its body to move it and
  its handles to resize. Layout aids make assembly quick: a toggleable pixel **grid with snapping**,
  **smart alignment guides** that snap a widget's edges and center to its siblings and container (with pink
  guide lines), a live **size readout** while dragging, and **arrow-key nudging** (Shift nudges by one grid
  step). Hold **Alt** while dragging to bypass snapping. It is the UI analog of the scene viewport.
- **Hierarchy** — the same panel as the scene hierarchy, which shows the widget tree while you're working in
  the UI Canvas (and the scene's entities when you click back into a scene viewport): add widgets
  (Panel/Button/Text/Image/Slider/Toggle), delete, rename, duplicate (`Ctrl+D`), and drag-drop to reparent,
  mirroring the scene hierarchy's shortcuts.
- **Inspector** — the selected widget's properties: its anchored rectangle, colors, text, font and sprite
  slots. Editing is immediate on the canvas. Press `Ctrl+S` to save the document.

To show a document in a scene, add a **UI Canvas** component to an entity and point its *Document* slot at
the `.sptui` (or drag the asset onto the slot). When play mode starts, the document's widgets are
instantiated into the scene's UI. Give a widget a **name** in the inspector and reach it from a script by
that name to wire up behaviour:

```csharp
protected override void OnStart()
{
    UI.Find<Button>("Play")!.OnClick += () => SceneManager.Load("Level1");
    UI.Find<Toggle>("Mute")!.OnValueChanged += on => AudioManager.Muted = on;
}
```

Editor-authored documents store appearance and layout; **behaviour is wired in code** by looking widgets up
by name, so designers lay out the screen and scripts give it life.

## Input

Each frame the root hit-tests the top-most interactive widget under the pointer and dispatches press,
hold and release to it; pressing a widget **captures** it so a drag (a slider handle) keeps tracking even
past the widget's edges. Widgets fire their callbacks from here — a button's click, a slider's value
change. The root also exposes whether the pointer is currently over interactive UI, so game code can
ignore world clicks that land on a menu.

## Related

- [Text & Fonts](text.md) — how the `Text`, `Button` and `Toggle` labels are rendered
- [Scripting](scripting.md) — where you build and drive the UI from
- [Rendering](rendering.md) — the screen-space UI pass in the frame
- [The Sandbox Hub](sandbox-hub.md) — the menu, options and HUD built with this UI
