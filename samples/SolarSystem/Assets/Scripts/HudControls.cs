using System.Numerics;
using Spot.Engine.UI;

namespace SolarSystem;

/// <summary>
/// The HUD's controls, built in code from the custom widgets in <c>HudWidgets.cs</c>: a row of icon buttons in the
/// bottom-right corner (pause, overview, tour, settings) and the settings pop-up that opens above it. It only lays
/// them out and animates the pop-up; <see cref="SolarHud"/> wires what they do.
/// </summary>
internal sealed class HudControls
{
    private const float Margin = 24.0f;
    private const float ButtonSize = 40.0f;
    private const float Gap = 4.0f;
    private const float Inset = 6.0f;
    private const float PopupWidth = 300.0f;
    private const float PopupHeight = 392.0f;

    private readonly float _popupY;
    private float _opacity;

    public HudControls(UIRoot ui, HudKit kit)
    {
        // The toolbar: four round buttons on a frosted pill, pinned to the corner.
        Toolbar = ui.Add(new GlassPanel(kit) { Name = "Toolbar" });
        const int buttons = 4;
        Toolbar.Rect = Corner(new Vector2(-Margin, -Margin), new Vector2(Inset * 2.0f + buttons * ButtonSize + (buttons - 1) * Gap, ButtonSize + Inset * 2.0f));
        Pause = AddIcon(kit, kit.PauseIcon, 0, "Pause", "Space");
        Overview = AddIcon(kit, kit.OverviewIcon, 1, "Overview", "Esc");
        Tour = AddIcon(kit, kit.TourIcon, 2, "Guided tour", "T");
        Gear = AddIcon(kit, kit.SettingsIcon, 3, "Settings", "");

        // The pop-up, just above the toolbar.
        _popupY = -Margin - Toolbar.Rect.Size.Y - 12.0f;
        Settings = ui.Add(new GlassPanel(kit) { Name = "Settings", Visible = false, Opacity = 0.0f });
        Settings.Rect = Corner(new Vector2(-Margin, _popupY), new Vector2(PopupWidth, PopupHeight));

        Settings.Add(Label("Settings", new Vector2(20.0f, 17.0f), 280.0f, 18.0f, HudKit.Ink));
        Close = Settings.Add(new IconButton(kit, kit.CloseIcon) { Name = "CloseSettings", IconSize = 12.0f });
        Close.Rect = Place(new Vector2(PopupWidth - 14.0f - 30.0f, 12.0f), new Vector2(30.0f));

        Settings.Add(Label("Time speed", new Vector2(20.0f, 60.0f), 140.0f, 14.5f, HudKit.Ink * new Vector4(1.0f, 1.0f, 1.0f, 0.86f)));
        SpeedValue = Settings.Add(Label("", new Vector2(160.0f, 61.0f), 120.0f, 13.0f, HudKit.Muted));
        SpeedValue.Align = Spot.Engine.Graphics.TextAlign.Right;
        Speed = Settings.Add(new PillSlider(kit) { Name = "SpeedSlider" });
        Speed.Rect = Place(new Vector2(20.0f, 84.0f), new Vector2(PopupWidth - 40.0f, 24.0f));

        Settings.Add(new Divider()).Rect = Place(new Vector2(20.0f, 124.0f), new Vector2(PopupWidth - 40.0f, 1.0f));
        float y = 132.0f;
        Orbits = AddSwitch(kit, "Orbits", ref y);
        Labels = AddSwitch(kit, "Labels", ref y);
        Asteroids = AddSwitch(kit, "Asteroid belt", ref y);
        Bloom = AddSwitch(kit, "Bloom", ref y);
        AutoTour = AddSwitch(kit, "Tour when idle", ref y);

        Settings.Add(new Divider()).Rect = Place(new Vector2(20.0f, y + 8.0f), new Vector2(PopupWidth - 40.0f, 1.0f));
        Shortcuts = Settings.Add(new MenuButton(kit, "Keyboard shortcuts", "F1") { Name = "ShortcutsButton" });
        Shortcuts.Rect = Place(new Vector2(12.0f, y + 18.0f), new Vector2(PopupWidth - 24.0f, 36.0f));
    }

    public GlassPanel Toolbar { get; }

    public IconButton Pause { get; }

    public IconButton Overview { get; }

    public IconButton Tour { get; }

    public IconButton Gear { get; }

    public GlassPanel Settings { get; }

    public IconButton Close { get; }

    public FadeText SpeedValue { get; }

    public PillSlider Speed { get; }

    public Switch Orbits { get; }

    public Switch Labels { get; }

    public Switch Asteroids { get; }

    public Switch Bloom { get; }

    public Switch AutoTour { get; }

    public MenuButton Shortcuts { get; }

    /// <summary>Gets or sets whether the settings pop-up is open (it fades in and out on its own).</summary>
    public bool SettingsOpen { get; set; }

    /// <summary>Gets or sets whether the controls are shown at all (H hides the interface).</summary>
    public bool Shown { get; set; } = true;

    /// <summary>Fades and slides the pop-up toward its state; call once a frame.</summary>
    public void Animate(float deltaTime)
    {
        Toolbar.Visible = Shown;
        _opacity = HudKit.Approach(_opacity, SettingsOpen && Shown ? 1.0f : 0.0f, deltaTime * 7.0f);
        float ease = 1.0f - (1.0f - _opacity) * (1.0f - _opacity);
        Settings.Opacity = ease;
        Settings.Visible = _opacity > 0.0f;
        Settings.Rect.Position.Y = _popupY + 10.0f * (1.0f - ease);
        Gear.Active = SettingsOpen;
    }

    /// <summary>Gets whether a point, in UI units, is on the toolbar or the open pop-up.</summary>
    public bool Contains(Vector2 point) =>
        (Toolbar.Visible && Inside(Toolbar.ScreenRect, point)) || (Settings.Visible && Inside(Settings.ScreenRect, point));

    private IconButton AddIcon(HudKit kit, Spot.Engine.Graphics.Texture2D icon, int slot, string tooltip, string shortcut)
    {
        IconButton button = Toolbar.Add(new IconButton(kit, icon) { Name = tooltip.Replace(" ", "") + "Button", Tooltip = tooltip, Shortcut = shortcut });
        button.Rect = Place(new Vector2(Inset + slot * (ButtonSize + Gap), Inset), new Vector2(ButtonSize));
        return button;
    }

    private Switch AddSwitch(HudKit kit, string label, ref float y)
    {
        Switch toggle = Settings.Add(new Switch(kit, label) { Name = label.Replace(" ", "") + "Switch" });
        toggle.Rect = Place(new Vector2(20.0f, y), new Vector2(PopupWidth - 40.0f, 38.0f));
        y += 38.0f;
        return toggle;
    }

    private static FadeText Label(string text, Vector2 at, float width, float size, Vector4 color)
    {
        var label = new FadeText { Content = text, FontSize = size, Color = color };
        label.Rect = Place(at, new Vector2(width, size * 1.3f));
        return label;
    }

    private static UIRect Place(Vector2 position, Vector2 size) =>
        new() { Anchor = Vector2.Zero, Pivot = Vector2.Zero, Position = position, Size = size };

    // Anchored to the bottom-right corner of the screen.
    private static UIRect Corner(Vector2 offset, Vector2 size) =>
        new() { Anchor = Vector2.One, Pivot = Vector2.One, Position = offset, Size = size };

    private static bool Inside(Vector4 rect, Vector2 p) => p.X >= rect.X && p.X <= rect.X + rect.Z && p.Y >= rect.Y && p.Y <= rect.Y + rect.W;
}
