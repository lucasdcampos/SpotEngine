# Particle Systems

Spot includes a CPU-based **particle system** for creating visual effects like fire, smoke, dust, magic, and explosions. A particle system is an entity with a `ParticleSystemComponent` attached.

## Overview

Unlike massive GPU-based particle systems, Spot's implementation is a lightweight CPU simulator that emits and manages a pool of camera-facing (or flat) quads. It is designed to be a solid baseline for adding life to your scenes without the complexity of a node graph or custom shaders. 

Particle data and logic reside in two places:
- **`ParticleSystemComponent`**: The data container holding all configuration properties (emission rates, shapes, lifetimes, colors) and the internal simulation state.
- **`ParticleSystem`** & **`ParticleRenderSystem`**: The underlying engine systems that simulate particle aging and motion every frame (only in play mode) and submit batched quads for rendering.

## Emitter Shapes

Particles are spawned from a defined volume (`ParticleEmitterShape`), which determines their starting positions and initial velocity directions:
- **Point**: All particles spawn exactly at the emitter's origin and travel straight up.
- **Box**: Particles spawn anywhere inside a rectangular volume (`BoxSize`) and travel straight up.
- **Sphere**: Particles spawn inside a sphere (`Radius`) and travel radially outward.
- **Cone**: Particles spawn on a flat disc and travel in a cone shape (`ConeAngle`), ideal for fountains, fire, or thruster exhausts.

## Particle Properties

Each particle's visual representation and motion are dictated by parameters in the inspector:
- **Lifetime & Speed**: `StartLifetime` and `StartSpeed` govern how long the particle lives and how fast it moves upon emission.
- **Size & Color**: Particles can shrink or grow from `StartSize` to `EndSize`. Their color transitions smoothly from `StartColor` to `EndColor` (fading out if `EndColor` has 0 alpha).
- **Physics**: You can apply a constant `Gravity` (pulling particles down) and `Damping` (slowing particles over time like air resistance).
- **Randomness**: The `Randomness` parameter (0 to 1) introduces variation to each spawned particle's lifetime, speed, size, and spin, making the effect look organic rather than uniform.

## Simulation Space

The `Space` property controls whether particles are attached to the emitter or the world:
- **Local**: Particles move with the emitter. If the emitter entity moves, all live particles move with it. (Good for glowing halos, forcefields).
- **World**: Particles are left behind in the world once spawned. If the emitter moves, it leaves a trail of particles. (Good for smoke trails, sparks, exhaust).

## Rendering

The particle renderer uses a highly efficient dynamic batcher.
- **Render Mode**: `Billboard3D` makes every quad face the camera (ideal for 3D games). `Flat2D` makes them lay flat on the XY plane (ideal for orthographic 2D games).
- **Blend Mode**: Choose between `Alpha` (standard transparency, good for smoke) and `Additive` (adds color to the scene, good for fire, sparks, and glow).
- **Texture**: Assign a `Texture2D` to customize the look. If left empty, Spot automatically uses a soft, round dot.

## Scripting

The particle system can be controlled at runtime through a simple API:
- `Play()`: Starts or resumes emission.
- `Stop()`: Stops emitting new particles, but currently live particles continue simulating until they die.
- `Clear()`: Immediately kills all active particles.
- `Emit(int count)`: Instantly spawns a burst of `count` particles.
