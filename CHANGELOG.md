# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Work in progress toward **v0.3**. The list below is provisional and will be finalized when 0.3 is tagged.

### Added
- **Spot is split into three levels**, each its own assembly with dependencies only pointing down: `Spot.Framework.Core` (level 1: window, events, raw input, graphics device and resources, minimal 2D batch, PCM audio device — no loop, no decoders), `Spot.Framework` (level 2: file loading and decoders, text, models, the animation mechanism, audio mixer, input actions) plus the optional `Spot.Framework.Assimp` module, and `Spot.Engine` (level 3). A game can reference just the framework and drive its own loop; architecture tests pin the layering
- **Documentation of the levels** (`docs/levels.md`): what each level holds, the mechanism-versus-policy rule, escape hatches, a level-1 loop, and upgrading a 0.2 project
- **Custom render passes in the engine**: `IRenderPass` (or `DelegateRenderPass`) registered with `Scene.AddRenderPass` draws at a fixed point of the scene pipeline — `BeforeOpaque`, `AfterOpaque`, `AfterTransparent` (inside the HDR capture), `AfterPostProcess` or `Overlay` — with the camera, the bound target and viewport in a `RenderContext`, using any framework renderer or the raw device; a faulty pass is logged once and skipped
- **Procedural primitives with parameters** in the framework: `PrimitiveSpec` describes a cube, sphere, capsule, cylinder, cone, plane or quad and its size/resolution, with a canonical text form (`Capsule?radius=0.3&height=1.7`); `PrimitiveMeshes` generates the CPU geometry, and `PrimitiveModelFactory.Get` shares one cached model per spec. `primitive:` references accept the same parameters, and `ModelImporter.RegisterProvider` lets other prefixes serve generated or built-in models
- **Built-in assets** in the engine: `BuiltinAssets` catalogs procedural meshes, utility textures (white, black, flat normal, checker, grid, soft dot) and materials (default, checker, grid) under stable `builtin:` references such as `builtin:Material/Grid`; mesh references take parameters (`builtin:Mesh/Capsule?radius=0.3&height=1.7`). Built-ins are generated in code (nothing to cook or ship), shared, rebuilt for a new graphics device, and never disposed by the materials that use them
- `ProceduralImages` (solid color, flat normal, checkerboard, prototyping grid, soft dot), `Image.EncodePng`/`SavePng`, and `MeshExport.ToObj`
- `samples/HelloEngine`: a minimal engine project — a lit, shadowed scene with post-processing, physics, particles, a UI document, scripts and a custom render pass, using no binary assets — with tests that its scenes, UI and scripts keep loading
- **Samples** (`samples/`): `HelloQuad` (core only: your own loop, 2D quads, raw input), `HelloTriangle` (a triangle straight through the graphics device), `Hello2D` (sprites, shapes, text, sound and input actions from code) and `Hello3D` (camera, lit primitives, instancing, and an optional source model loaded with Assimp and animated with `Skeleton`); each takes `--frames N` to exit on its own
- `BillboardBatch` (blended camera-facing or oriented quads in 3D, alpha or additive, depth-tested without depth writes, with a soft-dot default texture) and `FullscreenPass` (run a fragment shader over the viewport, optionally sampling a source texture); the engine's particle and world-text pass is now built on `BillboardBatch`
- `AudioManager.Init()` opens the platform's default audio device (OpenAL on desktop, Web Audio in the browser), plus `AudioManager.IsAvailable` and `CreateDefaultBackend()`
- **Code-only 3D in the framework**: `Camera3D`, `BasicRenderer3D` (meshes and models with one directional light and ambient, unlit or textured, instanced, skinned, or through your own shader via `BasicMaterial.Shader`) and `Skeleton`, which samples an `AnimationClip` into a pose and computes skinning palettes with no scene or entities
- `Time.Tick()` measures the real frame time itself (clamped against hitches) for loops you write yourself, and `Time.Reset()` restores the startup clock
- `Renderer2D.DrawQuad` from four corners (any convex quad, with a UV rectangle) and a public `Renderer2D.WhiteTexture`; the framework adds `Renderer2D.DrawTriangle`, `DrawCircle`, `DrawPolygon` and `DrawSprite` (atlas regions in top-left pixels, rotation, tint, flips)
- **Public low-level GPU access**: `Renderer.Device` (the backend-neutral `IGraphicsDevice`) and `Renderer.Init` are public, `GraphicsBuffer<T>` (formerly the internal `BufferObject`) is a public buffer primitive, every GPU wrapper exposes its typed device `Handle`, `Shader` reports `IsValid`/`ErrorLog`, `VertexArray` exposes `IndexCount`/`AttributeCount`/`IndexBuffer`, and the `ShaderDataType` size/component helpers are public
- **Dependency-free `Log`**: logging works with no setup (it writes to the terminal until configured) and never throws, even with a bad template or a failing sink; destinations are pluggable `ILogSink`s (`Log.AddSink`/`RemoveSink`/`ClearSinks`, `Log.MinimumLevel`), and the engine layers its Serilog terminal + rolling-file output on top through `EngineLogging`
- **Code-first resource loading**: `Image` decodes PNG/JPG/BMP/TGA/GIF into CPU pixels (`FromFile`/`FromBytes`/`FromStream`, `FlipVertically`, `ToTexture`), plus `Texture2D.FromFile`, `AudioClip.FromFile`/`FromBytes` and `Font.FromFile`; `AudioDecoder` is public and detects WAV/OGG from the data itself
- **`FileSystem`**: every loader reads through a swappable `IFileSystem` (disk by default, the fetched content store in the browser) with an optional `PathResolver`; the engine installs a project-relative resolver whenever `AssetPath.Root` is set
- **Input mechanisms are public**: `Input.Captured` (an overlay owns input: reads withheld, mouse frozen, cursor forced free and restored after) and `Input.Suppressed` (reads withheld, cursor untouched), `Input.IsBlocked`, `ReleaseCursor`/`RestoreCursor`, `NewFrame`/`OnEvent`/`AddMouseMotion`/`TickCursorLock`/`ICursorController` for driving input from a custom platform, `Input.Reset`, the `FrameStarted`/`Cleared` events, and `GetGamepadAxis(axis)`/`GetPreviousGamepadAxis(axis)` across every pad
- **Self-sufficient `Window`**: creating one installs its GL context as the renderer's device, `PollEvents` drives `Input` (new frame, events, cursor lock) and keeps the viewport in sync (including the startup drawable-size fix), and it gains `IsOpen`, `Close`, `FramebufferWidth`/`Height`, `SyncViewport`, `SetIcon(WindowIcon)` and a `WindowSpec.VSync`/`Icon` — enough to write your own `while (window.IsOpen)` loop with no `Application`
- `Renderer2D` initializes on first use (and again on a new device), and gains a public `Flush`, `ViewProjection`, `Init` and `Shutdown`
- `ModelImporter.ReferenceResolver` and a public `ModelImporter.BuildModel`: model references resolve through a pluggable hook and every format — cooked `.sptmesh` included, via the engine's `SpMeshModelImporter` — loads through a registered `IModelImporter`; `EngineAssets.Install` plugs the engine's guid resolution and cooked formats into the framework loaders
- `AssimpModelImporter.ReadMaterials` (per-slot name, base color and texture) and `ExtractEmbeddedTextures`, so a framework user can read a model's materials without the engine's material format
- `Framebuffer` (offscreen color + depth-stencil target) runs through the graphics device, so it now works in the browser too, and exposes typed `ColorTexture`/`DepthTexture`; `Bind` keeps the renderer's tracked target in sync
- `UIRenderer` initializes on first use (and again on a new device), with public `Init`/`Shutdown`
- **Browser platform layer**: a browser `Window` with the same public API as the desktop one (WebGL2 device, Pointer Lock cursor, DOM input into `Input`), and `BrowserPlatform` holding the page's JavaScript entry points with an `AnimationFrame` event to drive a frame; `DomInput` maps DOM key/button codes on every target
- **Rebuilt undo/redo**: one unified history for the whole editor (`Ctrl`+`Z` / `Ctrl`+`Shift`+`Z` / `Ctrl`+`Y`), recorded per *operation* instead of by polling full-scene snapshots. One edit is one entry (a slider drag, a color-picker session, a gizmo move, a burst of arrow nudges each collapse into a single step), entries are named in the **Edit** menu (*Undo Set Intensity*), and undo restores the selection — multi-selection included — alongside the state. Actions target entities by their stable id and re-resolve on apply, so they survive a scene re-hydration; the shortcut no longer fires while a text field has focus. Anything not yet routed through a specific action is still caught and recorded as a coarse *Scene Change* entry, so no change is un-undoable
- **History panel** (`Ctrl`+`H`): the action list with a cursor on the current state, the redo branch dimmed rather than hidden, click or `↑`/`↓`+`Enter` to jump to any point, and a footer reporting entry count, memory use and how many changes were only caught generically
- **Audio mixer / bus routing**: named volume groups (Master / Music / SFX / UI) in a bus tree; audio sources and `Audio.Play` pick a bus, and a bus's fader, mute, and solo apply to everything beneath it. The layout is project data — authored, saved to the `.sptproj`, and shipped in `game.manifest`
- **Audio Mixer panel** (`Ctrl+M`, editor and runtime overlay): channel strips with faders, live level meters, mute/solo, and re-routing, fully keyboard-driven
- `volume`, `mute`, `solo`, and `buses` console commands for changing the mix live
- **Play-in-viewport**: pressing Play now runs the game simulation directly inside the editor — no build step, no external process. Scripts, physics, audio, and animation tick in real time. Press Pause (`Ctrl+P`) to freeze the simulation, Step (`Ctrl+Right`) to advance one frame, and Stop to restore the scene to its exact pre-play state.
- **Occlusion culling**: tick *Occluder* on a mesh renderer and geometry hidden behind it is dropped before it is drawn; occluders are rasterized into a small CPU depth buffer each frame, so it needs no baking, no GPU queries, and runs the same on desktop and WebGL2. Global knobs `RenderSettings.OcclusionCulling` (on by default) and `OcclusionBufferWidth`; `occlusion` console command, counters in `RendererDebug`, and a culling section in the Profiler panel
- Color grading LUT support in `PostProcessingComponent`: assign a 2D horizontal-strip LUT texture (e.g. 256×16 for a 16³ grade) and blend it with `LutIntensity`
- Physics materials: `Friction` and `Restitution` on `Collider3DComponent` (Bepu static bodies) and `PhysicsBody2DComponent` values now correctly applied to Aether fixtures

