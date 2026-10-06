using Spot.Engine;

namespace Spot.Engine.Tests;

public class TimeTests : IDisposable
{
    public TimeTests() => Time.Reset();

    public void Dispose() => Time.Reset();

    [Fact]
    public void NewFrame_ScalesGameplayTimeButNotUnscaledTime()
    {
        Time.TimeScale = 0.5f;

        Time.NewFrame(0.2f);

        Assert.Equal(0.1f, Time.DeltaTime, 5);
        Assert.Equal(0.2f, Time.UnscaledDeltaTime, 5);
        Assert.Equal(0.1f, Time.ElapsedTime, 5);
        Assert.Equal(0.2f, Time.UnscaledTime, 5);
        Assert.Equal(1, Time.FrameCount);
    }

    [Fact]
    public void TimeScale_IsClampedAtZero()
    {
        Time.TimeScale = -3.0f;

        Assert.Equal(0.0f, Time.TimeScale);
    }

    [Fact]
    public void StepNextFrame_AdvancesOneFixedStepThenClears()
    {
        Time.TimeScale = 0.0f;
        Time.StepNextFrame = true;

        Time.NewFrame(0.016f);
        Assert.Equal(Time.FixedDeltaTime, Time.DeltaTime);
        Assert.False(Time.StepNextFrame);

        Time.NewFrame(0.016f);
        Assert.Equal(0.0f, Time.DeltaTime);
    }

    [Fact]
    public void NewFrame_IgnoresNegativeDeltas()
    {
        Time.NewFrame(-1.0f);

        Assert.Equal(0.0f, Time.UnscaledDeltaTime);
    }

    [Fact]
    public void Tick_FirstFrameIsZeroThenMeasuresRealTime()
    {
        Assert.Equal(0.0f, Time.Tick());

        Thread.Sleep(20);
        float delta = Time.Tick();

        Assert.InRange(delta, 0.015f, 0.1f);
        Assert.Equal(2, Time.FrameCount);
    }

    [Fact]
    public void Tick_ClampsAHitch()
    {
        Time.Tick();
        Thread.Sleep(20);

        Time.Tick(maxDeltaTime: 0.005f);

        Assert.Equal(0.005f, Time.UnscaledDeltaTime, 5);
    }

    [Fact]
    public void Reset_RestoresTheStartupState()
    {
        Time.TimeScale = 2.0f;
        Time.Tick();
        Time.NewFrame(1.0f);

        Time.Reset();

        Assert.Equal((0L, 0.0f, 0.0f, 1.0f), (Time.FrameCount, Time.ElapsedTime, Time.UnscaledTime, Time.TimeScale));
        Assert.Equal(0.0f, Time.Tick()); // the next Tick is a first frame again
    }
}
