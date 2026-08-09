# Introduction

## What is Spot?

Spot is a 2D/3D game engine written in C# on .NET. It gives you the building blocks for making a
game — a window and render loop, a scene and entity system, 2D and 3D rendering, model import,
physics, audio, scripting, and a visual editor — plus tooling for packaging your game into a
standalone application.

Spot is **3D-first** and **layered**: you can work at a high level (drop entities into a scene and
let the engine draw, simulate, and play them) or drop down to lower-level rendering when you need
full control.

## The pieces

Spot is made up of a few cooperating parts:

- **The engine** (`Spot.Engine`, namespace `Spot`) is the core library. It owns the window, the main
  loop, scenes, entities, rendering, physics, audio, assets, and scripting. Games and the editor are
  both built on top of it.
- **The editor** is a visual application for building scenes — placing entities, editing their
  components, importing assets, and testing your game in a play mode.
- **The build tooling** (`Spot.Build` and the `spot` CLI) turns a project into a standalone,
  self-contained application you can distribute. The same logic runs inside the editor and on the
  command line.
- **The sandbox** is a real, data-driven showcase project used to exercise the engine.

## How a game runs

At its heart, Spot runs a **main loop**. Each frame it processes input and window events, updates
the active scene (running your game logic, scripts, physics, and audio), and renders the scene to
the window. You provide the content — scenes full of entities — and the engine drives them. See
[Architecture](architecture.md) for the frame in detail.

A running game creates an application and hands it a starting scene (or a scene named in its project
config). From there the engine takes over the loop and calls into your scene each frame.

## A note on resilience

**Spot is built to never crash.** A misbehaving script, a broken scene file, or a bad asset is
logged and skipped rather than allowed to take down the process. The main loop wraps each frame in a
recovery boundary, scripts that throw are quarantined, and loaders catch and log instead of
throwing. This means one mistake never takes down the editor or your game, and you can keep
iterating while you track the problem down.

## Where to go next

- To understand the runtime model, read [Architecture](architecture.md).
- To understand how content is organized, start with [Scenes](scenes.md).
- To understand game objects, read [Entities & Components](entities-and-components.md).
- To add behavior, see [Scripting](scripting.md).
- To build and ship, see [Projects & Building a Game](projects-and-building.md).
