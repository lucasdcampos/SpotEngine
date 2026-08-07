using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Game;

public class PlayerUI : EntityBehaviour
{
    private int _hp = 100;

    public override void OnImGuiRender()
    {
        var io = ImGui.GetIO();
        Vector2 screenSize = io.DisplaySize;

        // 1. Crosshair — círculo no centro da tela
        Vector2 center = screenSize * 0.5f;
        var fg = ImGui.GetForegroundDrawList();
        uint white = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f));
        fg.AddCircle(center, 16f, white, 32, 1.5f);
        fg.AddCircleFilled(center, 2f, white);

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

            // Fonte grande assada no tamanho alvo (Assets/Fonts/Inter.ttf), em vez de escalar a fonte
            // de UI — escalar estica o bitmap do atlas e borra o texto.
            ImGui.PushFont(Application.Instance.GetFont("Inter", 48.0f));
            ImGui.Text($"+ {_hp}");
            ImGui.PopFont();

            ImGui.PopStyleColor();
            ImGui.End();
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }
}
