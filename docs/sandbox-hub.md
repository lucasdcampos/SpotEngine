# The Sandbox Hub

The `sandbox/` project is Spot's showcase and central test bed. Instead of booting straight into a
single test scene, it opens into a **Main Menu hub** that lists the playable demos and the engine's
test scenes. New demos are added over time by dropping in a scene plus its scripts and adding one menu
entry — the sandbox is where finished samples and throwaway experiments live side by side.

Everything here is built the ordinary way a Spot game is built: **data-driven `.sptscene` files** plus
**`EntityBehaviour` scripts**, with no engine changes. It doubles as a worked example of the patterns
described in [Scenes](scenes.md), [Scripting](scripting.md), and [Rendering](rendering.md).

## The hub

`Assets/Scenes/MainMenu.sptscene` is the start scene (set via `StartScene` in both `game.manifest` and
`Sandbox.sptproj`). It holds just two entities:

- a primary **orthographic camera** that sets the background color, and
- a **menu entity** carrying the `MainMenuController` script.

`MainMenuController` draws the menu with Dear ImGui in `OnImGuiRender` — the same immediate-mode UI the
editor uses, available to any script at runtime. Its buttons call `SceneManager.Load("Scenes/…")` to
switch scenes at the next frame boundary. The list of games and test scenes is a small array in the
script, so adding a demo is a one-line change.

## Horde Survival

`Assets/Scenes/HordeSurvival.sptscene` is a top-down 2D wave-survival game built from opaque quads
(`Sprite2DComponent`), proving out the 2D pipeline:

- **Camera** — orthographic, so the world maps directly to the screen. It carries a `CameraShake`
  script; gameplay adds "trauma" on hits and kills for a `trauma²` screen shake.
- **Arena** — a fixed play field framed by wall quads, with a few obstacle blocks (entities tagged
  `"Obstacle"`). The player is clamped inside the field and pushed out of obstacles (circle-vs-box);
  bullets are stopped by obstacles too.
- **Player** (`PlayerController`) — a triangle that moves with WASD, rotates to aim at the mouse, and
  fires bolts toward the cursor with a muzzle flash. Aiming maps the cursor from window pixels into
  world space using the camera's zoom and the window size. Taking contact damage flashes it red and
  shakes the screen.
- **Enemies** (`EnemyController`) — several `EnemyKind`s, each a distinct geometric shape (generated
  cut-out sprite textures, see `Shapes`): a red **Grunt** circle, a magenta **Dasher** diamond that
  points at the player, an orange spinning **Spinner** hexagon, and a big purple **Brute** pentagon
  with more health. They home in, drain health on contact, take hits (a white flash), and die in a
  particle burst with a jolt.
- **Projectiles** (`Projectile`) — travel straight, expire on a timer, stop on obstacles, and damage the
  first enemy they overlap, spawning an impact spark.
- **Effects** (`Vfx`) — fire-and-forget particle bursts via short-lived `ParticleSystemComponent`
  emitters (flat-2D, the engine's round dot) for spawns, muzzle flashes, impacts, hits and deaths. A
  self-destruct script (`AutoDestruct`) cleans each emitter up once its particles fade.
- **Game manager** (`HordeGameManager`) — spawns escalating, weighted waves of mixed enemy kinds on a
  coroutine, announces each wave, draws the HUD (health, score, wave, enemies) and the game-over screen,
  and owns the flow (restart, back to menu, and `Esc`).

Collisions are simple distance / box checks rather than the physics system — deterministic and enough
for a demo of this size. Shared state (score, wave, player health, arena bounds, game-over flag) lives
in a small static `HordeGameState` that the manager resets on scene enter, so reloading the scene starts
a clean run. Game objects are sprites — solid quads for the arena, walls, obstacles and bolts, and
generated cut-out shape textures (`Shapes`) for the player and enemies; soft, glowing visuals come from
the particle system. Sprites are z-layered — arena and walls sit behind the gameplay quads — and
particles draw over the whole sprite layer, so everything composites in the right order.

## Adding a new demo

1. Author a scene under `Assets/Scenes/` and its scripts under `Assets/Scripts/` (scripts compile into
   the sandbox assembly and are resolved by class name).
2. Add an entry to the games array in `MainMenuController`.
3. Have the demo return to the hub with `SceneManager.Load("Scenes/MainMenu.sptscene")` (a button and/or
   `Esc`).
4. Cook the assets so the runtime can find the new scene: `spot cook --project sandbox` (or
   `spot run --project sandbox`, which cooks and runs). The standalone runtime loads cooked scenes from
   `Content/`, not the source `Assets/`.
