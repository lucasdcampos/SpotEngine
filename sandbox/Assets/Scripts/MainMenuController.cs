using System;
using System.Collections.Generic;
using System.Numerics;
using Spot.Audio;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;
using Spot.UI;

namespace Spot.Game;

/// <summary>
/// The Sandbox hub, built with the engine's runtime UI (<c>Spot.UI</c>). It shows a card grid — a
/// <b>Games</b> row and a <b>Tests</b> row, each card an icon over a label — plus an options overlay that
/// exercises the <see cref="Slider"/> and <see cref="Toggle"/> widgets against the global audio settings.
/// New entries are added by appending to <see cref="Games"/> or <see cref="TestScenes"/>; the grid lays
/// itself out.
/// </summary>
public sealed class MainMenuController : EntityBehaviour
{
    // (button label, scene reference relative to the asset root).
    private static readonly (string Label, string Scene)[] Games =
    {
        ("Horde Survival", "Scenes/HordeSurvival.sptscene"),
        ("Physics 2D", "Scenes/Physics2DPlayground.sptscene"),
    };

    private static readonly (string Label, string Scene)[] TestScenes =
    {
        ("Physics Test", "Scenes/PhysicsTest.sptscene"),
        ("Main", "Scenes/Main.sptscene"),
        ("New Scene", "Scenes/NewScene.sptscene"),
    };

    private const float PanelWidth = 900f;
    private const float PanelHeight = 700f;
    private const float CardWidth = 172f;
    private const float CardHeight = 156f;
    private const float CardGap = 22f;
    private const float IconSize = 80f;

    private static readonly Vector4 AccentColor = new(0.98f, 0.60f, 0.28f, 1f);
    private static readonly Vector4 MutedColor = new(0.62f, 0.65f, 0.72f, 1f);
    private static readonly Vector4 TextColor = new(0.93f, 0.94f, 0.97f, 1f);

    private Panel _mainPanel = null!;
    private Panel _optionsPanel = null!;

    public override void OnCreate()
    {
        BuildMainPanel();
        BuildOptionsPanel();
        SetOptionsVisible(false);
    }

    private void BuildMainPanel()
    {
        _mainPanel = CenteredPanel(new Vector2(PanelWidth, PanelHeight));

        float y = 40f;
        AddText(_mainPanel, "Spot Sandbox", 48f, TextAlign.Center, TextColor, y, 58f);
        y += 60f;
        AddText(_mainPanel, "Engine 0.2 demo hub", 22f, TextAlign.Center, MutedColor, y, 28f);
        y += 52f;

        AddHeading(_mainPanel, "GAMES", AccentColor, y);
        y += 42f;
        var games = new List<Card>();
        foreach ((string label, string scene) in Games)
        {
            games.Add(new Card(label, MenuIcons.Target, new Vector4(0.95f, 0.38f, 0.32f, 1f), () => SceneManager.Load(scene)));
        }

        y = AddCardGrid(_mainPanel, games, y);
        y += 16f;

        AddHeading(_mainPanel, "TESTS", MutedColor, y);
        y += 42f;
        var tests = new List<Card>();
        foreach ((string label, string scene) in TestScenes)
        {
            tests.Add(new Card(label, MenuIcons.Gear, new Vector4(0.58f, 0.63f, 0.74f, 1f), () => SceneManager.Load(scene)));
        }

        y = AddCardGrid(_mainPanel, tests, y);
        y += 24f;

        AddFooter(_mainPanel, y);
    }

    private void AddFooter(Panel panel, float y)
    {
        const float buttonWidth = 210f;
        const float gap = 24f;
        float startX = (PanelWidth - (buttonWidth * 2f + gap)) * 0.5f;

        Button options = AddButton(panel, "Options", startX, y, buttonWidth, ShowOptions);
        options.NormalColor = new Vector4(0.20f, 0.22f, 0.28f, 1f);

        Button quit = AddButton(panel, "Quit", startX + buttonWidth + gap, y, buttonWidth, () => Application.Instance.Quit());
        quit.NormalColor = new Vector4(0.34f, 0.18f, 0.19f, 1f);
        quit.HoverColor = new Vector4(0.46f, 0.22f, 0.23f, 1f);
    }

    private void BuildOptionsPanel()
    {
        _optionsPanel = CenteredPanel(new Vector2(460f, 320f));

        float y = 34f;
        AddText(_optionsPanel, "Options", 40f, TextAlign.Center, TextColor, y, 50f);
        y += 64f;

        AddHeading(_optionsPanel, "Master Volume", MutedColor, y);
        y += 40f;
        Slider volume = _optionsPanel.Slider();
        volume.Min = 0f;
        volume.Max = 1f;
        volume.Value = AudioSettings.MasterVolume;
        volume.OnValueChanged += v => AudioSettings.MasterVolume = v;
        volume.Rect = Row(y, 24f, 372f);
        y += 44f;

        Toggle mute = _optionsPanel.Toggle("Mute");
        mute.On = AudioSettings.Muted;
        mute.FontSize = 24f;
        mute.TextColor = TextColor;
        mute.OnValueChanged += on => AudioSettings.Muted = on;
        mute.Rect = Row(y, 30f, 372f);
        y += 52f;

        AddButton(_optionsPanel, "Back", (460f - 372f) * 0.5f, y, 372f, ShowMain);
    }

