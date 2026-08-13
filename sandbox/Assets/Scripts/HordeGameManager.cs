using System;
using System.Collections;
using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// Runs the Horde Survival demo: it resets the shared <see cref="HordeGameState"/>, spawns escalating
/// waves of mixed <see cref="EnemyKind"/>s on a coroutine, announces each wave, draws the HUD and the
/// game-over screen, and owns the flow controls (restart, back to menu, and Escape).
/// </summary>
public sealed class HordeGameManager : EntityBehaviour
{
    /// <summary>Seconds before the first wave spawns.</summary>
    public float FirstWaveDelay { get; set; } = 1.5f;

    /// <summary>Seconds between waves.</summary>
    public float TimeBetweenWaves { get; set; } = 6.0f;

    /// <summary>Enemies in the first wave.</summary>
    public int BaseEnemiesPerWave { get; set; } = 6;

    /// <summary>Extra enemies added to each subsequent wave.</summary>
    public int EnemiesPerWaveGrowth { get; set; } = 3;

    /// <summary>Half-width of the play field the player is confined to.</summary>
    public float ArenaHalfWidth { get; set; } = 13.0f;

    /// <summary>Half-height of the play field the player is confined to.</summary>
    public float ArenaHalfHeight { get; set; } = 7.0f;

    /// <summary>The player's starting and maximum health.</summary>
    public float PlayerMaxHealth { get; set; } = 100.0f;

    private readonly Random _rng = new();
    private float _bannerTimer;

    public override void OnCreate()
    {
        HordeGameState.Reset(PlayerMaxHealth, ArenaHalfWidth, ArenaHalfHeight);
        StartCoroutine(RunWaves());
    }

    public override void OnUpdate(float deltaTime)
    {
        if (!HordeGameState.GameOver && HordeGameState.PlayerHealth <= 0.0f)
        {
            HordeGameState.PlayerHealth = 0.0f;
            HordeGameState.GameOver = true;
            OnPlayerDied();
        }

        if (_bannerTimer > 0.0f)
        {
            _bannerTimer -= deltaTime;
        }

        if (Input.GetKeyDown(Key.Escape))
        {
            SceneManager.Load("Scenes/MainMenu.sptscene");
        }
    }

    private void OnPlayerDied()
    {
        CameraShake.Add(1.0f);
        if (Find("Player") is Entity player)
        {
            Vector3 p = player.GetComponent<TransformComponent>().Position;
            Vfx.Burst(Scene, new Vector2(p.X, p.Y), new Vector4(0.3f, 0.8f, 1.0f, 1.0f), 40, 8.0f, 0.4f, 0.7f);
        }
    }

    private IEnumerator RunWaves()
    {
        yield return new WaitForSeconds(FirstWaveDelay);

        while (!HordeGameState.GameOver)
        {
            HordeGameState.Wave++;
            _bannerTimer = 1.6f;

            int count = BaseEnemiesPerWave + (HordeGameState.Wave - 1) * EnemiesPerWaveGrowth;
            for (int i = 0; i < count && !HordeGameState.GameOver; i++)
            {
                EnemyController.Spawn(Scene, RandomEdgePosition(), PickKind(HordeGameState.Wave), HordeGameState.Wave);
                yield return new WaitForSeconds(0.08f);
            }

            yield return new WaitForSeconds(TimeBetweenWaves);
        }
    }

    // Weighted enemy mix: early waves are grunts; tougher kinds phase in as the run escalates.
    private EnemyKind PickKind(int wave)
    {
        double r = _rng.NextDouble();
        if (wave <= 1)
        {
            return EnemyKind.Grunt;
        }

        if (wave == 2)
        {
            return r < 0.7 ? EnemyKind.Grunt : EnemyKind.Dasher;
        }

        if (wave == 3)
        {
            return r < 0.5 ? EnemyKind.Grunt : r < 0.8 ? EnemyKind.Dasher : EnemyKind.Spinner;
        }

        if (r < 0.4) return EnemyKind.Grunt;
        if (r < 0.65) return EnemyKind.Dasher;
        if (r < 0.85) return EnemyKind.Spinner;
        return EnemyKind.Brute;
    }

