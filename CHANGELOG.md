# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Work in progress toward **v0.2** ("Gameplay & Shipping"). The list below is provisional and
will be finalized when 0.2 is tagged.

### Performance
- Frustum culling: off-screen 3D meshes are skipped in both the main and shadow passes
- GPU instancing: repeated rigid meshes sharing a material draw in one instanced call each
- Point lights now scale to many per scene (UBO-backed, up from a fixed four)
- Clustered forward lighting (froxel grid) for many point lights, off by default (`RenderSettings.ClusteredLighting`)
- Fixed a severe editor stall from per-frame framebuffer reallocation
- Editor secondary views render only when visible
- VSync is now a runtime setting, plus frame-time instrumentation
- 3D draw calls no longer re-upload scene-constant state per mesh
- Redundant texture binds skipped in the 3D mesh pass
- Hierarchy active-state is memoized
- Editor per-frame overhead cut
- Animation sampling no longer re-runs a bone-name regex per channel each frame

### Added
- Editor restores your last session per project on reopen: open scene/UI/animator tabs (and the active one), panel visibility, and each viewport's camera
- Multi-selection in the editor Hierarchy and Asset Browser (Ctrl+click, Shift+click) to delete, reorder, reparent, duplicate, or move several items at once
- Editor is now cross-platform with native file dialog support for Linux and macOS
- Added macOS target (osx-x64) to the standalone project builder
- Added Spot engine icon to the Editor window and executable
- Editor UI authoring: create and edit `.sptui` UI documents on a screen-space canvas (UI Canvas + UI Hierarchy panels), attach them with the new UI Canvas component, and wire behaviour by looking widgets up by name
- UI Canvas layout aids: pixel grid with snapping, smart alignment guides (snap to sibling edges/centers), arrow-key nudging, and a live size readout (Alt bypasses snapping)
- UI Canvas navigation: middle-mouse panning, scroll zooming, configurable screen bounds, and dimmed out-of-bounds area
- Appended '(UI)' to UI document tabs in the editor to differentiate them from Scene tabs
- Added Delete key shortcut to remove states and transitions in the Animator Controller editor
- 3D model thumbnails in the Asset Browser
- Animator Controllers (animation state machines)
- Simplified `AnimatorComponent`
- Rename-safe script identity + reflection-free resolution
- More script lifecycle hooks
- Entity reference fields on scripts
- Script hot reload in the editor
- 3D in the browser (shared render pipeline)
- Audio in the browser (Web Audio)
- World-space text in the browser
- Particle rendering in the browser
- `spot run browser` command
- Browser target (WebAssembly + WebGL2, 2D MVP)
- 2D physics backend
- Runtime UI system
- In-game text rendering
- World-space text
- Font assets
- Setup scripts

### Changed
- Inspector asset picker now shows preview tiles for every entry (image thumbnails and live material sphere previews) and a cleaner row layout; material reference slots render a preview instead of a flat glyph
- Application Startup factory method initialization
- Standardized the codebase: added an `.editorconfig` and a CI workflow (build + test on push/PR), centralized common build settings, and brought the Sandbox under warnings-as-errors
- Decomposed two oversized files with no behaviour change: the `Renderer3D` shaders moved to a partial, and `Scene` split into entity-store, hierarchy-cache, and physics collaborators

### Fixed
- Play/Run no longer litter the project root: cooked `Content/` and `game.manifest` are staged into `Build/` beside the game
- Project Settings start-scene change now takes effect on the next Play (manifest is always regenerated)
- Fixed transition selection not working in the Animator Controller editor
- Fixed bidirectional transitions overlapping into a single line in the Animator Controller editor
- Clicking a 3D mesh in the viewport now selects its entity (primitives were unpickable; only imported models' pivots hit)
- Blank window until resize

## [v0.1.0] - 2026-08-12

### Added
- Initial release of Spot Engine.
