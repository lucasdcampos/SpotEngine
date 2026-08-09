# Assets

An **asset** is a piece of content your game uses: a model, a texture, a material, an audio clip, a
prefab, a scene. Spot separates the *source* asset you author from the *cooked* artifact your game
ships, and ties the two together with stable identities.

## Identity: GUIDs and `.meta` files

Every source asset gets a stable **GUID**, recorded in a `.meta` sidecar file next to it. References
between assets — a scene pointing at a model, a material pointing at a texture — are stored as
`guid:` references rather than file paths. This means you can move or rename a source file without
breaking the things that point at it: the identity travels in the `.meta`, not the path.

## Cooking

Source files (a `.png`, an `.fbx`/`.gltf`, a `.wav`) aren't loaded directly by a shipped game.
Instead they're **cooked** into engine-native artifacts — for example textures, meshes, and audio in
compact runtime formats — written into a project's content folder alongside a **manifest** that maps
each GUID to its cooked artifact. Cooking is done by the `spot cook` command (and as part of a build;
see [Projects & Building a Game](projects-and-building.md)).

A companion **migrate** step generates any missing `.meta` sidecars and rewrites older path-based
references in scenes and materials to `guid:` references, so existing content moves onto the pipeline.

## How content is resolved at runtime

The engine resolves asset references against a single **content root**:

- In the **editor**, that root is the project's source `Assets/` directory, so you see your content
  as you author it.
- In a **shipped game**, the root is the cooked content folder, and a loaded manifest turns `guid:`
  references into cooked artifacts. A shipped game loads cooked content, never source assets.

Relative paths in scenes and materials resolve against this root, so the same scene file works in the
editor and in a build. Loading is asynchronous where it can be (models finish their GPU upload before
the frame that needs them), and every loader catches and logs bad data rather than throwing — a
missing or corrupt asset degrades gracefully instead of crashing.

## Prefabs

A **prefab** is a reusable entity template saved as an asset. You author an entity (with its
components, children, and scripts) once, then instantiate copies of it into scenes. An instance
carries a reference back to its prefab, so the template stays the single source of truth.

## Related

- [Projects & Building a Game](projects-and-building.md) — cooking as part of producing a build
- [Rendering](rendering.md) and [Audio](audio.md) — the systems that consume cooked assets
- [The Editor](editor.md) — importing and browsing assets
