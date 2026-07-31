using System;
using System.Collections.Generic;
using Spot.Scenes;

namespace Spot.Engine.Tests;

public class SceneManagerTests : IDisposable
{
    private sealed class RecordingScene : Scene
    {
        private readonly List<string> _log;
        private readonly string _id;

        public int Enters;
        public int Exits;

        public RecordingScene(string id, List<string> log)
        {
            _id = id;
            _log = log;
        }

        public override void OnEnter()
        {
            Enters++;
            _log.Add($"{_id}:enter");
        }

        public override void OnExit()
        {
            Exits++;
            _log.Add($"{_id}:exit");
        }
    }

    // SceneManager holds static state; reset it around each test so they don't bleed into each other.
    public SceneManagerTests() => SceneManager.Shutdown();

    public void Dispose() => SceneManager.Shutdown();

    [Fact]
    public void Load_IsDeferredUntilApplyPendingSwitch()
    {
        var log = new List<string>();
        var scene = new RecordingScene("A", log);

        SceneManager.Load(scene);
        Assert.Null(SceneManager.Current); // not switched yet
        Assert.Empty(log);                 // OnEnter not called yet

        SceneManager.ApplyPendingSwitch();

        Assert.Same(scene, SceneManager.Current);
        Assert.Equal(1, scene.Enters);
    }

    [Fact]
    public void ApplyPendingSwitch_ExitsOldSceneBeforeEnteringNew()
    {
        var log = new List<string>();
        var first = new RecordingScene("A", log);
        var second = new RecordingScene("B", log);

        SceneManager.Load(first);
        SceneManager.ApplyPendingSwitch();

        SceneManager.Load(second);
        SceneManager.ApplyPendingSwitch();

        Assert.Same(second, SceneManager.Current);
        Assert.Equal(1, first.Exits);
        Assert.Equal(1, second.Enters);
        Assert.Equal(new[] { "A:enter", "A:exit", "B:enter" }, log);
    }

    [Fact]
    public void ApplyPendingSwitch_WithNothingPending_IsNoOp()
    {
        SceneManager.ApplyPendingSwitch();
        Assert.Null(SceneManager.Current);
    }
}
