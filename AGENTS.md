# AGENTS.md

This file provides guidance to AI Agents when working with code in this repository.

## What this is

Spot is a 2D/3D game engine written in C# (.NET 10), built on Silk.NET (windowing, OpenGL, input, Assimp) and Dear ImGui (via ImGuiNET). It ships an ImGui-based editor, a sandbox project, a project/build toolchain, and an xUnit test suite under `tests/` (`Spot.Engine.Tests`, `Spot.Build.Tests`).

## Commands

Build/run everything from the repo root. The solution is `SpotEngine.slnx` (the `.slnx` XML solution format, not `.sln`).

```bash
dotnet build SpotEngine.slnx        # build the whole solution
dotnet test SpotEngine.slnx         # run the xUnit test suite
dotnet run --project editor         # launch the ImGui editor (starts on LauncherScene)
dotnet run --project sandbox        # run the sandbox project (data-driven .sptscene showcase)
dotnet run --project tools/Spot.Cli -- new MyGame --path <dir>   # `spot` CLI (assembly name: spot)
dotnet run --project tools/Spot.Cli -- build windows --project <path>
```

`dotnet build` on a single project also works (e.g. `dotnet build engine/Spot.Engine.csproj`).

### Warnings are errors

`Spot.Engine`, `Sandbox`, and `Spot.Build` set `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — any warning fails the build. New code in those projects must be warning-clean (nullable annotations, unused usings, etc.). The editor and CLI do not enforce this. Nullable reference types and `ImplicitUsings` are enabled everywhere.

## Hard rule: the engine must never crash

This is a non-negotiable design directive, not a nicety. Bad user input, a throwing script, a broken scene file, or a faulty UI panel must **log and continue**, never take the process down. The mechanisms already in place, which you must preserve when touching these paths:

- `Application.Run` wraps each frame (update/render/UI/events) in try/catch and reports recovered exceptions via `ReportFrameError` (which de-dups identical repeating faults). Only startup failures (window/GL/ImGui creation) are allowed to be fatal.
- `ScriptSystem` runs each `EntityBehaviour` in its own guard and **quarantines** a script that throws (sets `Faulted`, skips it thereafter) so one bad script neither crashes nor spams.
- `Application.CanClose` is a veto gate for window-close (the editor uses it to confirm unsaved changes); call `Application.Quit()` to close unconditionally.
- Loaders (`SceneSerializer`, `Model.Load`, `Texture2D`, `Project.Load`) catch and log rather than throw.

When adding features that run per-frame or load external data, keep them inside these safety nets.

## Architecture

### Application & main loop (`engine/Core`)

`Application` is a singleton (`Application.Instance`) that owns the window, GL context, ImGui controller, and the main loop. `Run(startScene)` initializes the renderers, registers the Assimp model importer, loads the start scene, then loops. `Log` (Serilog-based) exposes Core* (engine, "SPOT") and plain (client, "APP") loggers plus a `DevConsole` sink toggled with the `'` key.

### Scenes are an ECS-lite + a screen (`engine/Scenes`)

`Scene` is both an entity/component container **and** a switchable screen with lifecycle hooks (`OnEnter`/`OnUpdate`/`OnRender`/`OnImGuiRender`/`OnEvent`/`OnExit`). Derive from it for menus/levels, or use a plain `Scene` populated by the serializer.

- **Entities** are just an `int` id. `Entity` is a lightweight readonly struct handle carrying the id + scene reference.
- **Components** are plain classes stored in per-type pools: `Dictionary<Type, Dictionary<int, object>>`. Every entity gets `LabelComponent`, `RelationshipComponent` (parent/children hierarchy), and `Transform` on `Instantiate`.
- **Queries**: `View<T>()` / `View<T1,T2>()` return a **snapshot** `List<Entity>`, so it's safe to add/destroy entities while iterating. `Destroy` is deferred to end-of-frame (`FlushDestroyed`).
- **Systems** are static and query the scene: `RenderSystem` (draws `MeshRenderer` + `Sprite2D` each with its `Transform`, plus the first `DirectionalLightComponent`), `ScriptSystem` (runs scripts), `Physics2DSystem`.

`SceneManager` (static) holds the one active scene and applies a `Load(scene)` request at the next frame boundary (`ApplyPendingSwitch`). `Scene.UpdateRuntime` is the play-mode tick: `OnUpdate` + physics + scripts + flush.

### Layered rendering (`engine/Rendering`)

