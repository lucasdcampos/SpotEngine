# Introduction

## What is Spot?

Spot is a 2D/3D game engine written in C# on .NET. It gives you the building blocks for making a
game — a window and render loop, a scene and entity system, 2D and 3D rendering, model import,
scripting, and a visual editor — plus a tool for packaging your game into a standalone application.

Spot is designed to be approachable and layered: you can work at a high level (drop entities into a
scene and let the engine draw them) or drop down to lower-level rendering when you need full control.

## The pieces

Spot is made up of a few cooperating parts:

- **The engine** is the core library. It owns the window, the main loop, scenes, entities,
  rendering, assets, and scripting. Games and the editor are both built on top of it.
- **The editor** is a visual application for building scenes — placing entities, editing their
  components, importing models, and testing your game in a play mode.
- **The build tool** turns a project into a standalone, self-contained application you can
  distribute. It's available both inside the editor and as a `spot` command-line tool.

## How a game runs

At its heart, Spot runs a **main loop**. Each frame it processes input and window events, updates
the active scene (running your game logic, scripts, and physics), and renders the scene to the
window. You provide the content — scenes full of entities — and the engine drives them.

A running game starts by creating an application and handing it a starting scene. From there the
engine takes over the loop and calls into your scene each frame.

## A note on resilience

Spot is built to keep running even when something goes wrong. A misbehaving script, a broken scene
file, or a bad asset is logged and skipped rather than allowed to crash the whole game. This means
you can iterate quickly without a single mistake taking down the editor or your game.

## Where to go next

- To understand how content is organized, start with [Scenes](scenes.md).
- To understand game objects, read [Entities & Components](entities-and-components.md).
- To add behavior, see [Scripting](scripting.md).
- To build and ship, see [Projects & Building a Game](projects-and-building.md).
