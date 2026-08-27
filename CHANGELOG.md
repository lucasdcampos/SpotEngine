# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Work in progress toward **v0.2** ("Gameplay & Shipping"). The list below is provisional and
will be finalized when 0.2 is tagged.

### Performance

Ongoing pass to cut per-frame cost across the engine and editor. Each step is measured against a baseline
captured with VSync off.

- **VSync is now a runtime setting, plus frame-time instrumentation** — `RenderSettings.VSync` (default
  on) can be toggled live with the new `vsync` console command; turning it off uncaps the frame rate so the
  engine's true frame time becomes measurable instead of pinned to the monitor's refresh. A lightweight
  `FrameStats` records the real (unclamped) frame delta each frame; the editor viewport HUD now shows `ms`
  next to FPS, and a new `stats` command prints frame time / FPS / VSync state. See
  [Rendering](docs/rendering.md#measuring-performance).
- **3D draw calls no longer re-upload scene-constant state per mesh** — the view-projection, camera
  position, and all light/shadow uniforms are identical for every mesh in a frame, yet were re-sent on
  every draw. They now upload once per shader per scene (GL keeps a program's uniforms across bind
  switches), consecutive same-shader draws skip a redundant program bind, and the point-light uniform
  names are pre-built so lighting no longer allocates strings on the render path. Rendered output is
  unchanged.

### Added
- **3D model thumbnails in the Asset Browser** — model assets (`.fbx`, `.obj`, `.gltf`, `.glb`, `.dae`,
  `.ply`, `.stl`) now render a live neutral-shaded preview auto-framed on the model's bounds, instead of a
  generic cube glyph, so models are distinguishable at a glance without adding them to a scene. Previews
  load in the background (non-blocking), are throttled and cached per folder, and fall back to the glyph
  while loading or on error. `Mesh`/`Model` now expose local `Bounds`/`LocalBounds`.
- **Animator Controllers (animation state machines)** — a new reusable `.sptcontroller` asset wires clips
  into a Unity-style state machine: states (each carrying its clip name and the file it comes from, so
  clips retarget by name across matching models), Float/Int/Bool/Trigger parameters, and transitions gated
  by conditions (with optional exit time and Any-State edges). Authored in a new node-graph editor window
  (create/open from the Asset Browser) with a parameter list and state/transition inspector; a state's clip
  is chosen from a project-wide picker or by drag-drop. Transitions switch clips instantly for now
  (crossfade blending is future work). See [Animation](docs/animation.md).
- **Simplified `AnimatorComponent`** — the animator now carries only an optional *Controller* reference
  (its model is a hidden, auto-set clip/skeleton source). The `DefaultClip`, `PlayOnStart`, `Speed`,
  `Loop`, and `ExtraClipPaths` fields were removed: playback is driven either by a controller or entirely
  from a script that owns its clips via `Play(clipName)` / `Play(clip)` (plus `Stop`/`Pause`/`Resume`,
  `IsPlaying`, `CurrentClip`, `ClipNames`), and the controller's parameter API (`SetFloat`/`SetBool`/… and
  `CurrentState`).
- **Rename-safe script identity + reflection-free resolution** — scripts are now referenced by a stable
  guid (the same guid+`.meta` sidecar identity the asset pipeline uses) instead of only a class name, so
  renaming a script class no longer breaks the scenes that use it and same-named classes in different
  namespaces no longer collide. A new source generator (`Spot.ScriptGen`, wired into the generated
  project/browser build as an analyzer) emits a reflection-free `IScriptProvider` — guid/name → type +
  construction factory — that `ScriptResolver` consults before falling back to an assembly scan, making
  script resolution trimming/AOT-safe for the browser. Older scenes (class-name only) still load and are
  upgraded to a guid on their next save.
- **More script lifecycle hooks** — `EntityBehaviour` gained `OnEnable`/`OnDisable` (fired on enabled-state
  transitions, with `OnDisable` also preceding `OnDestroy`), `OnFixedUpdate` (run once per physics step,
  before the simulation, via a new `SystemOrder.FixedUpdate` slot), `OnLateUpdate` (after every script's
  `OnUpdate` this frame), and `OnValidate` (fired by the inspector when a serialized field changes). All run
  inside the existing per-script quarantine, so a throwing hook is disabled rather than crashing the engine.
- **Entity reference fields on scripts** — a `public Entity` script field is now inspector-editable
  (drag an entity from the hierarchy) and serialized. Every entity carries a stable id (written in its scene
  `Tag` block); references store that id and are re-resolved in a fixup pass after the scene loads, so they
  survive renames/reordering and a reference to a deleted entity is left unset instead of throwing. Prefab
  instances get fresh ids with internal references remapped per-instance.
- **Script hot reload in the editor** — the editor loads project scripts into a collectible load context and
  can rebuild and swap them without restarting: edit/add a script or field, and auto-reload (or **Project ▸
  Reload Scripts**, `Ctrl+R`) recompiles and hot-swaps the assembly, preserving each live script's authored
  field values and entity references. A build error aborts the swap and leaves the running scripts in place.
- **3D in the browser (shared render pipeline)** — the browser now runs the **same `RenderSystem`** as the
  desktop instead of a slim 2D-only path: meshes, materials, lighting (directional/point/ambient), shadows,
  and the procedural skybox render through the WebGL2 device. Rather than fork a browser renderer, the
  `IGraphicsDevice` seam grew a **render-target layer** (framebuffers, `RGBA16F`/depth texture formats,
  depth-compare sampling, wireframe as a no-op) implemented on both OpenGL and WebGL2; `Renderer3D`,
  `DepthFramebuffer`, and `RenderSystem` were made backend-neutral. HDR/bloom/post now sit behind an
  `IScenePostProcessor` seam (desktop installs `DesktopScenePostProcessor`; the browser renders straight to
  the screen for now). Cooked `.sptmesh` models load synchronously in the single-threaded WASM runtime.
  Mouse-look works too: a browser `ICursorController` maps `Input.CursorLocked` to the Pointer Lock API
  (engaged on the first canvas click), feeding relative pointer movement so first-person cameras rotate and
  the cursor hides as on desktop.
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
  and a GLSL → `#version 300 es` transpiler. `spot build browser` publishes a WebGL2 static site. (The 3D
  pipeline was later made backend-neutral and now runs in the browser too; HDR/bloom/post, ImGui, and the
  Assimp importer remain desktop-only.)
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
- **Setup scripts** — a `scripts/` folder with cross-platform `build`, `setup`, and `uninstall`
  scripts (`.bat` for Windows, `.sh` for Linux). `setup` compiles the whole solution in Release
  and puts the `spot` CLI on PATH (user PATH on Windows; a `~/.local/bin` symlink on Linux).

### Changed
- **Application Startup** — generated `Program.cs` now initializes the engine using the factory method `SpotEngine.CreateApplication()`, simplifying the entry point and avoiding direct `Spot.Core` dependencies.

### Fixed
- **Blank window until resize** — on startup some setups reported a 0-sized framebuffer with the engine's
  manual render loop, which collapsed both the GL viewport and ImGui's `DisplayFramebufferScale` to 0 — so the
  window (launcher, editor, and running games alike) showed only the clear color until it was manually
  resized. The engine now drives the drawable size to the renderer and the ImGui controller at startup (with a
  first-frame safety net and a scale fallback), so the first frame renders correctly.

## [v0.1.0] - 2026-08-12

### Added
- Initial release of Spot Engine.