Rendering is deliberately layered so callers choose their altitude:
- `Renderer` — low-level: init, clear, viewport, depth/state, draw calls; `Renderer.Api` exposes the raw Silk.NET `GL`.
- `Renderer2D` — batched quads/lines/rects (`BeginScene`/`DrawQuad`/`EndScene`).
- `Renderer3D` — meshes, skybox, editor grid, directional lighting.
- `RenderSystem` — the optional *automatic* path that walks the scene and draws for you.

`RenderSystem.Render` is not called by the engine automatically; a `Scene.OnRender` (or the editor) calls it once a primary `CameraComponent` provides a view-projection.

### Scripting (`engine/Scenes`)

`EntityBehaviour` is the MonoBehaviour analog: `OnCreate`/`OnUpdate`/`OnDestroy`, attached via `Entity.AddScript<T>()`. In serialized scenes, scripts are stored by **class name string** and resolved by reflection across all loaded assemblies at deserialize time (`SceneSerializer`), so game script types live in the game/project assembly, not the engine.

### Assets & serialization

- `ModelImporter` is a registry; `AssimpModelImporter` is registered at startup. `Model`/`Mesh`, `Material`, `PrimitiveModelFactory` (cube/plane/quad/sphere), `Texture2D` (StbImageSharp).
- `SceneSerializer` reads/writes `.sptscene` JSON via `SceneData`/`EntityData` DTOs (one nullable DTO per component type; hierarchy stored as nested `Children`). BOM-tolerant, catches parse errors.
- `AssetPath` keeps stored asset references portable: paths in scenes/materials are relative to the project's `Assets/` dir (`AssetPath.Root`, set by `Project.Load`/`SaveActive`). Loaders (`Texture2D`, `Model.Load`, `Material.Load`) resolve them to absolute at load; the serializer relativizes on save. Absolute paths and the `primitive:`/`editor:` pseudo-paths pass through unchanged, so a committed project loads on any machine.
- `Project`/`ProjectConfig` model a `.sptproj` JSON file (`Name`, `StartScene`, `AssetDirectory`). `Project.Active` is the loaded project. `Project` deliberately does **not** depend on the build tooling.

### Editor (`editor/`)

ImGui docking-based. `EditorScene` is the shell: multiple open scenes as dockable tabs (`OpenSceneData`, each with its own `Framebuffer` + `EditorCamera`), Play/Edit mode where Play does a fast Debug build of the project (`ProjectBuilder.Build(fastDebug: true)`) and launches it as a separate process while the editor's own scene copy stays frozen in edit mode (Stop kills that process), unsaved-change confirmation dialogs, and per-panel dock layout persisted to `imgui.ini`. Panels (`editor/Panels`): Hierarchy, Inspector, Console, AssetBrowser, Viewport. `EditorContext` carries the current selection (entity XOR asset). The editor accesses engine internals via `[InternalsVisibleTo("Spot.Editor")]` (declared in `engine/Spot.cs`).

### Project/build toolchain (`tools/`)

`Spot.Build` is a library (used **in-process by the editor** and wrapped by the CLI) that turns a `.sptproj` into a buildable, distributable app:
- `ProjectScaffolder.Create` — new project on disk (folder, `Assets/`, `.sptproj`, empty start scene) + generated build files.
- `ProjectGenerator.Generate` — writes `<Name>.csproj`/`.sln`/`Program.cs` and copies `Spot.Engine.dll` into the project's `EngineBin/`. Generated projects reference the engine as that **copied DLL** (`HintPath`), not a project reference, and pin the same NuGet versions. `Program.cs` is preserved on regenerate unless `overwriteProgram`/`--full`.
- `ProjectBuilder.Build` — runs `dotnet publish -c Release -r <rid> --self-contained` into `Build/<platform>`, streaming output through callbacks (never writes to console itself, so both editor and CLI can present results).

`Spot.Cli` (assembly `spot`) is a thin, never-throws front-end: `new`, `generate`, `build`, `help`.

## Projects & dependencies

| Project | Output | References |
|---|---|---|
| `engine/Spot.Engine` | library (namespace `Spot`) | Silk.NET, Serilog, StbImageSharp |
| `editor/Spot.Editor` | exe | engine, Spot.Build |
| `sandbox/Sandbox` | exe (sandbox project) | engine, Spot.Build |
| `tools/Spot.Build` | library | engine |
| `tools/Spot.Cli` | exe (`spot`) | Spot.Build |
