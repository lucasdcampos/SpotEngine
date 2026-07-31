# Entities & Components

Spot models game objects with **entities** and **components** — a data-oriented approach where an
entity is an identity and its components hold the data.

## Entities

An **entity** is a single "thing" in a scene: a player, a camera, a light, a wall, a pickup. On its
own an entity carries almost no data — it's essentially just an identity that lives inside a scene.
What an entity *is* and *can do* comes entirely from the components attached to it.

Entities can be arranged in a **hierarchy**: an entity can have a parent and children. This is how
you group objects and build compound objects (for example, a vehicle with wheels as children). Moving
a parent conceptually brings its children along.

Entities are lightweight and are created and destroyed through the scene. Destruction is deferred to
the end of the frame, so it's always safe to destroy an entity — even from within its own logic —
without disrupting whatever is currently running.

## Components

A **component** is a piece of data attached to an entity that gives it a specific capability or
property. Components are mostly plain data; the engine's systems read them to decide what to do.

Typical components include:

- **Transform** — position, rotation, and scale. This is the most fundamental component; nearly every
  visible entity has one.
- **A name/tag** — a human-readable label for the entity.
- **Sprite** — a 2D image to draw.
- **Mesh renderer** — a reference to a 3D model (and its material) to draw.
- **Camera** — makes the entity a viewpoint the scene can be rendered through.
- **Directional light** — lights the scene from a direction.
- **Physics body / collider** — gives the entity 2D physics behavior.
- **Scripts** — custom behavior you write (see [Scripting](scripting.md)).

An entity has at most one component of each type. You add, query, and remove components through the
entity.

## Entities + Components = your game objects

Because behavior and appearance come from components, you compose objects rather than inherit them.
A "player" isn't a special class — it's an entity with a transform, a sprite or mesh, maybe a physics
body, and a script. A "camera" is just an entity with a camera component. This keeps objects flexible:
you can mix and match capabilities freely.

## Systems

The engine has **systems** that walk a scene and act on entities that have the right combination of
components. For example, the rendering system draws every entity that has both a transform and
something visible (a sprite or a mesh); the scripting system runs every entity's scripts. You mostly
don't interact with systems directly — you add components, and the systems do the rest.

## Related

- [Scenes](scenes.md) — the container entities live in
- [Scripting](scripting.md) — adding custom behavior to entities
- [Rendering](rendering.md) — how visible components are drawn
