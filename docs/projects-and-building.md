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
game needs, so players don't install anything extra. Builds can target **Windows** or **Linux**.

## From project to shippable app

Producing a distributable build generally follows these steps:

1. **Create or open a project** — in the editor, or with the CLI's `new` command.
2. **Build your content** — lay out scenes and entities, import assets, and set the start scene.
3. **Cook the assets** — turn source assets into engine-native artifacts and a manifest (see
   [Assets](assets.md)). This runs as part of a build.
4. **Publish a build** — choose a platform and let the tool generate the project files, bundle the
   engine, cook content, and produce the final self-contained application into the project's build
   folder. The output is a folder you can zip up and hand to a player.

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

# Publish a self-contained standalone build (windows | linux)
dotnet run --project tools/Spot.Cli -- build windows --project <dir>

# List all commands and options
dotnet run --project tools/Spot.Cli -- help
```

The exact options are printed by `help`; run it to see the current set.

A project name doubles as the project's folder, its `.csproj`/`.sln` filename and its generated C#
namespace, so `new` rejects names that aren't a valid path segment and identifier (e.g. `My:Game` or a
name starting with a digit) up front with a clear message, rather than scaffolding a project that won't
build. `cook` and `migrate` likewise fail with a friendly error when the project has no `Assets/`
directory, and `cook` exits non-zero if any asset failed to cook.

## Related

- [The Editor](editor.md) — the visual way to build a project
- [Assets](assets.md) — the cooking pipeline a build runs
- [Scenes](scenes.md) — the content a project is made of
