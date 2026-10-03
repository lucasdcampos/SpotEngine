using Xunit;

// The framework keeps process-wide state (Renderer.Device, Input, Log's sinks, FileSystem, the model registry).
// Run tests serially so those statics cannot interfere across collections.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
