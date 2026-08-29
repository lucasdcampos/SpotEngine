# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Work in progress toward **v0.2** ("Gameplay & Shipping"). The list below is provisional and
will be finalized when 0.2 is tagged.

### Performance
- Fixed a severe editor stall from per-frame framebuffer reallocation
- Editor secondary views render only when visible
- VSync is now a runtime setting, plus frame-time instrumentation
- 3D draw calls no longer re-upload scene-constant state per mesh
- Redundant texture binds skipped in the 3D mesh pass
- Hierarchy active-state is memoized
- Editor per-frame overhead cut

### Added
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
- Application Startup factory method initialization

### Fixed
- Blank window until resize

## [v0.1.0] - 2026-08-12

### Added
- Initial release of Spot Engine.
