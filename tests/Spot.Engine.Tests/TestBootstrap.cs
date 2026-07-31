using System.Runtime.CompilerServices;
using Spot.Core;
using Xunit;

// The engine relies on global mutable statics (SceneManager.Current, Physics2DSystem.Gravity) and an
// initialized logger. Run tests serially so those statics cannot interfere across collections.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Spot.Engine.Tests;

internal static class TestBootstrap
{
    // The engine logs on its error paths (script quarantine, resilient loaders) and Log.CoreError
    // throws if Log.Init() was never called. Initialize once when the test assembly loads so those
    // paths log-and-continue exactly as they do in the running engine.
    [ModuleInitializer]
    internal static void Initialize() => Log.Init();
}
