# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Work in progress toward **v0.2** ("Gameplay & Shipping"). The list below is provisional and
will be finalized when 0.2 is tagged.

### Added
- **Audio in the browser (Web Audio)** — a `WebAudioBackend` implements the `IAudioBackend` seam over a
  single `AudioContext` via `[JSImport]` (module `spot-audio`), mirroring the desktop OpenAL backend.
  Buffers and voices use integer handle tables like the WebGL2 device; the one-shot `AudioBufferSourceNode`
  is recreated per play, with state tracking and offset-based pause/resume. Full spatial audio (`PannerNode`
  + `AudioContext` listener, distance attenuation) is supported. The `AudioContext` is unlocked on the first
  user gesture (autoplay policy); browsers without Web Audio degrade to silence. `BrowserHost` now installs
  `WebAudioBackend` instead of `SilentAudioBackend`.
- **World-space text in the browser** — `TextComponent` (floating damage numbers, labels) now renders in
  the browser. The rendering logic was extracted from `RenderSystem` into a new `TextRenderSystem` class
  (same pattern as `ParticleRenderSystem`) and called from `BrowserHost.RenderScene2D`.
- **Particle rendering in the browser** — `ParticleRenderer` and `ParticleRenderSystem` now work in the
  browser build. `IGraphicsDevice` gained `SetDepthWrite(bool)` (`gl.depthMask` on WebGL2), removing the
  last direct Silk.NET dependency from `ParticleRenderer`. `BrowserHost` initializes the particle renderer
  and calls `ParticleRenderSystem.Render` each frame alongside the 2D sprite pass.
- **`spot run browser`** — cooks assets, generates the WebAssembly project, stages content, and
  starts the SDK Kestrel dev server (`dotnet run`) for fast browser iteration without a full publish.
- **Browser target (WebAssembly + WebGL2, 2D MVP)** — the engine now multi-targets `net10.0` (desktop,
  Silk.NET) and `net10.0-browser`, sharing one neutral core. A WebGL2 `IGraphicsDevice` backend drives the
  canvas from C# over `[JSImport]`; a `BrowserHost` runs the `requestAnimationFrame` loop, translates DOM
  input into the existing `Input`/event pipeline, and fetches cooked content into an in-memory
  `IAssetProvider`. Platform seams were extracted so the core no longer binds to the desktop window,
  renderer, or audio: an `IAudioBackend` (OpenAL on desktop, Web Audio in-browser), a `Display`
  render-surface-size seam, a `SceneRenderer` callback (full 3D pipeline on desktop, slim 2D in-browser),
  and a GLSL → `#version 300 es` transpiler. `spot build browser` publishes a WebGL2 static site. The
  3D/post/shadow pipeline, ImGui, and the Assimp importer remain desktop-only.
- **2D physics backend** — real 2D rigid-body simulation on **Aether.Physics2D** (a managed Box2D
  descendant) behind an `IPhysics2D` seam, mirroring the 3D/Bepu design: mass, friction, restitution,
  rotation, collision/trigger callbacks, and `Scene.Raycast2D`. Adds a **Circle Collider 2D** and
  expands **Physics Body 2D** (mass, drag, friction, restitution, kinematic, freeze-rotation). Selected
  via `PhysicsSettings.Backend2D`/`Gravity2D`; falls back to the legacy AABB solver if it fails to
  initialize. The Sandbox gains a **Physics 2D** playground demo.
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
