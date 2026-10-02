using Xunit;

// The engine relies on global mutable statics (SceneManager.Current, Physics2DSystem.Gravity, Renderer.Device,
// Log's sinks). Run tests serially so those statics cannot interfere across collections. Logging needs no
// setup: Log writes to the terminal until sinks are configured, and never throws.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
