# Entities & Components

Spot models game objects with **entities** and **components** — a data-oriented approach where an
entity is an identity and its components hold the data.

## Entities

An **entity** is a single "thing" in a scene: a player, a camera, a light, a wall, a pickup. On its
own an entity is just a lightweight handle — an identity that lives inside a scene. What an entity
*is* and *can do* comes entirely from the components attached to it. Every entity is created with a
label (its name, tag, and enabled flag), a place in the hierarchy, and a transform.

Entities can be arranged in a **hierarchy**: an entity can have a parent and children. This is how
you group objects and build compound objects (a vehicle with its wheels as children). A parent's
transform applies to its children, and disabling or destroying a parent applies to its whole
subtree.

Entities are created and destroyed through the scene. Destruction is **deferred** to the end of the
frame, so it's always safe to destroy an entity — even from within its own logic — without
disrupting whatever is currently running.

## Components

A **component** is a piece of data attached to an entity that gives it a specific capability or
property. Components are mostly plain data; the engine's systems read them to decide what to do. An
entity has at most one component of each type, and you add, query, and remove components through the
entity handle.

The built-in components include:

| Area | Components |
|---|---|
| Core | **Transform** (position/rotation/scale), the label (name, tag, enabled), and the hierarchy relationship |
| 2D | **Sprite** — an image to draw |
| 3D | **Mesh Renderer** — a model and its material; **Camera**; **Light** (directional or point); **Skybox**; **Dynamic Clouds** |
| Effects | **Particle System** — CPU-simulated batched particle emitter |
| Post | **Post Processing** — per-scene tone-mapping/bloom/vignette look |
| Physics | **Physics Body** (2D/3D), **Box/Sphere/Capsule Colliders**, **Character Controller** |
| Audio | **Audio Source**, **Audio Listener** |
| Content | **Prefab** — an instance of a reusable entity template |
| Behavior | **Scripts** — custom logic you write (see [Scripting](scripting.md)) |

Nearly every visible entity has a Transform; the rest are mixed and matched freely.

## Identity: names and tags

Every entity carries a **name** (a human-readable label, not required to be unique) and a **tag** (a
classification for lookup). You can find the first entity with a given name, the first with a given
tag, or every entity with a tag. Tags are the idiomatic way to categorize objects — "Enemy",
"Pickup", "Ground" — without giving them special types.

## Entities + Components = your game objects

Because behavior and appearance come from components, you **compose** objects rather than inherit
them. A "player" isn't a special class — it's an entity with a transform, a sprite or mesh, maybe a
physics body, and a script. A "camera" is just an entity with a camera component. This keeps objects
flexible: you can add or remove capabilities at any time.

## Systems

The engine's **systems** walk the scene and act on entities that have the right combination of
components — the rendering system draws everything with a transform and something visible; the
physics system simulates bodies with colliders; the script system runs every entity's scripts. You
mostly don't touch systems directly: you add components, and the systems do the rest. See
[Architecture](architecture.md).

## Related

- [Scenes](scenes.md) — the container entities live in
- [Scripting](scripting.md) — adding custom behavior to entities
- [Rendering](rendering.md), [Physics](physics.md), [Audio](audio.md) — the systems behind the components
