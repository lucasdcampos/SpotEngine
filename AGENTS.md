# AGENTS.md

Spot is a 2D/3D game engine written in C# (.NET 10) on Silk.NET (windowing, OpenGL, input, Assimp) with a Dear ImGui editor.

It is split into three levels (see `docs/levels.md`), each its own assembly with dependencies only pointing down: **L1 Core** `Spot.Framework.Core` (window, events, raw input, graphics device, minimal 2D batch, PCM audio device; no loop) ← **L2 Framework** `Spot.Framework` (+ `Spot.Framework.Assimp`; code-only: loaders/decoders, text, basic 3D, models, skeletons, mixer, input actions) ← **L3 Engine** `Spot.Engine` (Application loop, scenes, full renderer, physics, animator, particles, post, UI, assets). The framework provides *mechanisms* (data + functions you call), the engine *policies* (systems that run on their own, file formats, library choices). Low-level access must stay public at every level.

## Commands

Run from the repo root. The solution is `SpotEngine.slnx` (XML `.slnx` format, not `.sln`).

```bash
dotnet build SpotEngine.slnx                 # build the whole solution
dotnet test SpotEngine.slnx                  # run the xUnit test suite
dotnet run --project editor                  # launch the ImGui editor
dotnet run --project tools/Spot.Cli -- help  # the `spot` CLI (new/generate/build/cook/migrate)
dotnet run --project samples/HelloQuad       # framework samples: HelloQuad, HelloTriangle (L1), Hello2D, Hello3D (L2); `-- --frames N` exits after N frames
dotnet run --project tools/Spot.Cli -- run --project samples/HelloEngine  # L3 sample project: generate + cook + run
```

## Projects

| Project | Output | Notes |
|---|---|---|
| `framework/Spot.Framework.Core` | library (namespaces `Spot.Framework[.*]`) | L1: window, events, raw input, `IGraphicsDevice` (OpenGL/WebGL2), GPU resources, minimal `Renderer2D`, audio device. No file loading, no loop |
| `framework/Spot.Framework` | library (namespaces `Spot.Framework[.*]`) | L2: `FileSystem`, `Image`, fonts/text, shapes/sprites, `BasicRenderer3D`, `BillboardBatch`, `FullscreenPass`, models, `Skeleton`, audio clips/mixer, input actions |
| `framework/Spot.Framework.Assimp` | library (desktop only) | L2 module: runtime FBX/glTF/OBJ import via Assimp |
| `engine/Spot.Engine` | library (namespaces `Spot.Engine[.*]`) | L3: the opinionated engine (Application, scenes, rendering, physics, assets, UI, console) |
| `debugui/Spot.DebugUI` | library (namespace `Spot.DebugUI`) | ImGui debug/authoring panels (hierarchy, inspector, theming); referenced by the editor and hostable as the runtime debug overlay. Kept out of the engine so the runtime carries no authoring UI |
| `editor/Spot.Editor` | exe | ImGui docking editor |
| `tools/Spot.Build` | library | `.sptproj` → buildable app (used by editor + CLI) |
| `tools/Spot.Cli` | exe (`spot`) | thin CLI front-end over Spot.Build |
| `net/Spot.Net` | library | WebSocket networking (server-authoritative) |
| `samples/*` | exe | framework programs in the solution (HelloQuad, HelloTriangle, Hello2D, Hello3D) and `samples/HelloEngine`, an L3 `.sptproj` project: only its source is committed (its `.csproj`/`.sln` are regenerated and gitignored, so it is not in the solution); keep it free of binary assets. `SampleProjectTests` check its scenes, UI and scripts still load. Smoke-test engine changes with `spot build windows --project samples/HelloEngine` and run the exe from its build folder |
| `tests/*` | xUnit | `Spot.Framework.Tests` (framework only, no engine reference), `Spot.Engine.Tests` (includes the architecture tests that pin the layering), `Spot.Build.Tests`, `Spot.Net.Tests`; shared fakes (e.g. `RecordingGraphicsDevice`) in `tests/Shared`, binary fixtures in `tests/Fixtures` |

## Rules

- **Never crash the engine.** Bad input, a throwing script, a broken scene, or a faulty panel must log and continue, never take the process down. Preserve the existing safety nets (`Application.Run` frame try/catch, `ScriptSystem` script quarantine, render-pass/system guards, loaders that catch and log). The framework levels are libraries: they never crash on their own (logging never throws) but report errors to the caller; per-frame nets live in the engine.
- **Respect the levels.** Never make a lower level reference a higher one (`ArchitectureTests` enforce it). Put a feature in the framework only if it is a mechanism with no owner of time or the world and no third-party library choice; otherwise it belongs to the engine. Public types must live under their level's namespace.
- **Warnings are errors** in the framework projects, `Spot.Engine`, `Spot.DebugUI`, `Spot.Net`, `Spot.Build`, and the samples (`TreatWarningsAsErrors`). New code there must be warning-clean. Nullable reference types and `ImplicitUsings` are on everywhere.
- **Always prioritize UX and Keyboard Shortcuts.** When implementing new editor features or panels, consider the user experience: ensure common actions (like deleting, navigating, or undo/redo where applicable) have intuitive keyboard shortcuts and that visual feedback is clear.
- **Before marking a task complete**, always build (`dotnet build SpotEngine.slnx`) and run the tests (`dotnet test SpotEngine.slnx`), and confirm both pass.
- **Always update the docs** under `docs/` when you change behavior, add features, or alter architecture.
- **Always update `CHANGELOG.md`** when you make significant changes. Keep it concise: one line per change, focusing only on what was added/changed/fixed, not how.
