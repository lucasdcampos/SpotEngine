# Runtime UI

Spot has a **runtime UI** for building a game's own HUDs, menus and end screens — separate from the
editor's authoring UI, so the interface you build ships with the game. It is a **code-only, retained**
tree: scripts create widgets and keep them, the engine lays them out, routes pointer input and draws them
every frame. There is no UI authoring or serialization in the editor in this version.

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
