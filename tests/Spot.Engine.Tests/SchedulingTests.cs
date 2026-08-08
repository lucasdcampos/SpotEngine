using System.Collections;
using System.Numerics;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Engine.Tests;

public class SchedulingTests
{
    private sealed class WaitSecondsBehaviour : EntityBehaviour
    {
        public int Phase;

        public override void OnCreate() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Phase = 1;
            yield return new WaitForSeconds(1.0f);
            Phase = 2;
        }
    }

    private sealed class WaitFramesBehaviour : EntityBehaviour
    {
        public int Phase;

        public override void OnCreate() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Phase = 1;
            yield return new WaitForFrames(2);
            Phase = 2;
        }
    }

    private sealed class WaitUntilBehaviour : EntityBehaviour
    {
        public bool Gate;
        public int Phase;

        public override void OnCreate() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Phase = 1;
            yield return new WaitUntil(() => Gate);
            Phase = 2;
        }
    }

    private sealed class NestedBehaviour : EntityBehaviour
    {
        public int Phase;

        public override void OnCreate() => StartCoroutine(Parent());

        private IEnumerator Parent()
        {
            Phase = 1;
            yield return Child();
            Phase = 3;
        }

        private IEnumerator Child()
        {
            Phase = 2;
            yield return null;
        }
    }

    private sealed class LoopingBehaviour : EntityBehaviour
    {
        public int Ticks;
        public Coroutine? Handle;

        public override void OnCreate() => Handle = StartCoroutine(Run());

        public void Stop() => StopCoroutine(Handle!);

        private IEnumerator Run()
        {
            while (true)
            {
                Ticks++;
                yield return null;
            }
        }
    }

    private sealed class ThrowingCoroutineBehaviour : EntityBehaviour
    {
        public int Updates;

        public override void OnCreate() => StartCoroutine(Boom());

        public override void OnUpdate(float deltaTime) => Updates++;

        private IEnumerator Boom()
        {
            yield return null;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class InvokeBehaviour : EntityBehaviour
    {
        public int Count;

        public override void OnCreate() => Invoke(() => Count++, 1.0f);
    }

    private sealed class InvokeRepeatingBehaviour : EntityBehaviour
    {
        public int Count;

        public override void OnCreate() => InvokeRepeating(() => Count++, 0.0f, 1.0f);

        public void Cancel() => CancelInvoke();
    }

    private sealed class TweenBehaviour : EntityBehaviour
    {
        public float Value = -1.0f;
        public bool Completed;

        public override void OnCreate() =>
            Tween(0.0f, 10.0f, 1.0f, v => Value = v, Ease.Linear, () => Completed = true);
    }

    [Fact]
    public void WaitForSeconds_ResumesAfterDurationElapses()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddScript(new WaitSecondsBehaviour());

        ScriptSystem.Update(scene, 0.1f);   // OnCreate + first coroutine step
        Assert.Equal(1, script.Phase);

        ScriptSystem.Update(scene, 0.5f);   // 0.5s elapsed, still waiting
        Assert.Equal(1, script.Phase);

        ScriptSystem.Update(scene, 0.5f);   // 1.0s elapsed, resumes
        Assert.Equal(2, script.Phase);
    }

    [Fact]
    public void WaitForFrames_ResumesAfterFrameCount()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddScript(new WaitFramesBehaviour());

        ScriptSystem.Update(scene, 0.016f); // suspends on WaitForFrames(2)
        Assert.Equal(1, script.Phase);

        ScriptSystem.Update(scene, 0.016f); // frame 1
        Assert.Equal(1, script.Phase);

        ScriptSystem.Update(scene, 0.016f); // frame 2, resumes
        Assert.Equal(2, script.Phase);
    }

    [Fact]
    public void WaitUntil_ResumesWhenPredicateBecomesTrue()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddScript(new WaitUntilBehaviour());

        ScriptSystem.Update(scene, 0.1f);
        ScriptSystem.Update(scene, 0.1f);
        Assert.Equal(1, script.Phase); // predicate still false

        script.Gate = true;
        ScriptSystem.Update(scene, 0.1f);
        Assert.Equal(2, script.Phase);
    }

    [Fact]
    public void NestedCoroutine_RunsToCompletionBeforeParentResumes()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddScript(new NestedBehaviour());

        ScriptSystem.Update(scene, 0.1f); // parent starts child; child runs to its yield
        Assert.Equal(2, script.Phase);

        ScriptSystem.Update(scene, 0.1f); // child finishes, parent resumes
        Assert.Equal(3, script.Phase);
    }

    [Fact]
    public void StopCoroutine_HaltsIt()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddScript(new LoopingBehaviour());

        ScriptSystem.Update(scene, 0.1f);
        ScriptSystem.Update(scene, 0.1f);
        Assert.Equal(2, script.Ticks);

        script.Stop();
        ScriptSystem.Update(scene, 0.1f);
        Assert.Equal(2, script.Ticks); // no further ticks after stopping
        Assert.False(script.Handle!.IsRunning);
    }

    [Fact]
    public void ThrowingCoroutine_IsStoppedWithoutFaultingTheScript()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddScript(new ThrowingCoroutineBehaviour());

        var exception = Record.Exception(() =>
        {
            ScriptSystem.Update(scene, 0.1f); // coroutine suspends on yield null
            ScriptSystem.Update(scene, 0.1f); // coroutine resumes and throws
            ScriptSystem.Update(scene, 0.1f);
        });

        Assert.Null(exception);
        Assert.False(script.Faulted);   // a coroutine fault does not fault the whole script
        Assert.Equal(3, script.Updates); // OnUpdate keeps running
    }

    [Fact]
    public void Invoke_FiresOnceAfterDelay()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddScript(new InvokeBehaviour());

        ScriptSystem.Update(scene, 0.5f);
        Assert.Equal(0, script.Count);

        ScriptSystem.Update(scene, 0.5f); // 1.0s elapsed
        Assert.Equal(1, script.Count);

        ScriptSystem.Update(scene, 1.0f); // does not fire again
        Assert.Equal(1, script.Count);
    }

    [Fact]
    public void InvokeRepeating_FiresOnCadenceUntilCancelled()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddScript(new InvokeRepeatingBehaviour());

        ScriptSystem.Update(scene, 1.0f); // fires at t=0 with zero delay
        Assert.Equal(1, script.Count);

        ScriptSystem.Update(scene, 1.0f);
        Assert.Equal(2, script.Count);

        script.Cancel();
        ScriptSystem.Update(scene, 1.0f);
        Assert.Equal(2, script.Count); // cancelled: no further fires
    }

    [Fact]
    public void Tween_InterpolatesToTargetAndCompletes()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddScript(new TweenBehaviour());

        Time.NewFrame(0.5f);
        ScriptSystem.Update(scene, 0.5f); // starts tween, Value = start (0)
        Assert.Equal(0.0f, script.Value, 3);

        Time.NewFrame(0.5f);
        ScriptSystem.Update(scene, 0.5f); // halfway
        Assert.Equal(5.0f, script.Value, 3);

        Time.NewFrame(0.5f);
        ScriptSystem.Update(scene, 0.5f); // reaches the end
        Assert.Equal(10.0f, script.Value, 3);
        Assert.True(script.Completed);
    }

    [Fact]
    public void Tween_Vector3_ReachesTarget()
    {
        var scene = new Scene();
        Vector3 result = Vector3.Zero;

        // A tiny script that tweens a captured local instead of a transform, to exercise the Vector3 path.
        scene.Instantiate().AddScript(new VectorTweenBehaviour(v => result = v));

        Time.NewFrame(0.5f);
        ScriptSystem.Update(scene, 0.5f);
        Time.NewFrame(0.5f);
        ScriptSystem.Update(scene, 0.5f);
        Time.NewFrame(0.5f);
        ScriptSystem.Update(scene, 0.5f);

        Assert.Equal(new Vector3(0.0f, 10.0f, 0.0f), result);
    }

    private sealed class VectorTweenBehaviour : EntityBehaviour
    {
        private readonly Action<Vector3> _sink;

        public VectorTweenBehaviour(Action<Vector3> sink) => _sink = sink;

        public override void OnCreate() =>
            Tween(Vector3.Zero, new Vector3(0.0f, 10.0f, 0.0f), 1.0f, _sink);
    }
}