### Changed
- Writing `.sptmat` files from a model moved out of the Assimp importer: `AssimpModelImporter.ExtractMaterialsPerSlot`/`ExtractMaterials` became `ModelMaterials.ExtractPerSlot`/`ExtractEmbedded`; `Aabb.FromTransform` is now an engine extension (same call, `using Spot.Engine.Physics;`)
- `WindowSpec.IconPath` moved to `ApplicationSpec.IconPath` (the window takes raw pixels; the engine loads the file); `Renderer2D.DrawEditorGrid` became `EditorGrid.Draw2D`; the window no longer reads `RenderSettings` (the engine forwards `RenderSettings.VSync` to it)
- Named input actions moved out of the raw `Input` state into `InputActions`; `Input.GetAction`/`Bind`/`SetDefaultBindings`/... keep working as extension members. The engine-only capture API was renamed: `SetEngineCaptured`/`EngineCaptured` → `Input.Captured`, `GameInputSuppressed` → `Input.Suppressed`, `EditorReleaseCursor`/`EditorRestoreCursor` → `ReleaseCursor`/`RestoreCursor`
- The browser `BrowserHost` now only boots the engine (content, manifest, start scene) on top of the browser `Window`; the page's input, resize and frame calls go to `BrowserPlatform` (regenerate browser builds to pick up the new bootstrap)
- `Texture2D`, `Font` and `AudioClip` no longer know about project assets: `new Texture2D(path)` is now `Texture2D.FromFile(path)`, and `Texture2D.Load`/`Font.Load`/`AudioClip.Load` (guid or project path) plus `FromSpTex`/`FromSpAudio` are engine extensions found under `using Spot.Engine.Assets;`. `AssetProvider`/`IAssetProvider` became `FileSystem`/`IFileSystem` (`Spot.Framework.IO`)
- `Texture2D` rejects pixel data that does not match its size instead of letting the driver read out of bounds, and disposing it twice is safe; `AudioClip` rejects channel counts other than 1–2 and non-positive sample rates
- **Namespaces now say which level a type belongs to** (breaking). Saved scenes, prefabs and UI documents are unaffected (they store stable keys, not CLR names); scripts are migrated by `spot migrate`:
  - `Spot.Core` → `Spot.Framework` (window, input, `Time`, `Log`, input actions, profiler) and `Spot.Engine` (application, project, `EngineLogging`); `Spot.Core.Services` → `Spot.Engine.Services`; `Spot.SpotEngine` → `Spot.Engine.SpotEngine`
  - `Spot.Events` → `Spot.Framework.Events`; `Spot.Audio` → `Spot.Framework.Audio`; `Spot.IO` → `Spot.Framework.IO`
  - `Spot.Rendering` → `Spot.Framework.Graphics` (device, resources, batchers, text, meshes/models) and `Spot.Engine.Rendering` (lit 3D renderer, post-processing, particles, `RenderSettings`); `Frustum`/`Aabb`/`Aabb3d`/`SpotMath` → `Spot.Framework.Mathematics`
  - `Spot.Assets` → `Spot.Engine.Assets` (pipeline, cooked formats, materials, guid loading), with model types in `Spot.Framework.Graphics`, `AudioDecoder` in `Spot.Framework.Audio` and the Assimp importer in `Spot.Framework.Assimp`
  - `Spot.Animation` → `Spot.Framework.Animation` (clips, bones) and `Spot.Engine.Animation` (animator controller); `Spot.Physics[.Bepu|.Aether]` → `Spot.Engine.Physics[...]`; `Spot.Scenes` → `Spot.Engine.Scenes`; `Spot.UI` and `Spot.UI.Serialization` → `Spot.Engine.UI`; `Spot.Console` → `Spot.Engine.Console`; `Spot.Browser` → `Spot.Framework.Browser` / `Spot.Engine.Browser`
