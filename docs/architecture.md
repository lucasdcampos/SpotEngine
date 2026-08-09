# Architecture

This page describes the runtime model: what drives a running game and how the parts cooperate each
frame.

## The application and the main loop

An **application** owns the window and the main loop. You create one, optionally hand it a starting
scene, and call run; it then loops until the window closes (a close request can be vetoed — for
example to confirm unsaved work). Each frame the loop does three things:

1. **Poll events** — drain window and input events for the frame.
2. **Update** — advance the frame clock, finish any pending asset uploads, apply a queued scene
   switch, update the active scene, then update the engine services.
3. **Render** — clear the screen, render the active scene, then render the UI (ImGui) layer.

Every one of these phases runs inside a recovery boundary. If a phase throws, the engine logs it and
continues to the next frame rather than crashing — and a fault that repeats every frame is collapsed
in the log instead of flooding it. This is the backbone of the engine's "never crash" rule.

## Services

Alongside your scene, the application hosts a small set of **engine services** — long-lived
subsystems initialized once and updated every frame. The core services are **graphics** (the GL
context and renderer), **audio** (the sound device), and **ImGui** (the editor/UI layer). Services
run on the real frame time so pausing or slow motion in gameplay never starves them.

## Systems

Where services are global subsystems, **systems** are the per-frame logic that walks the active
scene and acts on entities with the right components. When a scene updates in play mode it runs its
systems in order — physics (character controllers, then 2D and 3D simulation, then collision
dispatch), audio, and finally scripts — and then flushes any entities queued for destruction. You
rarely call systems directly; you add components and the systems do the rest. See
[Entities & Components](entities-and-components.md).

## Time

The engine publishes a frame clock each update. Two things matter:

- **Delta time** is the seconds elapsed since the previous frame. Gameplay reads a *scaled* delta,
  which respects a global time scale (so you can pause or slow-mo the game), while engine services
  read the *unscaled* real delta so they keep running regardless.
- **The delta is clamped** to an upper bound. A hitch — a window drag, a GC pause, a heavy asset
  load — can't feed a huge time step into physics or scripts and explode the simulation; a stalled
  frame simply runs in slow motion instead.

## Input and the developer console

Input is polled each frame (keyboard, mouse, cursor state) rather than delivered only as events, so
gameplay reads the current state in its update. A built-in **developer console** overlays the game
for logging and commands; while it (or a text field) is focused, the engine withholds game input so
typing doesn't leak into gameplay.

## Related

- [Scenes](scenes.md) — the lifecycle the loop drives
- [Scripting](scripting.md) — where your per-frame logic runs
- [Rendering](rendering.md) — the render half of the frame
