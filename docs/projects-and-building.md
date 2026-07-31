# Projects & Building a Game

A **project** is your game: its assets, its scenes, and the configuration that ties them together.
Spot's build tooling turns a project into a standalone application you can distribute.

## What a project is

A project is a folder on disk containing:

- An **assets** folder with your content — scenes, models, textures, and so on.
- A **project file** (`.sptproj`) that records the project's name, where its assets live, and which
  scene the game should start on.

The editor and the command-line tool both read and write this format, so you can move between them
freely.

## The build tool

The **build tool** packages a project into a **self-contained, standalone build** — an application
that includes everything it needs to run, so players don't need to install anything extra. It can
target Windows or Linux.

The same tooling is available in two places:

- **Inside the editor**, through its project menu — convenient while you're working.
- **As the `spot` command-line tool** — convenient for scripting, automation, and headless workflows.

Both do the same thing, because the editor uses the same build library under the hood.

## Building a game

Producing a distributable build generally follows these steps:

1. **Create or open a project** — either in the editor, or with the CLI's project-creation command.
2. **Build your content** — lay out scenes and entities, and set the scene the game starts on.
3. **Publish a build** — choose a target platform (Windows or Linux) and let the tool produce a
   standalone build into the project's output folder.

Under the hood the tool generates the necessary project files, bundles the engine, and produces the
final self-contained application. The output is a folder you can zip up and hand to a player.

### Using the command-line tool

The `spot` CLI exposes the same operations. At a high level:

```bash
# Create a new project
dotnet run --project tools/Spot.Cli -- new MyGame --path <dir>

# Publish a standalone build (windows | linux)
dotnet run --project tools/Spot.Cli -- build windows --project <dir>

# List all commands
dotnet run --project tools/Spot.Cli -- help
```

The exact commands and options are printed by `help`; run it to see the current set.

## Related

- [The Editor](editor.md) — the visual way to build a project
- [Scenes](scenes.md) — the content a project is made of
