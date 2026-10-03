using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.UI;
using Spot.Framework;
using Spot.Framework.Audio;

namespace ProvingGrounds;

/// <summary>
/// The interface. The corner brand and the performance readout are a UI document (<c>Assets/UI/Hud.sptui</c>, on this
/// entity's UI Canvas) wired by widget name; the HUD proper is <see cref="HudOverlay"/>, a custom widget; and the pause
/// menu is built here from engine widgets restyled by subclassing — buttons, sliders and switches on a frosted panel
/// that swallows the clicks landing on it.
/// </summary>
public sealed class GameHud : Component
{
    private const float MenuWidth = 780.0f;
    private const float MenuHeight = 470.0f;

    private readonly List<(string Label, Vector3 Position)> _landmarks = new();

    private HudKit? _kit;
    private GlassPanel? _menu;
    private MenuButton? _quit;
    private SettingSlider? _sensitivity;
    private SettingSlider? _fov;
    private SettingSlider? _volume;
    private SettingSlider? _ambience;
    private Switch? _bob;
    private Switch? _bloom;
    private Switch? _performance;
    private Text? _performanceText;
    private Widget? _brand;
    private Widget? _tagline;
    private PostProcessingComponent? _post;
    private float _menuOpacity;
    private bool _wasPaused;
    private float _performanceTimer;

    /// <summary>Gets the places the compass points to: every zone, by title.</summary>
    internal IReadOnlyList<(string Label, Vector3 Position)> Landmarks => _landmarks;

    // The UI Canvas instantiates the document before scripts run, so its widgets already exist here.
    public override void OnStart()
    {
        _kit = new HudKit();
        _performanceText = UI.Find<Text>("Performance");
        _brand = UI.Find("Brand");
        _tagline = UI.Find("Tagline");

        foreach ((Entity _, PostProcessingComponent post) in Scene.ViewActive<PostProcessingComponent>())
        {
            _post = post;
            break;
        }

        foreach (Zone zone in Scene.GetComponents<Zone>())
        {
            if (zone.Title == "The Hub") continue;
            _landmarks.Add((zone.Title, zone.Entity.GetComponent<TransformComponent>().WorldPosition));
        }

        UI.Add(new HudOverlay(this, _kit));
        BuildMenu(_kit);
    }

    public override void OnDestroy() => _kit?.Dispose();

    public override void OnUpdate(float deltaTime)
    {
        if (Playground.Current is not { } game || _menu is null) return;

        float dt = Time.UnscaledDeltaTime;
        if (game.Paused && !_wasPaused) SyncControls(game);
        _wasPaused = game.Paused;

        _menuOpacity = HudKit.Approach(_menuOpacity, game.Paused ? 1.0f : 0.0f, dt * 7.0f);
        float ease = 1.0f - (1.0f - _menuOpacity) * (1.0f - _menuOpacity);
        _menu.Opacity = ease;
        _menu.Visible = _menuOpacity > 0.0f;
        _menu.Rect.Position = new Vector2(0.0f, 14.0f * (1.0f - ease));

        if (_brand is not null) _brand.Visible = game.Started;
        if (_tagline is not null) _tagline.Visible = game.Started;

        if (_performanceText is not null)
        {
            _performanceText.Visible = game.Started && game.ShowPerformance;
            _performanceTimer -= dt;
            if (_performanceText.Visible && _performanceTimer <= 0.0f)
            {
                _performanceTimer = 0.25f;
                _performanceText.Content = $"{FrameStats.Fps:0} FPS  ·  {FrameStats.FrameTimeMs:0.0} ms";
            }
        }
    }

