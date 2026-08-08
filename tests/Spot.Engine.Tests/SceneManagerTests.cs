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

    private sealed class Counter : EntityBehaviour
    {
        public int Created;
        public int Destroyed;

        public override void OnCreate() => Created++;

        public override void OnDestroy() => Destroyed++;
    }

    [Fact]
    public void PersistentEntity_SurvivesSceneSwitch_WithLiveScriptState()
    {
        var a = new RecordingScene("A", new List<string>());
        SceneManager.Load(a);
        SceneManager.ApplyPendingSwitch();

        var entity = a.Instantiate("Player");
        var counter = entity.AddScript<Counter>();
        ScriptSystem.Update(a, 0f); // runs OnCreate, marks the script started
        Assert.Equal(1, counter.Created);

        entity.DontDestroyOnLoad();

        var b = new RecordingScene("B", new List<string>());
        SceneManager.Load(b);
        SceneManager.ApplyPendingSwitch();

        // The script is neither destroyed nor recreated: its live state carries over.
        Assert.Equal(0, counter.Destroyed);
        Assert.Equal(1, counter.Created);

        // It now lives in the new scene (rebound), and is gone from the old one.
        Assert.Same(b, counter.Entity.Scene);
        Assert.True(counter.Entity.IsValid);
        Assert.Contains(b.View<LabelComponent>(), x => x.Name == "Player");
        Assert.Empty(a.View<LabelComponent>());
    }

    [Fact]
    public void PersistentEntity_CarriesItsChildren()
    {
        var a = new RecordingScene("A", new List<string>());
        SceneManager.Load(a);
        SceneManager.ApplyPendingSwitch();

        var root = a.Instantiate("Root");
        var child = a.Instantiate("Child");
        child.SetParent(root);
        root.DontDestroyOnLoad();

        var b = new RecordingScene("B", new List<string>());
        SceneManager.Load(b);
        SceneManager.ApplyPendingSwitch();

        Assert.Equal(2, b.View<LabelComponent>().Count);
        var roots = new List<Entity>();
        foreach (var e in b.View<LabelComponent>())
        {
            if (e.Parent == null) roots.Add(e);
        }

        Assert.Single(roots);
        Assert.Equal("Root", roots[0].Name);
        var children = new List<Entity>(roots[0].Children);
        Assert.Single(children);
        Assert.Equal("Child", children[0].Name);
    }

    [Fact]
    public void NonPersistentEntity_IsDestroyedOnSceneSwitch()
    {
        var a = new RecordingScene("A", new List<string>());
        SceneManager.Load(a);
        SceneManager.ApplyPendingSwitch();

        var entity = a.Instantiate("Temp");
        var counter = entity.AddScript<Counter>();
        ScriptSystem.Update(a, 0f);

        var b = new RecordingScene("B", new List<string>());
        SceneManager.Load(b);
        SceneManager.ApplyPendingSwitch();

        Assert.Equal(1, counter.Destroyed);
        Assert.Empty(b.View<LabelComponent>());
    }
}
