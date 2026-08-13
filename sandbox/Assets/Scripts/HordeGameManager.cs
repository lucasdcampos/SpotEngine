using System;
using System.Collections;
using System.Numerics;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;
using Spot.UI;

namespace Spot.Game;

/// <summary>
/// Runs the Horde Survival demo: it resets the shared <see cref="HordeGameState"/>, spawns escalating
/// waves of mixed <see cref="EnemyKind"/>s on a coroutine, announces each wave, drives the HUD and the
/// game-over screen (built with the engine's runtime UI), and owns the flow controls (restart, back to
/// menu, and Escape).
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

    private const float HealthBarWidth = 248f;
    private const float BannerDuration = 1.6f;

    private readonly Random _rng = new();
    private float _bannerTimer;

    private Panel _healthFill = null!;
    private Text _healthValue = null!;
    private Text _stats = null!;
    private Text _waveBanner = null!;
    private Panel _gameOverPanel = null!;
    private Text _gameOverSummary = null!;

    public override void OnCreate()
    {
        HordeGameState.Reset(PlayerMaxHealth, ArenaHalfWidth, ArenaHalfHeight);
        BuildUI();
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

        UpdateUI();
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
            _bannerTimer = BannerDuration;

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

    // ---- UI -------------------------------------------------------------------------------------------

    private void BuildUI()
    {
        BuildHud();
        BuildWaveBanner();
        BuildGameOver();
    }

    private void BuildHud()
    {
        Panel hud = UI.Panel();
        hud.Color = new Vector4(0f, 0f, 0f, 0.5f);
        hud.Rect = TopLeft(new Vector2(24f, 24f), new Vector2(280f, 150f));

        Text health = hud.Text("HEALTH");
        health.FontSize = 18f;
        health.Color = new Vector4(0.75f, 0.78f, 0.82f, 1f);
        health.Rect = Local(new Vector2(16f, 12f), new Vector2(248f, 20f));

        Panel track = hud.Panel();
        track.Color = new Vector4(0.08f, 0.08f, 0.10f, 1f);
        track.Rect = Local(new Vector2(16f, 40f), new Vector2(HealthBarWidth, 24f));

        _healthFill = hud.Panel();
        _healthFill.Color = new Vector4(0.15f, 0.85f, 0.20f, 1f);
        _healthFill.Rect = Local(new Vector2(16f, 40f), new Vector2(HealthBarWidth, 24f));

        _healthValue = hud.Text("100");
        _healthValue.FontSize = 16f;
        _healthValue.Align = TextAlign.Center;
        _healthValue.Rect = Local(new Vector2(16f, 43f), new Vector2(HealthBarWidth, 20f));

        _stats = hud.Text(string.Empty);
        _stats.FontSize = 18f;
        _stats.Color = new Vector4(0.90f, 0.91f, 0.94f, 1f);
        _stats.Rect = Local(new Vector2(16f, 78f), new Vector2(248f, 66f));
    }

    private void BuildWaveBanner()
    {
        _waveBanner = UI.Text("WAVE 1");
        _waveBanner.FontSize = 64f;
        _waveBanner.Align = TextAlign.Center;
        _waveBanner.Rect = new UIRect
        {
            Anchor = new Vector2(0.5f, 0.28f),
            Pivot = new Vector2(0.5f, 0.5f),
            Position = Vector2.Zero,
            Size = new Vector2(640f, 90f),
        };
        _waveBanner.Visible = false;
    }

    private void BuildGameOver()
    {
        _gameOverPanel = UI.Panel();
        _gameOverPanel.Color = new Vector4(0.08f, 0.08f, 0.11f, 0.94f);
        _gameOverPanel.Rect = new UIRect
        {
            Anchor = new Vector2(0.5f, 0.5f),
            Pivot = new Vector2(0.5f, 0.5f),
            Position = Vector2.Zero,
            Size = new Vector2(480f, 320f),
        };

        Text title = _gameOverPanel.Text("GAME OVER");
        title.FontSize = 52f;
        title.Align = TextAlign.Center;
        title.Color = new Vector4(0.98f, 0.40f, 0.35f, 1f);
        title.Rect = TopCenter(36f, new Vector2(480f, 60f));

        _gameOverSummary = _gameOverPanel.Text(string.Empty);
        _gameOverSummary.FontSize = 22f;
        _gameOverSummary.Align = TextAlign.Center;
        _gameOverSummary.Color = new Vector4(0.85f, 0.87f, 0.90f, 1f);
        _gameOverSummary.Wrap = true;
        _gameOverSummary.Rect = TopCenter(110f, new Vector2(400f, 60f));

        Button restart = _gameOverPanel.Button("Restart");
        restart.FontSize = 24f;
        restart.Rect = TopCenter(190f, new Vector2(300f, 44f));
        restart.OnClick += () => SceneManager.Load("Scenes/HordeSurvival.sptscene");

        Button back = _gameOverPanel.Button("Back to Menu");
        back.FontSize = 24f;
        back.NormalColor = new Vector4(0.20f, 0.22f, 0.28f, 1f);
        back.Rect = TopCenter(244f, new Vector2(300f, 44f));
        back.OnClick += () => SceneManager.Load("Scenes/MainMenu.sptscene");

        _gameOverPanel.Visible = false;
        _gameOverPanel.Enabled = false;
    }

    private void UpdateUI()
    {
        float max = HordeGameState.PlayerMaxHealth;
        float fraction = max > 0f ? Math.Clamp(HordeGameState.PlayerHealth / max, 0f, 1f) : 0f;
        _healthFill.Rect.Size = new Vector2(HealthBarWidth * fraction, 24f);
        _healthFill.Color = new Vector4(1f - fraction, 0.35f + fraction * 0.5f, 0.20f, 1f);
        _healthValue.Content = ((int)MathF.Ceiling(MathF.Max(0f, HordeGameState.PlayerHealth))).ToString();
        _stats.Content = $"Score     {HordeGameState.Score}\nWave      {HordeGameState.Wave}\nEnemies   {CountEnemies()}";

        bool showBanner = _bannerTimer > 0f && !HordeGameState.GameOver;
        _waveBanner.Visible = showBanner;
        if (showBanner)
        {
            _waveBanner.Content = $"WAVE {HordeGameState.Wave}";
            _waveBanner.Color = new Vector4(1f, 1f, 1f, Math.Clamp(_bannerTimer / BannerDuration, 0f, 1f));
        }

        bool over = HordeGameState.GameOver;
        _gameOverPanel.Visible = over;
        _gameOverPanel.Enabled = over;
        if (over)
        {
            _gameOverSummary.Content = $"You survived {HordeGameState.Wave} wave(s) and scored {HordeGameState.Score}.";
        }
    }

    private static UIRect TopLeft(Vector2 position, Vector2 size) => new()
    {
        Anchor = Vector2.Zero,
        Pivot = Vector2.Zero,
        Position = position,
        Size = size,
    };

    private static UIRect Local(Vector2 position, Vector2 size) => TopLeft(position, size);

    private static UIRect TopCenter(float y, Vector2 size) => new()
    {
        Anchor = new Vector2(0.5f, 0f),
        Pivot = new Vector2(0.5f, 0f),
        Position = new Vector2(0f, y),
        Size = size,
    };
}
