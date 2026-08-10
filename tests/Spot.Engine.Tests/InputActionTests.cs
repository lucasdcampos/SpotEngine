using Spot.Core;
using Spot.Events;

namespace Spot.Engine.Tests;

public class InputActionTests
{
    private static void Press(Key key) => Input.OnEvent(new KeyPressedEvent(key));

    private static void Release(Key key) => Input.OnEvent(new KeyReleasedEvent(key));

    [Fact]
    public void Action_TracksHeldDownAndUp()
    {
        Input.ResetForTests();
        Input.Bind("forward", Key.W);
        Input.Bind("forward", Key.Up);

        // Frame 1: press W -> active and down-edge, not up.
        Input.NewFrame();
        Press(Key.W);
        Assert.True(Input.GetAction("forward"));
        Assert.True(Input.GetActionDown("forward"));
        Assert.False(Input.GetActionUp("forward"));

        // Frame 2: still held -> active but no fresh down-edge.
        Input.NewFrame();
        Assert.True(Input.GetAction("forward"));
        Assert.False(Input.GetActionDown("forward"));

        // Frame 3: release W -> inactive and up-edge.
        Input.NewFrame();
        Release(Key.W);
        Assert.False(Input.GetAction("forward"));
        Assert.True(Input.GetActionUp("forward"));
    }

    [Fact]
    public void ActionDown_DoesNotRefireWhileAlreadyActive()
    {
        Input.ResetForTests();
        Input.Bind("forward", Key.W);
        Input.Bind("forward", Key.Up);

        Input.NewFrame();
        Press(Key.W);
        Assert.True(Input.GetActionDown("forward"));

        // Pressing the second bound key while the first is still held must not re-fire the down-edge.
        Input.NewFrame();
        Press(Key.Up);
        Assert.True(Input.GetAction("forward"));
        Assert.False(Input.GetActionDown("forward"));
    }

    [Fact]
    public void ActionUp_FiresOnlyWhenEveryBindingReleased()
    {
        Input.ResetForTests();
        Input.Bind("forward", Key.W);
        Input.Bind("forward", Key.Up);

        Input.NewFrame();
        Press(Key.W);
        Input.NewFrame();
        Press(Key.Up);

        // Releasing one while the other stays held keeps the action active.
        Input.NewFrame();
        Release(Key.W);
        Assert.True(Input.GetAction("forward"));
        Assert.False(Input.GetActionUp("forward"));

        // Releasing the last one fires the up-edge.
        Input.NewFrame();
        Release(Key.Up);
        Assert.False(Input.GetAction("forward"));
        Assert.True(Input.GetActionUp("forward"));
    }

    [Fact]
    public void ActionLookup_IsCaseInsensitive()
    {
        Input.ResetForTests();
        Input.Bind("Forward", Key.W);

        Input.NewFrame();
        Press(Key.W);
        Assert.True(Input.GetAction("forward"));
        Assert.True(Input.GetAction("FORWARD"));
    }

    [Fact]
    public void Action_SupportsMouseButtons()
    {
        Input.ResetForTests();
        Input.Bind("fire", MouseButton.Left);

        Input.NewFrame();
        Input.OnEvent(new MouseButtonPressedEvent(MouseButton.Left));
        Assert.True(Input.GetAction("fire"));
        Assert.True(Input.GetActionDown("fire"));
    }

    [Fact]
    public void Unbind_RemovesBindingFromEveryAction()
    {
        Input.ResetForTests();
        Input.Bind("forward", Key.W);
        Input.Bind("run", Key.W); // same physical key drives two actions

        Assert.True(Input.Unbind(InputBinding.Key(Key.W)));
        Assert.False(Input.Unbind(InputBinding.Key(Key.W))); // nothing left to remove

        Input.NewFrame();
        Press(Key.W);
        Assert.False(Input.GetAction("forward"));
        Assert.False(Input.GetAction("run"));

        // Actions left with no bindings are dropped entirely.
        Assert.Empty(Input.GetActionNames());
    }

    [Fact]
    public void UnbindAction_RemovesTheWholeAction()
    {
        Input.ResetForTests();
        Input.Bind("forward", Key.W);
        Input.Bind("forward", Key.Up);

        Assert.True(Input.UnbindAction("forward"));
        Assert.Empty(Input.GetBindings("forward"));
        Assert.False(Input.UnbindAction("forward")); // already gone
    }

    [Fact]
    public void ResetBindingsToDefaults_RestoresTheDefaultSet()
    {
        Input.ResetForTests();
        Input.SetDefaultBindings(new Dictionary<string, InputBinding[]>
        {
            ["forward"] = new[] { InputBinding.Key(Key.W) },
        });

        // Mutate at runtime the way the console would.
        Input.Bind("forward", Key.Up);
        Input.Bind("jump", Key.Space);
        Input.Unbind(InputBinding.Key(Key.W));

        Input.ResetBindingsToDefaults();

        IReadOnlyList<InputBinding> forward = Input.GetBindings("forward");
        Assert.Single(forward);
        Assert.Equal(InputBinding.Key(Key.W), forward[0]);
        Assert.Empty(Input.GetBindings("jump"));
    }
}
