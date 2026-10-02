# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Work in progress toward **v0.3**. The list below is provisional and will be finalized when 0.3 is tagged.

### Added
- **Public low-level GPU access**: `Renderer.Device` (the backend-neutral `IGraphicsDevice`) and `Renderer.Init` are public, `GraphicsBuffer<T>` (formerly the internal `BufferObject`) is a public buffer primitive, every GPU wrapper exposes its typed device `Handle`, `Shader` reports `IsValid`/`ErrorLog`, `VertexArray` exposes `IndexCount`/`AttributeCount`/`IndexBuffer`, and the `ShaderDataType` size/component helpers are public
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

### Fixed
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