    private void BuildMenu(HudKit kit)
    {
        _menu = UI.Add(new GlassPanel(kit) { Name = "Pause Menu", Visible = false, Opacity = 0.0f });
        _menu.Rect = new UIRect { Anchor = new Vector2(0.5f), Pivot = new Vector2(0.5f), Position = Vector2.Zero, Size = new Vector2(MenuWidth, MenuHeight) };

        // Left: what to do.
        _menu.Add(new Caption { Content = "PAUSED", Color = HudColors.Accent, Size = 13.0f, Tracking = 4.0f }).Rect = Place(30.0f, 30.0f, 300.0f, 20.0f);
        _menu.Add(new Caption { Content = "PROVING GROUNDS", Color = HudColors.Ink, Size = 26.0f, Tracking = 4.0f }).Rect = Place(30.0f, 54.0f, 320.0f, 36.0f);

        float y = 116.0f;
        MenuButton resume = AddButton(kit, "Resume", "Esc", ref y);
        MenuButton controls = AddButton(kit, "Controls", "F1", ref y);
        MenuButton reset = AddButton(kit, "Reset the playground", "Bksp", ref y);
        MenuButton respawn = AddButton(kit, "Back to the hub", "", ref y);
        _quit = AddButton(kit, "Quit to desktop", "", ref y);
        _quit.Accent = HudColors.Danger;

        // Hosted in the editor's viewport or a browser tab, quitting is the host's job.
        _quit.Visible = !Display.HasView && !OperatingSystem.IsBrowser();

        resume.OnClick += () => Click(game => game.SetPaused(false));
        controls.OnClick += () => Click(game =>
        {
            game.SetPaused(false);
            game.HelpOpen = true;
        });
        reset.OnClick += () => Click(game =>
        {
            game.ResetPlayground();
            game.SetPaused(false);
        });
        respawn.OnClick += () => Click(game =>
        {
            game.Respawn();
            game.SetPaused(false);
        });
        _quit.OnClick += () => Click(_ => Application.Instance.Quit());

        _menu.Add(new Divider()).Rect = Place(370.0f, 30.0f, 1.0f, MenuHeight - 60.0f);

        // Right: settings, applied live.
        const float x = 400.0f;
        const float width = MenuWidth - x - 34.0f;
        _menu.Add(new Caption { Content = "SETTINGS", Color = HudColors.Accent, Size = 13.0f, Tracking = 4.0f }).Rect = Place(x, 30.0f, width, 20.0f);

        _sensitivity = AddSlider(kit, "Mouse sensitivity", v => $"{v:0.00}x", 0.2f, 3.0f, x, 66.0f, width);
        _fov = AddSlider(kit, "Field of view", v => $"{v:0}°", 60.0f, 110.0f, x, 124.0f, width);
        _volume = AddSlider(kit, "Master volume", v => $"{v * 100.0f:0}%", 0.0f, 1.0f, x, 182.0f, width);
        _ambience = AddSlider(kit, "Wind", v => $"{v * 100.0f:0}%", 0.0f, 1.0f, x, 240.0f, width);
        _sensitivity.OnValueChanged += v => Apply(game => game.MouseSensitivity = v);
        _fov.OnValueChanged += v => Apply(game => game.FieldOfView = MathF.Round(v));
        _volume.OnValueChanged += v => AudioSettings.MasterVolume = v;
        _ambience.OnValueChanged += v => Apply(game => game.AmbienceVolume = v);

        _menu.Add(new Divider()).Rect = Place(x, 304.0f, width, 1.0f);
        _bob = AddSwitch(kit, "Head bob", x, 314.0f, width);
        _bloom = AddSwitch(kit, "Bloom", x, 354.0f, width);
        _performance = AddSwitch(kit, "Show frame rate", x, 394.0f, width);
        _bob.OnValueChanged += on => Apply(game => game.HeadBob = on);
        _bloom.OnValueChanged += on =>
        {
            if (_post is not null) _post.EnableBloom = on;
        };
        _performance.OnValueChanged += on => Apply(game => game.ShowPerformance = on);
    }

    // The menu opens on the current values, whatever changed them since.
    private void SyncControls(Playground game)
    {
        _sensitivity!.Value = game.MouseSensitivity;
        _fov!.Value = game.FieldOfView;
        _volume!.Value = AudioSettings.MasterVolume;
        _ambience!.Value = game.AmbienceVolume;
        _bob!.On = game.HeadBob;
        _bloom!.On = _post?.EnableBloom ?? true;
        _performance!.On = game.ShowPerformance;
    }

    private static void Click(Action<Playground> action)
    {
        Sfx.Play(Sfx.UiClick, 0.6f, 0.0f, 1.0f, AudioMixer.UiBus);
        if (Playground.Current is { } game) action(game);
    }

    private static void Apply(Action<Playground> action)
    {
        if (Playground.Current is { } game) action(game);
    }

    private MenuButton AddButton(HudKit kit, string label, string shortcut, ref float y)
    {
        MenuButton button = _menu!.Add(new MenuButton(kit, label, shortcut) { Name = label.Replace(" ", "") + "Button" });
        button.Rect = Place(24.0f, y, 322.0f, 46.0f);
        y += 52.0f;
        return button;
    }

    private SettingSlider AddSlider(HudKit kit, string label, Func<float, string> format, float min, float max, float x, float y, float width)
    {
        SettingSlider slider = _menu!.Add(new SettingSlider(kit, label, format) { Name = label.Replace(" ", "") + "Slider", Min = min, Max = max });
        slider.Rect = Place(x, y, width, 46.0f);
        return slider;
    }

    private Switch AddSwitch(HudKit kit, string label, float x, float y, float width)
    {
        Switch toggle = _menu!.Add(new Switch(kit, label) { Name = label.Replace(" ", "") + "Switch" });
        toggle.Rect = Place(x, y, width, 38.0f);
        return toggle;
    }

    private static UIRect Place(float x, float y, float width, float height) =>
        new() { Anchor = Vector2.Zero, Pivot = Vector2.Zero, Position = new Vector2(x, y), Size = new Vector2(width, height) };

    /// <summary>A one-pixel line that fades with the menu.</summary>
    private sealed class Divider : Widget
    {
        protected override void OnDraw() =>
            HudKit.Quad(new Vector2(ScreenRect.X, ScreenRect.Y), new Vector2(ScreenRect.Z, ScreenRect.W), new Vector4(1.0f, 1.0f, 1.0f, 0.07f * HudKit.Opacity(this)));
    }
}
