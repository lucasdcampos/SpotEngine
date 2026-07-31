using System.Runtime.CompilerServices;
using Spot.Core;
using Xunit;

// The build tooling mutates the static Project.Active; run tests serially so they don't collide.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Spot.Build.Tests;

internal static class TestBootstrap
{
    // Defensive: some engine paths reachable from the build tooling log through Log, which throws
    // if uninitialized. Initialize once when the test assembly loads.
    [ModuleInitializer]
    internal static void Initialize() => Log.Init();
}
