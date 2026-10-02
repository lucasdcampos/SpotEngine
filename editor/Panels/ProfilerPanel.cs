using System;
using System.Collections.Generic;
using ImGuiNET;
using System.Numerics;
using Spot.Engine.Rendering;
using Spot.Framework;
using Spot.Framework.Mathematics;
using Spot.DebugUI.UI;

namespace Spot.Editor.Panels;

/// <summary>
/// An ImGui panel that shows per-system CPU frame times and a scrolling total-frame-time graph.
/// </summary>
public sealed class ProfilerPanel
{
    // Rolling history of total frame times (ms) for the line graph.
    private const int HistorySize = 128;
    private readonly float[] _history = new float[HistorySize];
    private int _historyOffset;

    private float _maxMs = 33.33f;  // graph Y-axis ceiling, auto-scaled
    private float _peakMs;
    private bool _paused;

    // Snapshot of the last seen samples, captured when not paused.
    private readonly Dictionary<string, float> _snapshot = new();

    public void OnImGuiRender()
    {
        if (!ImGui.Begin("Profiler"))
        {
            ImGui.End();
            return;
        }

        if (!_paused)
        {
            float frameMs = FrameStats.FrameTimeMs;
            _history[_historyOffset] = frameMs;
            _historyOffset = (_historyOffset + 1) % HistorySize;
            _peakMs = MathF.Max(_peakMs, frameMs);
            _maxMs = MathF.Max(_maxMs, frameMs * 1.1f);

            _snapshot.Clear();
            foreach (var kv in Profiler.Samples)
            {
                _snapshot[kv.Key] = kv.Value;
            }
        }

        var palette = EditorThemeManager.Current.Palette;

        // ---- Controls row ----
        if (_paused)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.3f, 0.2f, 1f));
            if (ImGui.Button("Resume")) { _paused = false; }
            ImGui.PopStyleColor();
        }
        else
        {
            if (ImGui.Button("Pause")) { _paused = true; }
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset Peak")) { _peakMs = 0f; _maxMs = 33.33f; }
        ImGui.SameLine();
        ImGui.TextDisabled($"Peak: {_peakMs:0.0} ms");

        ImGui.Separator();

        // ---- Frame-time graph ----
        float graphH = 80f;
        Vector2 graphSize = new(ImGui.GetContentRegionAvail().X, graphH);

        // Build an overlay label.
        string overlay = $"{FrameStats.FrameTimeMs:0.0} ms  ({FrameStats.Fps:0} fps)";

        // PlotLines needs the values in the right order (oldest first).
        ImGui.PlotLines("##frametimes", ref _history[0], HistorySize, _historyOffset,
            overlay, 0f, _maxMs, graphSize);

        // Budget lines: 60 fps = 16.7 ms, 30 fps = 33.3 ms.
        var drawList = ImGui.GetWindowDrawList();
        Vector2 graphPos = ImGui.GetItemRectMin();
        Vector2 graphMax = ImGui.GetItemRectMax();
        DrawBudgetLine(drawList, graphPos, graphMax, 16.67f, _maxMs, new Vector4(0.2f, 0.8f, 0.2f, 0.5f), "60 fps");
        DrawBudgetLine(drawList, graphPos, graphMax, 33.33f, _maxMs, new Vector4(0.9f, 0.6f, 0.1f, 0.5f), "30 fps");

        ImGui.Separator();

        // ---- Per-system table ----
        ImGui.TextDisabled("System timings (last completed frame)");
        ImGui.Spacing();

        if (ImGui.BeginTable("##profiler_systems", 2,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("System", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("ms / frame", ImGuiTableColumnFlags.WidthFixed, 90f);
            ImGui.TableHeadersRow();

            float totalTracked = 0f;
            foreach (KeyValuePair<string, float> kv in _snapshot)
            {
                float ms = kv.Value;
                totalTracked += ms;

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(kv.Key);
                ImGui.TableSetColumnIndex(1);
                // Color-code: green <4 ms, yellow 4-8 ms, red >8 ms.
                Vector4 col = ms < 4f
                    ? new Vector4(0.5f, 0.9f, 0.5f, 1f)
                    : ms < 8f
                        ? new Vector4(0.9f, 0.8f, 0.3f, 1f)
                        : new Vector4(0.9f, 0.3f, 0.3f, 1f);
                ImGui.TextColored(col, $"{ms:0.000}");
            }

            // "Other" row — frame time not attributed to any tracked system.
            float other = FrameStats.FrameTimeMs - totalTracked;
            if (other > 0.01f)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextDisabled("Other / overhead");
                ImGui.TableSetColumnIndex(1);
                ImGui.TextDisabled($"{other:0.000}");
            }

            ImGui.EndTable();
        }

        ImGui.Separator();

        // ---- Culling ----
        // What the frame actually submitted, and what the two culling stages saved it from submitting.
        // Both stages are toggleable right here so their effect can be A/B'd against the graph above.
        ImGui.TextDisabled("Culling (last 3D pass)");
        ImGui.Spacing();

        int drawn = RendererDebug.VisibleMeshCount;
        int frustumCulled = RendererDebug.CulledMeshCount;
        int occluded = RendererDebug.OccludedMeshCount;
        int considered = drawn + frustumCulled + occluded;

        if (ImGui.BeginTable("##profiler_culling", 2,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Meshes", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, 90f);
            ImGui.TableHeadersRow();

            CullingRow("Drawn", drawn, considered, new Vector4(0.5f, 0.9f, 0.5f, 1f));
            CullingRow("Culled: off screen", frustumCulled, considered, new Vector4(0.6f, 0.75f, 0.95f, 1f));
            CullingRow("Culled: behind an occluder", occluded, considered, new Vector4(0.85f, 0.7f, 0.95f, 1f));
            CullingRow("Occluders rasterized", RendererDebug.OccluderCount, 0, new Vector4(0.7f, 0.7f, 0.7f, 1f));

            ImGui.EndTable();
        }

        ImGui.Spacing();

        bool frustumOn = !RendererDebug.DisableFrustumCulling;
        if (ImGui.Checkbox("Frustum culling", ref frustumOn))
        {
            RendererDebug.DisableFrustumCulling = !frustumOn;
        }

        ImGui.SameLine();
        bool occlusionOn = RenderSettings.OcclusionCulling && !RendererDebug.DisableOcclusionCulling;
        if (ImGui.Checkbox("Occlusion culling", ref occlusionOn))
        {
            RenderSettings.OcclusionCulling = occlusionOn;
            RendererDebug.DisableOcclusionCulling = false;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Skips meshes hidden behind meshes marked as Occluder in the inspector.");
        }

        ImGui.End();
    }

    // One culling row: the count, plus its share of the meshes considered this frame when that is
    // meaningful (it is not, for the occluder count — occluders are not candidates).
    private static void CullingRow(string label, int count, int considered, Vector4 color)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        if (considered > 0)
        {
            ImGui.TextColored(color, $"{count}  ({100f * count / considered:0}%)");
        }
        else
        {
            ImGui.TextColored(color, $"{count}");
        }
    }

    private static void DrawBudgetLine(ImDrawListPtr dl, Vector2 min, Vector2 max,
        float budgetMs, float scaleMs, Vector4 color, string label)
    {
        if (budgetMs > scaleMs) return;
        float t = 1f - (budgetMs / scaleMs);
        float y = min.Y + t * (max.Y - min.Y);
        uint col = ImGui.GetColorU32(color);
        dl.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), col, 1f);
        dl.AddText(new Vector2(min.X + 4f, y - 13f), col, label);
    }
}
