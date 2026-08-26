using System;
using System.Collections.Generic;
using System.IO;
using Spot.Animation;
using Spot.Scenes;
using Xunit;

namespace Spot.Engine.Tests;

public class AnimatorComponentTests
{
    private static AnimationClip Clip(string name) => new(name, 1.0f, Array.Empty<AnimationChannel>());

    [Fact]
    public void Play_ClipObject_SetsPlayingAndCurrentClip()
    {
        var animator = new AnimatorComponent();
        animator.Play(Clip("Run"));

        Assert.True(animator.IsPlaying);
        Assert.Equal("Run", animator.CurrentClip);

        animator.Stop();
        Assert.False(animator.IsPlaying);
    }

    [Fact]
    public void Play_UnknownClipName_DoesNotThrowAndStaysStopped()
    {
        var animator = new AnimatorComponent();

        // No model/controller means no clips resolve; playing an unknown name logs and no-ops.
        animator.Play("Missing");

        Assert.False(animator.IsPlaying);
        Assert.Null(animator.CurrentClip);
    }

    [Fact]
    public void Parameters_AreNoOps_WithoutController()
    {
        var animator = new AnimatorComponent();

        animator.SetFloat("Speed", 5.0f);
        animator.SetBool("Grounded", true);

        Assert.Equal(0.0f, animator.GetFloat("Speed"));
        Assert.False(animator.GetBool("Grounded"));
        Assert.Null(animator.CurrentState);
    }
}

public class AnimatorControllerTests
{
    // A minimal clip set (durations only matter for exit-time) the runtime can look up by name.
    private static IReadOnlyDictionary<string, AnimationClip> Clips(params string[] names)
    {
        var dict = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            dict[name] = new AnimationClip(name, 1.0f, Array.Empty<AnimationChannel>());
        }

