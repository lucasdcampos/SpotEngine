using System.Numerics;
using ImGuiNET;
using Spot.Scenes;

namespace Spot.Game;

public class PlayerUI : EntityBehaviour
{
    private int _hp = 100;

    public override void OnImGuiRender()
    {
        var io = ImGui.GetIO();
        Vector2 screenSize = io.DisplaySize;

        ImGuiWindowFlags crosshairFlags = ImGuiWindowFlags.NoDecoration | 
                                 ImGuiWindowFlags.AlwaysAutoResize | 
                                 ImGuiWindowFlags.NoSavedSettings | 
                                 ImGuiWindowFlags.NoFocusOnAppearing | 
                                 ImGuiWindowFlags.NoNav | 
                                 ImGuiWindowFlags.NoBackground |
                                 ImGuiWindowFlags.NoInputs;

        // 1. Crosshair no centro da tela (apenas um ponto branco)
        Vector2 center = screenSize * 0.5f;
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        
        if (ImGui.Begin("Crosshair", crosshairFlags))
        {
            var drawList = ImGui.GetWindowDrawList();
            Vector2 p = ImGui.GetWindowPos(); // Posição central devido ao pivô 0.5
            
            uint color = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)); // Branco
            float radius = 3.0f;

            drawList.AddCircleFilled(p, radius, color);
            
            ImGui.End();
        }

        // 2. HP no canto inferior esquerdo (estilo Valve)
        ImGuiWindowFlags hpFlags = ImGuiWindowFlags.NoDecoration | 
                                 ImGuiWindowFlags.AlwaysAutoResize | 
                                 ImGuiWindowFlags.NoSavedSettings | 
                                 ImGuiWindowFlags.NoFocusOnAppearing | 
                                 ImGuiWindowFlags.NoNav | 
                                 ImGuiWindowFlags.NoInputs; 
        // Note que removemos o NoBackground aqui para ter o fundo da janela

        float pad = 40.0f;
        ImGui.SetNextWindowPos(new Vector2(pad, screenSize.Y - pad), ImGuiCond.Always, new Vector2(0.0f, 1.0f));

        // Deixando o background (campo) maior ajustando o padding
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(30.0f, 20.0f));
        // Deixando o fundo escuro transparente para contraste
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 0.5f));

        if (ImGui.Begin("HUD_HP", hpFlags))
        {
            // Laranja clássico do Half-Life
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.6f, 0.0f, 1.0f)); 
            
            // Deixando a fonte maior ainda
            ImGui.SetWindowFontScale(4.0f);
            ImGui.Text($"+ {_hp}");
            ImGui.SetWindowFontScale(1.0f);
            
            ImGui.PopStyleColor();
            ImGui.End();
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }
}
