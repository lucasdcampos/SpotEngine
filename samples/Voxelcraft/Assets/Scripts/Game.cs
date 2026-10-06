using Spot.Engine.Scenes;
using Spot.Engine;

namespace Voxelcraft;

/// <summary>
/// The game's state and its global keys: the loading screen until the land around the spawn is drawn, pause
/// (Esc), the inventory (E), the debug screen (F3), hiding the interface (F1) and the clock (T time-lapse,
/// N next phase). It owns the mouse: locked for looking around while playing, free in menus.
/// </summary>
public sealed class Game : Component
{
    /// <summary>Gets or sets how many chunks around the spawn must be drawn before play starts.</summary>
    [InspectorRange(0.0f, 6.0f, 1.0f)]
    public int SpawnRadius { get; set; } = 3;

    public static Game? Current { get; private set; }

    /// <summary>Gets whether the world around the spawn is ready and play has begun.</summary>
    public bool Loaded { get; private set; }

    public bool Paused { get; private set; }

    public bool InventoryOpen { get; private set; }

    public bool ShowDebug { get; set; }

    public bool HudHidden { get; set; }

    /// <summary>Gets whether the player is in control: loaded, and no menu needs the pointer.</summary>
    public bool InControl => Loaded && !Paused && !InventoryOpen;

    /// <summary>Gets how far the loading is, 0..1.</summary>
    public float LoadProgress { get; private set; }

    /// <summary>Gets a short message for the HUD, and how long ago it was set.</summary>
    public string Toast { get; private set; } = "";

    public float ToastAge { get; private set; } = 100.0f;

    private bool _hadLock;
    private float _loadingTime;

    public override void OnStart()
    {
        Current = this;
        Input.CursorLocked = false;
    }

    public override void OnDestroy()
    {
        // The editor keeps the process alive between plays: hand the mouse and the clock back.
        Input.CursorLocked = false;
        Time.TimeScale = 1.0f;
        if (Current == this) Current = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        ToastAge += deltaTime;

        if (!Loaded)
        {
            _loadingTime += deltaTime;
            World? world = VoxelWorld.Current?.World;
            if (world is not null && PlayerController.Current is { } player)
            {
                LoadProgress = world.Progress(player.Position, SpawnRadius);
                if (LoadProgress >= 1.0f && _loadingTime > 0.25f)
                {
                    Loaded = true;
                    Notify("Esc for the menu and controls");
                }
            }
        }

        HandleKeys();
        UpdateCursor();

        // The world stands still while the pause menu is open.
        Time.TimeScale = Paused ? 0.0f : 1.0f;
    }

    public void Notify(string message)
    {
        Toast = message;
        ToastAge = 0.0f;
    }

    public void Pause() => Paused = Loaded;

    public void OpenInventory() => InventoryOpen = Loaded && !Paused;

    public void Resume()
    {
        Paused = false;
        InventoryOpen = false;
    }

    private void HandleKeys()
    {
        if (Input.GetKeyDown(Key.F1)) HudHidden = !HudHidden;
        if (Input.GetKeyDown(Key.F3)) ShowDebug = !ShowDebug;
        if (!Loaded) return;

        if (Input.GetKeyDown(Key.Escape))
        {
            if (InventoryOpen) InventoryOpen = false;
            else Paused = !Paused;
        }

        if (Input.GetKeyDown(Key.E) && !Paused)
        {
            InventoryOpen = !InventoryOpen;
        }

        // A click on the pause screen goes back into the game.
        if (Paused && Input.GetMouseButtonDown(MouseButton.Left))
        {
            Paused = false;
        }

        if (DayNightCycle.Current is { } cycle && !Paused && !InventoryOpen)
        {
            if (Input.GetKeyDown(Key.T))
            {
                cycle.TimeLapse = !cycle.TimeLapse;
                Notify(cycle.TimeLapse ? "Time-lapse on" : "Time-lapse off");
            }

            if (Input.GetKeyDown(Key.N))
            {
                cycle.SkipToNextPhase();
                Notify($"Skipped to {cycle.Clock}");
            }
        }
    }

    // The mouse is locked for looking around only while playing. Losing the lock some other way (the editor's
    // Esc, switching windows) opens the pause menu rather than leaving the view spinning with a free cursor.
    private void UpdateCursor()
    {
        bool want = InControl;
        if (want && _hadLock && !Input.CursorLocked)
        {
            Paused = true;
            want = false;
        }

        if (Input.CursorLocked != want) Input.CursorLocked = want;
        _hadLock = Input.CursorLocked;
    }
}
