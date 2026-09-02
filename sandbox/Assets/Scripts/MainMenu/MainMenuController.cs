using Spot.Audio;
using Spot.Core;
using Spot.Scenes;
using Spot.UI;

namespace Spot.Game;

/// <summary>
/// The Sandbox hub. Its layout is authored in the editor as a UI document
/// (<c>Assets/UI/MainMenu.sptui</c>) and instantiated into the scene's UI by the entity's
/// <c>UICanvasComponent</c>; this script only wires behaviour, looking widgets up by name. It is the
/// data-driven counterpart to the old code-built menu: designers change the screen in the UI Canvas, and the
/// card/scene mapping below stays the single source of truth for what each card opens.
/// </summary>
public sealed class MainMenuController : EntityBehaviour
{
    // Card name in the .sptui -> scene it opens, and whether it is a game (reticle icon) or a test (gear icon).
    private static readonly (string Card, string Scene, bool Game)[] Entries =
    {
        ("Card_HordeSurvival", "Scenes/HordeSurvival.sptscene", true),
        ("Card_Physics2D", "Scenes/Physics2DPlayground.sptscene", true),
        ("Card_ThirdPerson", "Scenes/ThirdPerson.sptscene", false),
        ("Card_PhysicsTest", "Scenes/PhysicsTest.sptscene", false),
        ("Card_Main", "Scenes/Main.sptscene", false),
        ("Card_NewScene", "Scenes/NewScene.sptscene", false),
    };

    private Widget? _mainPanel;
    private Widget? _optionsPanel;

    // The UICanvasComponent instantiates the document before scripts run, so the widgets already exist here.
    public override void OnCreate()
    {
        _mainPanel = UI.Find("MainPanel");
        _optionsPanel = UI.Find("OptionsPanel");

        WireCards();
        WireOptions();
        WireFooter();

        SetOptionsVisible(false);
    }

    private void WireCards()
    {
        foreach ((string cardName, string scene, bool isGame) in Entries)
        {
            Button? card = UI.Find<Button>(cardName);
            if (card is null) continue;

            string target = scene;
            card.OnClick += () => SceneManager.Load(target);

            // The card icons are procedurally generated (see MenuIcons), so they are assigned here rather than
            // referenced as image assets in the document.
            Image? icon = card.Find<Image>("Icon");
            if (icon is not null) icon.Texture = isGame ? MenuIcons.Target : MenuIcons.Gear;
        }
    }

    private void WireOptions()
    {
        Slider? volume = UI.Find<Slider>("MasterVolume");
        if (volume is not null)
        {
            volume.Value = AudioSettings.MasterVolume;
            volume.OnValueChanged += v => AudioSettings.MasterVolume = v;
        }

        Toggle? mute = UI.Find<Toggle>("Mute");
        if (mute is not null)
        {
            mute.On = AudioSettings.Muted;
            mute.OnValueChanged += on => AudioSettings.Muted = on;
        }

        Button? back = UI.Find<Button>("BackButton");
        if (back is not null) back.OnClick += () => SetOptionsVisible(false);
    }

    private void WireFooter()
    {
        Button? options = UI.Find<Button>("OptionsButton");
        if (options is not null) options.OnClick += () => SetOptionsVisible(true);

        Button? quit = UI.Find<Button>("QuitButton");
        if (quit is not null)
        {
#if SPOT_BROWSER
            // A browser tab can't quit itself, so hide the button on that target.
            quit.Visible = false;
            quit.Enabled = false;
#else
            quit.OnClick += () => Application.Instance.Quit();
#endif
        }
    }

    private void SetOptionsVisible(bool showOptions)
    {
        if (_mainPanel is not null)
        {
            _mainPanel.Visible = !showOptions;
            _mainPanel.Enabled = !showOptions;
        }

        if (_optionsPanel is not null)
        {
            _optionsPanel.Visible = showOptions;
            _optionsPanel.Enabled = showOptions;
        }
    }
}