- `primitive:` and `editor:Checkerboard` references are read as aliases of `builtin:Mesh/…` and `builtin:Material/Checker` and saved in the new form; `Material.IsBuiltin` flags shared built-in materials
- `spot migrate` also moves scripts (and the root `Program.cs`) to the new namespaces: each old `using` becomes just the namespaces the script uses, and fully qualified names are re-pointed
- Games now reference `Spot.Framework.Core.dll`, `Spot.Framework.dll` (and optionally `Spot.Framework.Assimp.dll`) beside `Spot.Engine.dll`: run `spot generate` (or open the project in the editor) to refresh `EngineBin` and the `.csproj`. `spot build` now bundles a current `Spot.DebugUI.dll` too
- `Mesh.GetInstancedVertexArray` keeps one vertex array per instance buffer, so several renderers can instance the same mesh
- `SilentAudioBackend`, `OpenAlAudioBackend`, `WebAudioBackend`, `Time.NewFrame`, `AnimationMath`, `Mesh.VertexArray` and `Mesh.GetInstancedVertexArray` are public
- `Log.Init`/`Log.CloseAndFlush` moved to `EngineLogging.Init`/`EngineLogging.CloseAndFlush`; `Log.CoreLogger`/`ClientLogger` (Serilog loggers) are gone, and `DevConsoleSink` is now an `ILogSink`. Strings in log messages are no longer quoted in the developer console
- `Texture2D.Handle` is now a typed `TextureHandle`; use `.Handle.Id` for the native texture name (e.g. for `ImGui.Image`)
- Quieter console: routine success chatter is gone (startup banners for the log file, window and audio device, one line per file dropped into the project, project-scripts load, a project save on every keystroke in Project Settings), build output is filtered to warnings and errors instead of mirroring msbuild's restore/timing narration, and a uniform the shader compiler dropped is now a trace rather than a warning
- Scene camera default look rate retuned (60% slower): the previous rate compensated for the dropped mouse motion described below, and felt far too fast once that was fixed
- Scene camera **Look sensitivity** and **Fly speed** are now adjustable from the viewport's **Camera** toolbar button and persist with the window layout; `Shift` while flying is a 4x multiplier on the configured speed
- Extracted shared GLSL `ShadowCalculation` and `hash` utilities into `GlslSnippets.cs`; eliminated copy-paste across fragment, water, skybox, clouds, and post-process shaders

