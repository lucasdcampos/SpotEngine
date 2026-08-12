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

A host can also supply an optional **debug overlay** — the in-game hierarchy/inspector/time panels
toggled at runtime. Its ImGui panels live in a separate `Spot.DebugUI` assembly rather than in the
engine, so the runtime never carries the authoring UI; a game opts in by referencing that assembly and
setting `Application.Debugger`. The editor hosts it automatically.

## Systems

Where services are global subsystems, **systems** are the per-frame logic that walks the active
scene and acts on entities with the right components. Each scene owns an ordered **system registry**,
seeded with the engine's built-in systems and run every play-mode frame in a fixed order — character
controllers, 2D physics, 3D physics (simulation plus collision dispatch), animation, particles, audio,
and finally scripts — after which the scene flushes any entities queued for destruction. Scripts run
last, so a script's update sees the world after physics has resolved it that frame.

You rarely call systems directly; you add components and the built-in systems do the rest. To add your
own simulation, implement `ISystem` (or wrap a callback in `DelegateSystem`) and register it with
`Scene.RegisterSystem`, choosing where it runs relative to the built-ins via the `SystemOrder` slots.
Every system runs inside a guard, so a faulty one is logged once and skipped rather than taking the
frame down. See [Entities & Components](entities-and-components.md).

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

The console renders through a single `DevConsole` but supports two visual skins via
`DrawContents(ConsolePresentation)`. `Runtime` (the default, used by the floating in-game overlay)
keeps an opaque, Source-engine inspired command line that stays legible on top of live gameplay
regardless of the game's colors. `Editor` renders a clean, theme-driven panel: the log region and
input frame derive from the active editor theme (inset `ChildBg`, hairline `Border`, native
`FrameBg`) and there is no Submit button — Enter submits — so it reads as a native dockable panel.
The editor's `ConsolePanel` requests the `Editor` skin.

Logging goes through `Log` (Serilog under the hood, with a core `SPOT` logger and a client `APP`
logger). Besides the console and the in-app developer console, everything at `Information` and above is
persisted to a **rolling log file** so a bad session or a shipped-build crash leaves something to
diagnose after the process is gone. The file lives in a `logs/` folder next to the executable
(`logs/spot.log`, rolled daily, capped at 50 MB with the last 7 files kept). If the folder can't be
created, file logging is skipped and the app keeps running — logging never takes the process down.

## Related

- [Scenes](scenes.md) — the lifecycle the loop drives
- [Scripting](scripting.md) — where your per-frame logic runs
- [Rendering](rendering.md) — the render half of the frame
