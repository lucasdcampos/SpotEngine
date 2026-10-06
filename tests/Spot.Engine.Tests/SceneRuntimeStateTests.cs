using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.UI;

namespace Spot.Engine.Tests;

/// <summary>
/// What play mode builds beside a scene's entities — its UI tree, its render passes — must not outlive play: the
/// editor stops a game by destroying its scripts and restoring the same scene instance, and anything left over
/// would keep drawing over the edit-mode viewport (and pile up with every play).
/// </summary>
public class SceneRuntimeStateTests
{
    private const float Dt = 1f / 60f;

    // Builds UI and a render pass when it starts, and — like a careless script — never removes them.
    private sealed class LeavesThingsBehind : Component
    {
        public override void OnStart()
        {
            UI.Text("Score");
            Scene.AddRenderPass(new DelegateRenderPass(RenderStage.Overlay, _ => { }));
        }
    }

    [Fact]
    public void ClearRuntimeState_DropsTheUIAndRenderPassesAScriptLeftBehind()
    {
        var scene = new Scene();
        scene.Instantiate().AddComponent(new LeavesThingsBehind());
        scene.UpdateRuntime(Dt);
        Assert.NotNull(scene.UIRootOrNull);
        Assert.Equal(1, scene.RenderPasses.Count);

        ComponentSystem.DestroyAll(scene);
        scene.ClearRuntimeState();

        Assert.Null(scene.UIRootOrNull);
        Assert.Equal(0, scene.RenderPasses.Count);
    }

    [Fact]
    public void ClearRuntimeState_LetsUICanvasesInstantiateTheirDocumentAgain()
    {
        var scene = new Scene();
        var canvas = scene.Instantiate().AddComponent(new UICanvas { DocumentRef = "UI/Hud.sptui" });
        canvas.Instantiated = true;
        canvas.Instances.Add(scene.UI.Panel());

        scene.ClearRuntimeState();

        Assert.False(canvas.Instantiated);
        Assert.Empty(canvas.Instances);
    }

    [Fact]
    public void Clear_AlsoDropsTheRuntimeState()
    {
        var scene = new Scene();
        scene.UI.Button("Play");
        scene.AddRenderPass(new DelegateRenderPass(RenderStage.AfterOpaque, _ => { }));

        scene.Clear();

        Assert.Null(scene.UIRootOrNull);
        Assert.Equal(0, scene.RenderPasses.Count);
    }

    [Fact]
    public void AfterClearing_TheSceneBuildsAFreshUIRoot()
    {
        var scene = new Scene();
        UIRoot first = scene.UI;
        first.Text("Old");

        scene.ClearRuntimeState();

        UIRoot second = scene.UI;
        Assert.NotSame(first, second);
        Assert.Empty(second.Children);
    }
}
