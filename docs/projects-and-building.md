# Projects & Building a Game

A **project** is your game: its assets, its scenes, and the configuration that ties them together.
Spot's build tooling turns a project into a standalone application you can distribute.

## What a project is

A project is a folder on disk containing:

- An **`Assets/`** folder with your content — scenes, models, textures, audio, prefabs, and so on.
- A **project file** (`.sptproj`) that records the project's name, where its assets live, and which
  scene the game starts on.

The editor and the command-line tool both read and write this format, so you can move between them
freely.

## The build tooling

Two pieces of tooling operate on a project, and both share the same underlying library
(`Spot.Build`), so the editor and the CLI do exactly the same thing:

- **`Spot.Build`** — the library that scaffolds a project, generates its build files, and publishes
  a build. The editor calls it in-process.
- **The `spot` CLI** — a thin command-line front-end over that library, convenient for scripting,
  automation, and headless workflows.

A published build is **self-contained and standalone** — it bundles the engine and everything the
game needs, so players don't install anything extra. Builds can target **Windows**, **Linux**, or **Mac**, or
**the browser** (WebAssembly + WebGL2) for 2D games — see [The browser target](#the-browser-target).

## From project to shippable app

Producing a distributable build generally follows these steps:

1. **Create or open a project** — in the editor, or with the CLI's `new` command.
2. **Build your content** — lay out scenes and entities, import assets, and set the start scene.
3. **Cook the assets** — turn source assets into engine-native artifacts and a manifest (see
   [Assets](assets.md)). This runs as part of a build.
4. **Publish a build** — choose a platform and let the tool generate the project files, bundle the
   engine, cook content, and produce the final self-contained application into the project's build
   folder. The output is a folder you can zip up and hand to a player.

### Where build artifacts go

Everything a build or a Play/Run produces lands under **`Build/`** — the project root stays clean:

- **`Play`** (in the editor) publishes a fast Debug build to `Build/play`, cooks the assets into
  `Build/play/Content`, writes `game.manifest` beside the game, and launches it from that folder.
- **`spot run`** cooks into `Build/run` and runs the game from there.
- **`spot build <platform>`** publishes the self-contained app into `Build/<platform>` with its
  `Content/` and `game.manifest` alongside.

The cooked `Content/` and the `game.manifest` are **generated build outputs**, not source you edit or
commit — they live only inside `Build/`, never at the project root. `game.manifest` is rewritten on
every build/run from the `.sptproj` config, so changing the start scene in Project Settings takes
effect on the next Play with no manual cleanup.

## Using the `spot` CLI

The CLI exposes these operations. At a high level:

```bash
# Create a new project (folder, Assets/, .sptproj, build files)
dotnet run --project tools/Spot.Cli -- new MyGame --path <dir>

# Regenerate a project's build files (and copy the engine DLL)
dotnet run --project tools/Spot.Cli -- generate --project <dir>

# Cook source assets into engine-native artifacts + a manifest
dotnet run --project tools/Spot.Cli -- cook --project <dir>

# Rewrite asset references to stable guid: references (and add .meta sidecars)
dotnet run --project tools/Spot.Cli -- migrate --project <dir>

# Cook assets and run the project from source (quick iteration, no publish)
dotnet run --project tools/Spot.Cli -- run --project <dir>

# Cook assets and start the browser dev server (quick iteration, no publish)
dotnet run --project tools/Spot.Cli -- run browser --project <dir>

# Publish a self-contained standalone build (windows | linux)
dotnet run --project tools/Spot.Cli -- build windows --project <dir>

# Publish a browser (WebAssembly + WebGL2) build into Build/browser
dotnet run --project tools/Spot.Cli -- build browser --project <dir>

# List all commands and options
dotnet run --project tools/Spot.Cli -- help
```

The exact options are printed by `help`; run it to see the current set.

## The browser target

`spot build browser` (aliases `web`, `wasm`) publishes the game as a **WebAssembly + WebGL2 static
site** into `Build/browser`. It is the shipping side of the engine's browser port: the same neutral
engine core that runs on the desktop is compiled for the `net10.0-browser` target and driven from
JavaScript instead of a native window.

How it differs from a desktop build:

- **Rendering** goes through a WebGL2 backend that issues every GL call from C# over `[JSImport]`;
  engine shaders are rewritten to `#version 300 es` automatically.
- **The loop** is driven by the browser's `requestAnimationFrame`, and DOM keyboard/mouse events feed
  the same `Input` API your scripts already use.
- **Content** is cooked exactly as for desktop, staged under `wwwroot/content`, and fetched into memory
  before the first scene loads (listed in a generated `content-index.txt`).
- **Particles** work in the browser. `ParticleSystem` components and all their blend modes (alpha /
  additive) render correctly; the `GlslTranspiler` already handles the shader.
- **Audio** works in the browser through a Web Audio backend, including spatial sources. Sound stays
  silent until the first click or key press (the browser autoplay policy unlocks it automatically).
  See [Audio](audio.md).
- **3D** works in the browser: the same `RenderSystem` as the desktop renders meshes, materials, lighting
  (directional + point + ambient), shadows, and the procedural skybox through the WebGL2 device. Cooked
  `.sptmesh` models load synchronously (the WASM runtime is single-threaded).
- **Scope**: HDR/bloom/post-processing is still desktop-only (a follow-up will bring it to the browser);
  wireframe is unavailable in WebGL2. The ImGui editor overlay and the Assimp model importer are
  desktop-only and are not part of a browser build.

**For development iteration**, use `spot run browser` instead of `spot build browser`. It cooks
assets, (re)generates the browser project, stages content, and starts the SDK's built-in Kestrel
dev server with `dotnet run` — much faster than a full publish. The URL is printed to the console;
press Ctrl+C to stop.

The published `Build/browser` folder is a static site; serve it with any host that returns
`application/wasm` for `.wasm` files (the `dotnet serve`/`dotnet run` dev server does this).

> **Prerequisite:** the WebAssembly workloads (`dotnet workload install wasm-tools`) must be installed,
> and the engine must be built for the browser target (`dotnet build engine -f net10.0-browser`) so the
> tooling can bundle its `net10.0-browser` assembly.

A project name doubles as the project's folder, its `.csproj`/`.sln` filename and its generated C#
namespace, so `new` rejects names that aren't a valid path segment and identifier (e.g. `My:Game` or a
name starting with a digit) up front with a clear message, rather than scaffolding a project that won't
build. `cook` and `migrate` likewise fail with a friendly error when the project has no `Assets/`
directory, and `cook` exits non-zero if any asset failed to cook.

## Related

- [The Editor](editor.md) — the visual way to build a project
- [Assets](assets.md) — the cooking pipeline a build runs
- [Scenes](scenes.md) — the content a project is made of
