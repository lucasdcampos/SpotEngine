namespace Spot.Core;

/// <summary>
/// Defines a service that hooks into the application's lifecycle.
/// </summary>
public interface IEngineService
{
    /// <summary>
    /// Called once when the application starts, after the window and graphics context are created.
    /// </summary>
    void Init(Application app);

    /// <summary>
    /// Called every frame before rendering.
    /// </summary>
    void Update(float deltaTime) {}

    /// <summary>
    /// Called every frame to render UI elements via ImGui.
    /// </summary>
    void ImGuiRender() {}

    /// <summary>
    /// Called once when the application is shutting down.
    /// </summary>
    void Shutdown() {}
}