        return dict;
    }

    private static AnimatorController TwoStates(string a, string b)
    {
        return new AnimatorController
        {
            States =
            {
                new AnimatorState { Name = a, Clip = a },
                new AnimatorState { Name = b, Clip = b },
            },
            DefaultState = a,
        };
    }

    [Fact]
    public void Runtime_EntersDefaultState()
    {
        var controller = TwoStates("Idle", "Run");
        var runtime = new AnimatorControllerRuntime(controller);

        Assert.Equal("Idle", runtime.CurrentState);
        Assert.Equal("Idle", runtime.CurrentClip);
    }

    [Fact]
    public void Runtime_FloatConditionTakesTransition_WhenThresholdCrossed()
    {
        var controller = TwoStates("Idle", "Run");
        controller.Parameters.Add(new AnimatorParameter { Name = "Speed", Type = AnimatorParameterType.Float });
        controller.Transitions.Add(new AnimatorTransition
        {
            From = "Idle",
            To = "Run",
            Conditions = { new AnimatorCondition { Parameter = "Speed", Mode = AnimatorConditionMode.Greater, Threshold = 0.1f } },
        });

        var runtime = new AnimatorControllerRuntime(controller);
        var clips = Clips("Idle", "Run");

        runtime.Tick(0.016f, clips);
        Assert.Equal("Idle", runtime.CurrentState);

        runtime.SetFloat("Speed", 1.0f);
        runtime.Tick(0.016f, clips);
        Assert.Equal("Run", runtime.CurrentState);
    }

    [Fact]
    public void Runtime_TriggerFiresOnce_AndIsConsumed()
    {
        var controller = TwoStates("Idle", "Jump");
        controller.Parameters.Add(new AnimatorParameter { Name = "Jump", Type = AnimatorParameterType.Trigger });
        controller.Transitions.Add(new AnimatorTransition
        {
            From = "Idle",
            To = "Jump",
            Conditions = { new AnimatorCondition { Parameter = "Jump" } },
        });

        var runtime = new AnimatorControllerRuntime(controller);
        var clips = Clips("Idle", "Jump");

        runtime.SetTrigger("Jump");
        runtime.Tick(0.016f, clips);

        Assert.Equal("Jump", runtime.CurrentState);
        Assert.False(runtime.GetBool("Jump")); // the taken transition consumed the trigger
    }

    [Fact]
    public void Runtime_ExitTimeGatesTransition_UntilNormalizedTimeReached()
    {
        var controller = TwoStates("A", "B");
        controller.Transitions.Add(new AnimatorTransition { From = "A", To = "B", HasExitTime = true, ExitTime = 0.5f });

        var runtime = new AnimatorControllerRuntime(controller);
        var clips = Clips("A", "B"); // duration 1.0

        runtime.Tick(0.4f, clips); // normalized 0.4 < 0.5
        Assert.Equal("A", runtime.CurrentState);

        runtime.Tick(0.4f, clips); // normalized 0.8 >= 0.5
        Assert.Equal("B", runtime.CurrentState);
    }

    [Fact]
    public void Runtime_BoolEqualsCondition_TakesTransition()
    {
        var controller = TwoStates("Idle", "Run");
        controller.Parameters.Add(new AnimatorParameter { Name = "Grounded", Type = AnimatorParameterType.Bool });
        controller.Transitions.Add(new AnimatorTransition
        {
            From = "Idle",
            To = "Run",
            Conditions = { new AnimatorCondition { Parameter = "Grounded", Mode = AnimatorConditionMode.Equals, Threshold = 1.0f } },
        });

        var runtime = new AnimatorControllerRuntime(controller);
        var clips = Clips("Idle", "Run");

        runtime.Tick(0.016f, clips);
        Assert.Equal("Idle", runtime.CurrentState);

        runtime.SetBool("Grounded", true);
        runtime.Tick(0.016f, clips);
        Assert.Equal("Run", runtime.CurrentState);
    }

    [Fact]
    public void Runtime_AnyStateTransition_TakesPriority()
    {
        var controller = new AnimatorController
        {
            States =
            {
                new AnimatorState { Name = "A", Clip = "A" },
                new AnimatorState { Name = "B", Clip = "B" },
                new AnimatorState { Name = "C", Clip = "C" },
            },
            DefaultState = "A",
            Parameters = { new AnimatorParameter { Name = "X", Type = AnimatorParameterType.Float } },
        };

        // Both are satisfiable; the any-state edge must win because it is evaluated first.
        controller.Transitions.Add(new AnimatorTransition
        {
            From = "A", To = "B",
            Conditions = { new AnimatorCondition { Parameter = "X", Mode = AnimatorConditionMode.Greater, Threshold = 0.0f } },
        });
        controller.Transitions.Add(new AnimatorTransition
        {
            FromAnyState = true, To = "C",
            Conditions = { new AnimatorCondition { Parameter = "X", Mode = AnimatorConditionMode.Greater, Threshold = 0.0f } },
        });

        var runtime = new AnimatorControllerRuntime(controller);
        runtime.SetFloat("X", 1.0f);
        runtime.Tick(0.016f, Clips("A", "B", "C"));

        Assert.Equal("C", runtime.CurrentState);
    }

    [Fact]
    public void Controller_JsonRoundTrips()
    {
        var controller = TwoStates("Idle", "Run");
        controller.Parameters.Add(new AnimatorParameter { Name = "Speed", Type = AnimatorParameterType.Float, DefaultValue = 2.0f });
        controller.States[0].ClipSource = "guid:deadbeef";
        controller.Transitions.Add(new AnimatorTransition
        {
            From = "Idle",
            To = "Run",
            HasExitTime = true,
            ExitTime = 0.75f,
            Conditions = { new AnimatorCondition { Parameter = "Speed", Mode = AnimatorConditionMode.Greater, Threshold = 0.1f } },
        });

        string path = Path.Combine(Path.GetTempPath(), $"anim_{Guid.NewGuid():N}.sptcontroller");
        try
        {
            controller.Save(path);
            AnimatorController loaded = AnimatorController.Load(path);

            Assert.Equal("Idle", loaded.DefaultState);
            Assert.Equal(2, loaded.States.Count);
            Assert.Single(loaded.Parameters);
            Assert.Equal(AnimatorParameterType.Float, loaded.Parameters[0].Type);
            Assert.Equal(2.0f, loaded.Parameters[0].DefaultValue, 3);
            Assert.Equal("guid:deadbeef", loaded.States[0].ClipSource);

            Assert.Single(loaded.Transitions);
            AnimatorTransition t = loaded.Transitions[0];
            Assert.Equal("Idle", t.From);
            Assert.Equal("Run", t.To);
            Assert.True(t.HasExitTime);
            Assert.Equal(0.75f, t.ExitTime, 3);
            Assert.Single(t.Conditions);
            Assert.Equal(AnimatorConditionMode.Greater, t.Conditions[0].Mode);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }
}