### Added
- **Point light shadows**: point and spot lights now cast real-time cubemap shadows; enable per-light via `CastShadows` (the first shadow-casting light in the scene gets a depth cubemap, sampled as linear distance); `RenderSettings.PointShadows` and `PointShadowResolution` are global controls
- **Spotlight**: new `LightType.Spot` with `SpotAngle` (inner half-angle) and `SpotOuterAngle` (outer half-angle) properties; smooth cone attenuation applied in all lit shaders (standard + water)
- **Profiler window**: `View > Panels > Profiler` opens an ImGui panel with a scrolling frame-time graph and a per-system ms/frame table; systems expose their name via `ISystem.Name`
- Basic networking foundation in a new `Spot.Net` library: server-authoritative sessions (host/dedicated server/client) over a cross-platform WebSocket transport that runs on desktop and browser
- Networked identity and server-authoritative spawn/despawn via a prefab registry (`NetworkObject`, `NetworkSpawner`)
- `NetworkTransform` replicates position/rotation with client-side interpolation
- `NetworkBehaviour` with `[ServerRpc]`/`[ClientRpc]` RPCs and `SyncVar<T>` synchronized variables
- Connection health: heartbeats keep quiet connections alive and silent peers time out; an inbound message-size cap bounds untrusted buffering
- Developer-console commands `net_host`, `net_connect`, `net_stop`, `net_status` (desktop)
- `NetworkSettings.UseSsl`: switches the client transport to `wss://` for WASM builds served over HTTPS
- `NetworkBehaviour.OnNetworkConnected` / `OnNetworkDisconnected` virtual lifecycle hooks for client scripts
- `net_status` now reports the count of live networked objects
- `net_host` prints a hint when `BindAddress` is `localhost` so remote-connection failures are obvious
- Hierarchy `Ctrl+Shift+N` shortcut to create an empty entity (mirrors Unity convention)

