using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// The Sandbox hub. Renders a centered ImGui menu that launches the playable demos and opens the
/// existing engine test scenes. New demos are added by appending one entry to <see cref="Games"/> or
/// <see cref="TestScenes"/> — the menu is data-driven so it grows without touching the layout code.
/// </summary>
public sealed class MainMenuController : EntityBehaviour
{
    // (button label, scene reference relative to the asset root).
    private static readonly (string Label, string Scene)[] Games =
    {
        ("Horde Survival", "Scenes/HordeSurvival.sptscene"),
    };

    private static readonly (string Label, string Scene)[] TestScenes =
    {
        ("Physics Test", "Scenes/PhysicsTest.sptscene"),
        ("Main", "Scenes/Main.sptscene"),
        ("New Scene", "Scenes/NewScene.sptscene"),
    };

    public override void OnImGuiRender()
    {
        Vector2 display = ImGui.GetIO().DisplaySize;
        ImGui.SetNextWindowPos(display * 0.5f, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(420.0f, 0.0f), ImGuiCond.Always);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoTitleBar;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(32.0f, 28.0f));
        if (ImGui.Begin("SandboxHub", flags))
        {
            ImGui.PushFont(Application.Instance.GetFont("Inter", 48.0f));
            ImGui.Text("Spot Sandbox");
            ImGui.PopFont();
            ImGui.TextDisabled("Engine 0.1 demo hub");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var buttonSize = new Vector2(-1.0f, 44.0f);

            ImGui.Text("Games");
            ImGui.Spacing();
            foreach ((string label, string scene) in Games)
            {
                if (ImGui.Button(label, buttonSize))
                {
                    SceneManager.Load(scene);
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Test Scenes");
            ImGui.Spacing();
            var smallButton = new Vector2(-1.0f, 32.0f);
            foreach ((string label, string scene) in TestScenes)
            {
                if (ImGui.Button(label, smallButton))
                {
                    SceneManager.Load(scene);
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Quit", buttonSize))
            {
                Application.Instance.Quit();
            }

            ImGui.End();
        }

        ImGui.PopStyleVar();
    }
}
