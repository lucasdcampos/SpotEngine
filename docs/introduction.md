# Introduction

## What is Spot?

Spot is a 2D/3D game engine written in C# on .NET. It gives you the building blocks for making a
game — a window and render loop, a scene and entity system, 2D and 3D rendering, model import,
physics, audio, scripting, and a visual editor — plus tooling for packaging your game into a
standalone application.

Spot is **3D-first** and **layered**: you can work at a high level (drop entities into a scene and
let the engine draw, simulate, and play them) or drop down to lower-level rendering when you need
full control — and you never have to leave the engine to do so.

## Three levels

Spot is three libraries stacked on top of each other, each usable on its own:

1. **Core** (`Spot.Framework.Core`) — a window, events, raw input, the GPU, a minimal 2D batch and an
   audio device. No loop: you write it.
2. **Framework** (`Spot.Framework`, plus the optional `Spot.Framework.Assimp`) — a friendly, code-only
   layer in the spirit of raylib or MonoGame: loading files, sprites, text, basic 3D, models, skeletons,
   a mixer, input actions. Still your loop, and your choice of libraries for physics or UI.
3. **Engine** (`Spot.Engine`) — the opinionated engine: the loop, scenes, the lit renderer,
   post-processing, particles, physics, the animator, UI, assets, the editor and the CLI.

See [Levels](levels.md) for what each holds, how a feature finds its level, and the escape hatches that
let engine code reach down to the framework and the raw GPU.

## The pieces

Spot is made up of a few cooperating parts:

- **The framework** (`Spot.Framework.Core` and `Spot.Framework`, namespaces `Spot.Framework.*`) is the
  foundation: the window, input, graphics device, renderers, resources and audio, all driven by code.
- **The engine** (`Spot.Engine`, namespaces `Spot.Engine.*`) builds on the framework. It owns the main
  loop, scenes, entities, the full renderer, physics, audio playback, assets, and scripting. Games made
  in the editor and the editor itself are both built on top of it.
- **The editor** is a visual application for building scenes — placing entities, editing their
  components, importing assets, and testing your game in a play mode.
- **The build tooling** (`Spot.Build` and the `spot` CLI) turns a project into a standalone,
  self-contained application you can distribute. The same logic runs inside the editor and on the
  command line.
- **The sandbox** is a real, data-driven showcase project used to exercise the engine, and the
  **samples** are one small program per framework level.

## How a game runs

At its heart, the engine runs a **main loop**. Each frame it processes input and window events, updates
the active scene (running your game logic, scripts, physics, and audio), and renders the scene to
the window. You provide the content — scenes full of entities — and the engine drives them. See
[Architecture](architecture.md) for the frame in detail. (With only the framework, the loop is yours:
poll the window, update, draw, present.)

A running game creates an application and hands it a starting scene (or a scene named in its project
config). From there the engine takes over the loop and calls into your scene each frame.

## A note on resilience

**Spot is built to never crash.** A misbehaving script, a broken scene file, or a bad asset is
logged and skipped rather than allowed to take down the process. The main loop wraps each frame in a
recovery boundary, scripts that throw are quarantined, and loaders catch and log instead of
throwing. This means one mistake never takes down the editor or your game, and you can keep
iterating while you track the problem down.

These nets belong to the engine, which owns the loop. The framework levels are libraries: they never
take the process down on their own, but they report errors to the code that called them, so your loop
decides what to do.

## Where to go next

- To choose a level, or reach below the one you are on, read [Levels](levels.md).
- To understand the runtime model, read [Architecture](architecture.md).
- To understand how content is organized, start with [Scenes](scenes.md).
- To understand game objects, read [Entities & Components](entities-and-components.md).
- To add behavior, see [Scripting](scripting.md).
- To build and ship, see [Projects & Building a Game](projects-and-building.md).
