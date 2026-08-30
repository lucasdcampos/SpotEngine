using System.Linq;
using Spot.Core;
using Spot.UI;
using Spot.UI.Serialization;

namespace Spot.Scenes;

/// <summary>
/// The play-mode system that brings editor-authored UI to life: for each active
/// <see cref="UICanvasComponent"/> it loads the referenced <c>.sptui</c> document once and adds its widgets to
/// the scene's <see cref="Scene.UI"/> tree, applying the document's scale settings. It runs before scripts so
/// a script's <c>OnStart</c> can already look the instantiated widgets up by name. Each canvas is isolated in
/// its own guard, so one broken document can neither crash nor block the rest.
/// </summary>
internal static class UICanvasSystem
{
    public static void Update(Scene scene, float deltaTime)
    {
        _ = deltaTime;

        foreach (Entity entity in scene.View<UICanvasComponent>())
        {
            UICanvasComponent canvas = entity.GetComponent<UICanvasComponent>();
            if (canvas.Instantiated || !canvas.Enabled || !entity.IsActiveInHierarchy())
            {
                continue;
            }

            canvas.Instantiated = true;
            if (string.IsNullOrEmpty(canvas.DocumentRef))
            {
                continue;
            }

            try
            {
                UIRoot document = UISerializer.Load(canvas.DocumentRef);
                scene.UI.ScaleMode = document.ScaleMode;
                scene.UI.ReferenceHeight = document.ReferenceHeight;

                foreach (Widget widget in document.Children.ToArray())
                {
                    scene.UI.Add(widget);
                    canvas.Instances.Add(widget);
                }
            }
            catch (Exception ex)
            {
                Log.CoreWarn("UI canvas on entity {0} failed to instantiate '{1}': {2}", entity.Id, canvas.DocumentRef, ex.Message);
            }
        }
    }
}