    // A random point just inside the arena border, so enemies stream in from the edges.
    private Vector2 RandomEdgePosition()
    {
        float w = ArenaHalfWidth - 0.6f;
        float h = ArenaHalfHeight - 0.6f;

        if (_rng.Next(2) == 0)
        {
            float x = (float)(_rng.NextDouble() * 2.0 - 1.0) * w;
            float y = _rng.Next(2) == 0 ? -h : h;
            return new Vector2(x, y);
        }

        float side = _rng.Next(2) == 0 ? -w : w;
        float yy = (float)(_rng.NextDouble() * 2.0 - 1.0) * h;
        return new Vector2(side, yy);
    }

    public override void OnImGuiRender()
    {
        DrawHud();

        if (_bannerTimer > 0.0f && !HordeGameState.GameOver)
        {
            DrawWaveBanner();
        }

        if (HordeGameState.GameOver)
        {
            DrawGameOver();
        }
    }

    private int CountEnemies()
    {
        int n = 0;
        foreach (Entity e in Scene.View<TransformComponent, Sprite2DComponent>())
        {
            if (e.CompareTag("Enemy"))
            {
                n++;
            }
        }

        return n;
    }

    private void DrawHud()
    {
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoInputs;

        ImGui.SetNextWindowPos(new Vector2(24.0f, 24.0f), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16.0f, 12.0f));

        if (ImGui.Begin("HUD", flags))
        {
            float fraction = HordeGameState.PlayerMaxHealth > 0.0f
                ? Math.Clamp(HordeGameState.PlayerHealth / HordeGameState.PlayerMaxHealth, 0.0f, 1.0f)
                : 0.0f;

            ImGui.Text("HEALTH");
            var barColor = new Vector4(1.0f - fraction, fraction, 0.15f, 1.0f);
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
            ImGui.ProgressBar(fraction, new Vector2(240.0f, 22.0f), $"{(int)MathF.Ceiling(HordeGameState.PlayerHealth)}");
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Text($"Score:   {HordeGameState.Score}");
            ImGui.Text($"Wave:    {HordeGameState.Wave}");
            ImGui.Text($"Enemies: {CountEnemies()}");
            ImGui.End();
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawWaveBanner()
    {
        Vector2 display = ImGui.GetIO().DisplaySize;
        ImGui.SetNextWindowPos(new Vector2(display.X * 0.5f, display.Y * 0.28f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.NoNav;

        // Fade out over the banner's life.
        float alpha = Math.Clamp(_bannerTimer / 1.6f, 0.0f, 1.0f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, alpha));
        if (ImGui.Begin("WaveBanner", flags))
        {
            ImGui.PushFont(Application.Instance.GetFont("Inter", 64.0f));
            ImGui.Text($"WAVE {HordeGameState.Wave}");
            ImGui.PopFont();
            ImGui.End();
        }

        ImGui.PopStyleColor(2);
    }

    private void DrawGameOver()
    {
        Vector2 display = ImGui.GetIO().DisplaySize;
        ImGui.SetNextWindowPos(display * 0.5f, ImGuiCond.Always, new Vector2(0.5f, 0.5f));

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.AlwaysAutoResize;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(40.0f, 32.0f));
        if (ImGui.Begin("GameOver", flags))
        {
            ImGui.PushFont(Application.Instance.GetFont("Inter", 48.0f));
            ImGui.Text("GAME OVER");
            ImGui.PopFont();

            ImGui.Spacing();
            ImGui.Text($"You survived {HordeGameState.Wave} wave(s) and scored {HordeGameState.Score}.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var buttonSize = new Vector2(240.0f, 40.0f);
            if (ImGui.Button("Restart", buttonSize))
            {
                SceneManager.Load("Scenes/HordeSurvival.sptscene");
            }

            if (ImGui.Button("Back to Menu", buttonSize))
            {
                SceneManager.Load("Scenes/MainMenu.sptscene");
            }

            ImGui.End();
        }

        ImGui.PopStyleVar();
    }
}
