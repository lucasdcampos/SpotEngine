# Voxelcraft

An **engine** project (level 3): an infinite world of blocks, generated as you explore it. Walk, sprint, swim
and fly through oceans, beaches, plains, forests, deserts, snowy taiga and snow-capped mountains, dig into caves
and ore, and break and place blocks, while the sun and moon cross the sky. Like the other samples it ships no
binary assets: every block texture is painted in code at startup, and the sky, the water and the lighting are
its own shaders.

```bash
dotnet run --project tools/Spot.Cli -- run --project samples/Voxelcraft            # cook and run
dotnet run --project tools/Spot.Cli -- build windows --project samples/Voxelcraft  # publish (also linux, browser)
```

Or open `Voxelcraft.sptproj` in the editor. The **World** entity holds the seed, the render distance, the length
of a day and the shadow settings; the **Player** entity holds the movement and the field of view.

## Controls

Esc opens the pause menu, which lists them all.

| Input | Action |
|---|---|
| WASD, mouse | Move, look around |
| Space | Jump (hold to keep jumping), swim up, fly up |
| Space twice / F | Fly on or off (landing also ends a flight) |
| Ctrl, or W twice | Sprint |
| Shift | Sneak (you don't fall off edges), fly down |
| Left click | Break a block (hold to keep breaking) |
| Right click | Place the selected block |
| Middle click | Pick the block you look at into the hotbar |
| 1-9, wheel | Select a hotbar slot |
| E | Inventory: click a block to put it in the selected slot |
| T | Time-lapse on or off |
| N | Skip to the next sunrise, noon, sunset or midnight |
| F1 / F3 | Hide the interface / debug screen (frame rate, position, biome, chunks) |
| Esc | Pause (time stops) and show the controls |

## How it works

| Part | Where |
|---|---|
| The world is chunks of 16 × 16 × 192 blocks, one byte each, streamed in nearest first around the player and dropped behind; chunks you changed are kept | `World`, `Chunk` |
| Generation and meshing run on the thread pool (inline within a time budget in the browser); the results are uploaded on the main thread a few milliseconds' worth per frame | `World.Update`, `VoxelWorld` |
| Terrain: Perlin continents, hills and ridged mountains; five biomes from temperature and humidity, with blended grass and leaf colors; tunnels and caverns from 3D noise sampled on a coarse grid; ores; oak, birch and spruce trees that grow across chunk borders; cacti, grass and flowers | `TerrainGenerator`, `Noise` |
| Meshing: only faces next to something see-through; per-corner ambient occlusion and smooth light; sky light poured down each column and flood-filled sideways, block light flood-filled from glowstone across chunk borders; faces lit the same at every corner merged into larger quads (greedy meshing); leaves drawn as a two-sided shell | `ChunkMesher` |
| One index buffer shared by every chunk; each chunk is one draw call for its blocks and one for its water | `ChunkMesh` |
| The renderer, entirely custom render passes inside the engine's HDR frame: a shadow map from the sun (or the moon), the sky, the terrain front to back with view culling, then the water back to front, block-break chips and the selection outline | `WorldRenderer`, `WorldShaders`, `BlockParticles` |
| Day and night: the sun's path, the sky and fog colors, the light and ambient levels, stars, a square sun and moon, drifting blocky clouds | `DayNightCycle`, `WorldShaders.SkyFragment` |
| The player: an axis-by-axis box collider against the blocks, walking, sprinting, sneaking, swimming and flying, the camera, and breaking and placing | `PlayerController` |
| The interface: one custom widget drawing the loading screen, crosshair, hotbar with isometric block icons rendered from the atlas, clock, debug screen, inventory and pause menu | `Game`, `GameHud`, `HudWidget` |
| The art: 16×16 pixel-art textures painted from hashed noise into a 256×256 atlas, and a mask of the texels the biome colors tint | `BlockAtlas`, `Blocks` |

The world is the same for the same seed. Set **Random Seed** on the World entity for a new one every run.

| Path | What it is |
|---|---|
| `Voxelcraft.sptproj` | project config: name, start scene, asset folder |
| `Program.cs` | entry point: creates the application and runs it |
| `Assets/Scenes/Voxelcraft.sptscene` | the scene: the game, the world, the player and camera, the post-processing |
| `Assets/Scripts/` | the scripts, the generator, the mesher, the renderer and its shaders |

`Voxelcraft.csproj` and `Voxelcraft.sln` are regenerated from the `.sptproj` by `spot generate`, `spot run`,
`spot build` and the editor, so they are not committed.
