using Xunit;
using Spot.Build;

// The build tooling mutates the static Project.Active; run tests serially so they don't collide.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
