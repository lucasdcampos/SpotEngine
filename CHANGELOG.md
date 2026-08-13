# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Work in progress toward **v0.2** ("Gameplay & Shipping"). The list below is provisional and
will be finalized when 0.2 is tagged.

### Added
- **Runtime UI system** — a code-driven, retained UI tree (`Spot.UI`) for building HUDs and
  menus in-game: `UIRoot`/`Panel`/`Image`/`Text`/`Button`/`Slider`/`Toggle`, screen anchoring,
  9-slice sprites, and pointer input (hover/press/click) with callbacks. Independent of the
  editor's ImGui authoring UI.
- **In-game text rendering** — TrueType fonts are rasterized to a dynamic glyph atlas and drawn
  as batched quads, working in shipped builds without ImGui.
- **World-space text** — a `Text` component for labels and floating numbers anchored in the
  scene, with optional camera billboarding.
- **Font assets** — `.ttf`/`.otf` now cook to a `.sptfont` artifact and load by guid reference
  like other cooked content.

### Changed
- **Application Startup** — generated `Program.cs` now initializes the engine using the factory method `SpotEngine.CreateApplication()`, simplifying the entry point and avoiding direct `Spot.Core` dependencies.

## [v0.1.0] - 2026-08-12

### Added
- Initial release of Spot Engine.