### Removed
- The `sandbox/` project: `samples/` now holds the examples, with `samples/HelloEngine` as the engine-level project

### Fixed
- The generated sphere was wound inside out (so `BasicRenderer3D` culled its outside and lit it from the wrong side) and mapped its texture upside down; the generated plane mirrored its texture
- `spot build <desktop> --project <relative path>` published into a nested `<project>/<project>/Build/...` folder while the cooked content went to the real `Build/` folder, leaving a build that could not find its content
- The `'` key in the editor no longer opens a duplicate console: the editor claims ownership of the console window (`DevConsole.SetHost`), so the engine stops drawing its floating overlay — ImGui merged the two by name and drew the whole console body, command prompt included, twice. `'` now reveals and focuses the docked **Console** panel from anywhere in the editor, and from the Game panel it also releases game input and the cursor (as `Esc` does) instead of leaving the game's input dead with no way to dismiss the capture
- Opening the developer console no longer lets the game keep mouse-looking: `Input.MousePosition` freezes while the engine owns input (console or runtime debugger open), so a game that tracks frame-to-frame mouse delta stops rotating the camera instead of spinning as the freed cursor moves, and resumes without a jump on close. The freed cursor also falls back to the window centre when the position it was locked from no longer lies inside the window
- Scene view camera look is smooth again: the fly camera no longer recentres the cursor every frame (which discarded a variable slice of each frame's mouse motion, making the look feel sluggish and jittery); it now reads plain frame-to-frame movement and only warps back to the centre near a viewport border
- Play mode auto-builds project scripts on first Play when no compiled DLL exists yet (clean checkout or new project), so game scripts always run instead of silently doing nothing
- Cursor lock (`Input.CursorLocked = true`) now correctly hides the hardware cursor during in-editor play; ImGui's backend no longer resets it to visible every frame
- Pressing Stop now releases any cursor lock the game held, returning the cursor to the normal editor state
- Game input (WASD, mouse-look, cursor lock) is now isolated to the Game panel: the game only receives input when the Game panel is active (click to focus, Escape to release); in the Scene view the editor camera works freely as expected
- Editor "Add Component" popup now closes reliably on Escape (InputText was swallowing the key)
- "Show Colliders" viewport checkbox now draws colliders for all scene entities, not only the selected one

## [v0.2.0] - 2026-09-02

**Gameplay & Shipping.**

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
- Viewport camera fly (right-drag) now confines the cursor to the viewport by recentring it each frame (GLFW's Disabled/Raw cursor lock silently failed to confine on some setups, letting the hidden cursor escape) and no longer triggers from a right-click in other panels (e.g. the hierarchy context menu)
- Runtime cursor lock (mouse-look) now confines the hardware cursor to the window by recentring it each frame instead of relying on the backend's unreliable Raw/Disabled confine mode, so it no longer escapes during play
- Blank window until resize

## [v0.1.0] - 2026-08-12

### Added
- Initial release of Spot Engine.
