# Text & Fonts

Spot renders text in the game itself — on the screen (HUDs, menus) and in the world (floating labels,
damage numbers) — independently of the editor's ImGui fonts, so text works the same in a shipped build.

## How text is drawn

A **font** is a TrueType/OpenType file rasterized into a single **glyph atlas** texture. A fixed set of
code points — ASCII plus the Latin-1 supplement, which covers Western European text including Portuguese
accents — is baked once when the font loads, at one base pixel size; drawing at other sizes scales the
baked glyphs rather than re-rasterizing. Glyph coverage lives in the atlas's alpha channel with white RGB,
so text keeps clean, soft edges without a dark fringe.

Turning a string into quads is a pure **layout** step — measuring, word-wrapping to a width, and aligning
(left/center/right) — separate from any GPU work, so it is shared by both screen and world text and is
unit-tested directly. Text is drawn with alpha blending (not the sprite pass's alpha-test), which is what
keeps small glyph edges smooth.

## The built-in font

The engine embeds a default font (Inter), so in-game text renders with **zero setup** — you don't have to
supply a font to draw a HUD or a label. A game can override it everywhere by assigning its own font as the
UI's default, or per-widget/per-component.

## Screen text vs. world text

The same font and layout feed two places:

- **Screen text** is part of the runtime UI — a `Text` widget in the retained UI tree, drawn in the final
  screen-space pass. See [Runtime UI](ui.md).
- **World text** is scene content: add a **Text component** to an entity and it draws in the world at the
  entity's transform. By default it **billboards** (always faces the camera), which suits floating labels
  and damage numbers; turn billboarding off to lay the text on the entity's plane (for signs painted onto
  surfaces). Its size in the world is the font size times a world-scale factor. World text is drawn along
  with particles — blended, before post-processing — so it is tone-mapped and can be occluded by geometry
  like the rest of the scene.

## Fonts as assets

A `.ttf`/`.otf` dropped into a project's `Assets/` is a first-class asset: it gets a GUID and a `.meta`
sidecar, and it **cooks** to a compact `.sptfont` artifact referenced by `guid:` like any other asset, so
renaming or moving the source never breaks references. A shipped game loads the cooked font from its
content folder; the editor loads the source file directly. See [Assets](assets.md).

## Related

- [Runtime UI](ui.md) — the retained widget tree that draws screen text and interactive controls
- [Rendering](rendering.md) — where the text passes sit in the frame
- [Assets](assets.md) — how fonts are imported, cooked, and referenced