    private void ShowOptions() => SetOptionsVisible(true);

    private void ShowMain() => SetOptionsVisible(false);

    private void SetOptionsVisible(bool showOptions)
    {
        _mainPanel.Visible = !showOptions;
        _mainPanel.Enabled = !showOptions;
        _optionsPanel.Visible = showOptions;
        _optionsPanel.Enabled = showOptions;
    }

    // Lays cards out in centered rows of up to four, returning the y just below the last row.
    private float AddCardGrid(Panel panel, IReadOnlyList<Card> cards, float y)
    {
        const int maxColumns = 4;
        int i = 0;
        while (i < cards.Count)
        {
            int n = Math.Min(maxColumns, cards.Count - i);
            float rowWidth = n * CardWidth + (n - 1) * CardGap;
            float startX = (PanelWidth - rowWidth) * 0.5f;

            for (int c = 0; c < n; c++)
            {
                AddCard(panel, cards[i + c], startX + c * (CardWidth + CardGap), y);
            }

            y += CardHeight + 18f;
            i += n;
        }

        return y;
    }

    private void AddCard(Panel panel, Card card, float x, float y)
    {
        Button button = panel.Button(string.Empty);
        button.NormalColor = new Vector4(0.17f, 0.18f, 0.23f, 1f);
        button.HoverColor = new Vector4(0.25f, 0.28f, 0.36f, 1f);
        button.PressedColor = new Vector4(0.13f, 0.14f, 0.18f, 1f);
        button.Rect = new UIRect
        {
            Anchor = Vector2.Zero,
            Pivot = Vector2.Zero,
            Position = new Vector2(x, y),
            Size = new Vector2(CardWidth, CardHeight),
        };
        button.OnClick += card.Click;

        Image icon = button.Image(card.Icon);
        icon.Color = card.Tint;
        icon.Rect = new UIRect
        {
            Anchor = new Vector2(0.5f, 0f),
            Pivot = new Vector2(0.5f, 0f),
            Position = new Vector2(0f, 22f),
            Size = new Vector2(IconSize, IconSize),
        };

        Text label = button.Text(card.Label);
        label.FontSize = 22f;
        label.Align = TextAlign.Center;
        label.Color = TextColor;
        label.Rect = new UIRect
        {
            Anchor = Vector2.Zero,
            Pivot = Vector2.Zero,
            Position = new Vector2(0f, CardHeight - 42f),
            Size = new Vector2(CardWidth, 28f),
        };
    }

    private Panel CenteredPanel(Vector2 size)
    {
        Panel panel = UI.Panel();
        panel.Color = new Vector4(0.10f, 0.11f, 0.14f, 0.96f);
        panel.Rect = new UIRect
        {
            Anchor = new Vector2(0.5f, 0.5f),
            Pivot = new Vector2(0.5f, 0.5f),
            Position = Vector2.Zero,
            Size = size,
        };
        return panel;
    }

    private static void AddText(Panel panel, string text, float size, TextAlign align, Vector4 color, float y, float height)
    {
        Text label = panel.Text(text);
        label.FontSize = size;
        label.Align = align;
        label.Color = color;
        label.Rect = new UIRect
        {
            Anchor = new Vector2(0.5f, 0f),
            Pivot = new Vector2(0.5f, 0f),
            Position = new Vector2(0f, y),
            Size = new Vector2(panel.Rect.Size.X, height),
        };
    }

    private static void AddHeading(Panel panel, string text, Vector4 color, float y)
    {
        Text heading = panel.Text(text);
        heading.FontSize = 22f;
        heading.Align = TextAlign.Left;
        heading.Color = color;
        heading.Rect = new UIRect
        {
            Anchor = new Vector2(0.5f, 0f),
            Pivot = new Vector2(0.5f, 0f),
            Position = new Vector2(0f, y),
            Size = new Vector2(panel.Rect.Size.X - 96f, 28f),
        };
    }

    private static Button AddButton(Panel panel, string label, float x, float y, float width, Action onClick)
    {
        Button button = panel.Button(label);
        button.FontSize = 24f;
        button.TextColor = TextColor;
        button.Rect = new UIRect
        {
            Anchor = Vector2.Zero,
            Pivot = Vector2.Zero,
            Position = new Vector2(x, y),
            Size = new Vector2(width, 46f),
        };
        button.OnClick += onClick;
        return button;
    }

    private static UIRect Row(float y, float height, float width) => new()
    {
        Anchor = new Vector2(0.5f, 0f),
        Pivot = new Vector2(0.5f, 0f),
        Position = new Vector2(0f, y),
        Size = new Vector2(width, height),
    };

    // A single menu entry: an icon (with a tint) over a label, opening a scene when clicked.
    private sealed class Card
    {
        public Card(string label, Texture2D icon, Vector4 tint, Action click)
        {
            Label = label;
            Icon = icon;
            Tint = tint;
            Click = click;
        }

        public string Label { get; }

        public Texture2D Icon { get; }

        public Vector4 Tint { get; }

        public Action Click { get; }
    }
}
