using System.Globalization;
using System.Numerics;
using Spot.Engine.UI;
using Spot.Framework;
using Spot.Framework.Graphics;

namespace ProvingGrounds;

/// <summary>Everything the HUD layers read in one frame.</summary>
internal readonly record struct HudFrame(
    Vector2 Size,
    float DeltaTime,
    float Time,
    Playground Game,
    WeaponController? Weapons,
    Player? Player,
    ReflexChallenge? Challenge,
    float FieldOfView,
    float Heading);

/// <summary>
/// A custom widget — the runtime UI is open to subclassing — that draws the whole in-game HUD in layers: crosshair
/// and hit marker, compass, score and event feed, zone banners, the weapon and movement panels, the reflex
/// challenge, the stats board, the controls sheet and the start screen. It only reads game state; the interactive
/// pause menu is made of ordinary widgets (see <see cref="GameHud"/>).
/// </summary>
internal sealed class HudOverlay : Widget
{
    private readonly GameHud _hud;
    private readonly HudKit _kit;
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    private float _time;
    private float _shownScore;
    private float _startFade = 1.0f;
    private float _statsAlpha;
    private float _helpAlpha;
    private float _pauseDim;

    public HudOverlay(GameHud hud, HudKit kit)
    {
        _hud = hud;
        _kit = kit;
        Name = "HUD Overlay";
    }

    protected override void OnDraw()
    {
        if (Parent is null || Playground.Current is not { } game) return;

        float dt = Time.UnscaledDeltaTime;
        _time += dt;
        var size = new Vector2(Parent.ScreenRect.Z, Parent.ScreenRect.W);
        float heading = game.Controller is { } controller ? Wrap360(-controller.Yaw) : 0.0f;
        var frame = new HudFrame(size, dt, _time, game, WeaponController.Current, Player.Current, ReflexChallenge.Current, game.FieldOfView, heading);

        _startFade = HudKit.Approach(_startFade, game.Started ? 0.0f : 1.0f, dt * 2.5f);
        if (_startFade >= 0.999f)
        {
            DrawStart(frame, 1.0f);
            return;
        }

        float hudAlpha = 1.0f - _startFade;
        DrawScreenEffects(frame);
        if (!game.Paused) DrawCrosshair(frame);
        DrawCompass(frame, hudAlpha);
        DrawBanner(frame);
        DrawScore(frame, hudAlpha);
        DrawWeapon(frame, hudAlpha);
        DrawMovement(frame, hudAlpha);
        DrawChallenge(frame);

        _statsAlpha = HudKit.Approach(_statsAlpha, game.StatsOpen ? 1.0f : 0.0f, dt * 8.0f);
        if (_statsAlpha > 0.001f) DrawStats(frame, _statsAlpha);

        _helpAlpha = HudKit.Approach(_helpAlpha, game.HelpOpen && !game.Paused ? 1.0f : 0.0f, dt * 8.0f);
        if (_helpAlpha > 0.001f) DrawHelp(frame, _helpAlpha);

        _pauseDim = HudKit.Approach(_pauseDim, game.Paused ? 1.0f : 0.0f, dt * 6.0f);
        if (_pauseDim > 0.001f) HudKit.Quad(Vector2.Zero, size, new Vector4(0.01f, 0.015f, 0.025f, 0.55f * _pauseDim));

        if (_startFade > 0.001f) DrawStart(frame, _startFade);
    }

    // ---- Crosshair & hit marker ---------------------------------------------------------------------------------

    private void DrawCrosshair(in HudFrame f)
    {
        Vector2 center = f.Size * 0.5f;
        if (f.Weapons is { } weapons)
        {
            WeaponSpec weapon = weapons.Weapon;
            float aim = weapons.Aim;
            float alpha = weapon.Launcher ? 1.0f - aim * 0.5f : 1.0f - aim;
            float fov = f.FieldOfView * (1.0f + (weapon.AimZoom - 1.0f) * aim);
            float gap = MathF.Tan(weapons.Spread * MathF.PI / 180.0f) / MathF.Tan(fov * 0.5f * MathF.PI / 180.0f) * f.Size.Y * 0.5f + 5.0f;

            if (alpha > 0.01f)
            {
                if (weapon.Launcher)
                {
                    float ring = 30.0f + gap;
                    HudKit.Image(_kit.Ring, center - new Vector2(ring * 0.5f), new Vector2(ring), new Vector4(1.0f, 1.0f, 1.0f, 0.75f * alpha));
                    // Drop marks under the ring: where a grenade lands at short, medium and long range.
                    for (int i = 1; i <= 3; i++)
                    {
                        float y = center.Y + ring * 0.5f + 6.0f + i * 9.0f;
                        float w = 14.0f - i * 3.0f;
                        Line(new Vector2(center.X - w * 0.5f, y), new Vector2(w, 2.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.55f * alpha));
                    }
                }
                else
                {
                    const float length = 9.0f;
                    var color = new Vector4(1.0f, 1.0f, 1.0f, 0.92f * alpha);
                    Line(new Vector2(center.X - 1.0f, center.Y - gap - length), new Vector2(2.0f, length), color);
                    Line(new Vector2(center.X - 1.0f, center.Y + gap), new Vector2(2.0f, length), color);
                    Line(new Vector2(center.X - gap - length, center.Y - 1.0f), new Vector2(length, 2.0f), color);
                    Line(new Vector2(center.X + gap, center.Y - 1.0f), new Vector2(length, 2.0f), color);
                }

                Line(center - new Vector2(1.5f), new Vector2(3.0f), new Vector4(1.0f, 1.0f, 1.0f, alpha));
            }

            float reload = weapons.ReloadProgress;
            if (reload >= 0.0f)
            {
                var barAt = new Vector2(center.X - 30.0f, center.Y + 34.0f);
                _kit.Rounded(barAt, new Vector2(60.0f, 4.0f), new Vector4(0.0f, 0.0f, 0.0f, 0.45f), small: true);
                _kit.Rounded(barAt, new Vector2(60.0f * reload, 4.0f), HudColors.Accent, small: true);
                HudKit.Tracked("RELOADING", new Vector2(center.X, center.Y + 44.0f), 11.0f, HudKit.Fade(HudColors.Ink, 0.85f), 2.5f, TextAlign.Center);
            }
        }

        float since = f.Game.SinceHit;
        if (since < 0.32f)
        {
            bool kill = f.Game.LastHit == HitResult.Kill;
            float t = since / 0.32f;
            float scale = 1.0f + (kill ? 0.55f : 0.25f) * (1.0f - t) * (1.0f - t);
            float marker = (kill ? 30.0f : 24.0f) * scale;
            Vector4 color = kill ? new Vector4(1.0f, 0.38f, 0.3f, 1.0f - t * t) : new Vector4(1.0f, 1.0f, 1.0f, 1.0f - t * t);
            HudKit.Image(_kit.HitMarker, center - new Vector2(marker * 0.5f) + new Vector2(0.0f, 1.0f), new Vector2(marker), new Vector4(0.0f, 0.0f, 0.0f, color.W * 0.5f));
            HudKit.Image(_kit.HitMarker, center - new Vector2(marker * 0.5f), new Vector2(marker), color);
        }
    }

    // A crisp line with a dark outline, so it reads on any background.
    private static void Line(Vector2 at, Vector2 size, Vector4 color)
    {
        HudKit.Quad(at - Vector2.One, size + new Vector2(2.0f), new Vector4(0.0f, 0.0f, 0.0f, 0.45f * color.W));
        HudKit.Quad(at, size, color);
    }

    // ---- Compass ------------------------------------------------------------------------------------------------

    private static readonly string[] Cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    private void DrawCompass(in HudFrame f, float alpha)
    {
        const float width = 560.0f;
        const float range = 75.0f; // degrees each side
        float cx = f.Size.X * 0.5f;
        float y = 26.0f;
        float perDegree = width * 0.5f / range;

        UIRenderer.PushClip(new Vector4(cx - width * 0.5f, y - 6.0f, width, 64.0f));
        int first = (int)MathF.Floor((f.Heading - range) / 5.0f) * 5;
        for (int deg = first; deg <= f.Heading + range; deg += 5)
        {
            float x = cx + DeltaAngle(f.Heading, deg) * perDegree;
            float edge = EdgeFade(x - cx, width * 0.5f) * alpha;
            int norm = (int)Wrap360(deg);
            bool major = norm % 45 == 0;
            bool mid = norm % 15 == 0;
            float tick = major ? 12.0f : mid ? 8.0f : 4.0f;
            HudKit.Quad(new Vector2(x - 1.0f, y), new Vector2(2.0f, tick), new Vector4(1.0f, 1.0f, 1.0f, (major ? 0.9f : 0.45f) * edge));
            if (major)
            {
                string label = Cardinals[norm / 45 % 8];
                HudKit.Text(label, new Vector2(x, y + 15.0f), norm % 90 == 0 ? 17.0f : 13.0f,
                    HudKit.Fade(norm == 0 ? HudColors.Accent : HudColors.Ink, edge), TextAlign.Center);
            }
            else if (mid)
            {
                HudKit.Text(norm.ToString(_culture), new Vector2(x, y + 15.0f), 12.5f, HudKit.Fade(HudColors.Muted, edge * 0.8f), TextAlign.Center);
            }
        }

        UIRenderer.PopClip();

        // Landmarks: a diamond at their bearing, pinned to the edge when they are behind you.
        if (f.Game.Player.IsValid)
        {
            Vector3 player = f.Game.Player.GetComponent<Spot.Engine.Scenes.TransformComponent>().Position;
            foreach ((string label, Vector3 position) in _hud.Landmarks)
            {
                Vector3 to = position - player;
                float distance = new Vector2(to.X, to.Z).Length();
                if (distance < 4.0f) continue;
                float bearing = MathF.Atan2(to.X, -to.Z) * 180.0f / MathF.PI;
                float delta = DeltaAngle(f.Heading, bearing);
                bool pinned = MathF.Abs(delta) > range;
                float x = cx + Math.Clamp(delta, -range, range) * perDegree;
                float a = (pinned ? 0.35f : 1.0f) * alpha;
                HudKit.Image(_kit.DiamondIcon, new Vector2(x - 6.0f, y - 18.0f), new Vector2(12.0f), HudKit.Fade(HudColors.Gold, a));
                if (!pinned && MathF.Abs(delta) < 22.0f)
                {
                    string text = $"{label}  {distance:0} m";
                    HudKit.Tracked(text.ToUpperInvariant(), new Vector2(x, y + 38.0f), 12.0f, HudKit.Fade(HudColors.Gold, a * (1.0f - MathF.Abs(delta) / 22.0f)),
                        1.5f, TextAlign.Center);
                }
            }
        }

        // The heading under a small notch.
        HudKit.Quad(new Vector2(cx - 1.0f, y - 8.0f), new Vector2(2.0f, 6.0f), HudKit.Fade(HudColors.Accent, alpha));
    }

    private static float EdgeFade(float offset, float half)
    {
        float t = MathF.Abs(offset) / half;
        return Math.Clamp((1.0f - t) * 3.0f, 0.0f, 1.0f);
    }

    // ---- Zone banner --------------------------------------------------------------------------------------------

    private void DrawBanner(in HudFrame f)
    {
        float t = f.Game.SinceZone;
        if (t > 4.4f || f.Game.ZoneTitle.Length == 0) return;
        if (f.Challenge is { State: ChallengeState.Countdown or ChallengeState.Running }) return; // the challenge owns the top of the screen

        float alpha = t < 0.35f ? t / 0.35f : t > 3.6f ? 1.0f - (t - 3.6f) / 0.8f : 1.0f;
        float rise = (1.0f - MathF.Min(1.0f, t / 0.35f)) * 10.0f;
        float cx = f.Size.X * 0.5f;
        float y = 118.0f + rise;

        // A soft shadow behind the words, so they read over a bright sky.
        HudKit.Image(_kit.Glow, new Vector2(cx - 420.0f, y - 50.0f), new Vector2(840.0f, 200.0f), new Vector4(0.0f, 0.0f, 0.0f, 0.4f * alpha));
        float width = HudKit.Tracked(f.Game.ZoneTitle.ToUpperInvariant(), new Vector2(cx, y), 34.0f, HudKit.Fade(HudColors.Ink, alpha), 9.0f, TextAlign.Center);
        float line = width * Math.Clamp(t / 0.6f, 0.0f, 1.0f);
        HudKit.Quad(new Vector2(cx - line * 0.5f, y + 50.0f), new Vector2(line, 2.0f), HudKit.Fade(HudColors.Accent, alpha));
        HudKit.Text(f.Game.ZoneSubtitle, new Vector2(cx, y + 60.0f), 17.0f, HudKit.Fade(HudColors.Muted, alpha), TextAlign.Center);
    }

    // ---- Score & feed -------------------------------------------------------------------------------------------

    private void DrawScore(in HudFrame f, float alpha)
    {
        float right = f.Size.X - 40.0f;
        float y = 30.0f;
        int score = f.Game.Stats.Score;
        _shownScore = score < _shownScore ? score : _shownScore + MathF.Max(1.0f, (score - _shownScore) * 10.0f * f.DeltaTime);
        if (_shownScore > score) _shownScore = score;

        HudKit.Tracked("SCORE", new Vector2(right, y), 13.0f, HudKit.Fade(HudColors.Muted, alpha), 3.0f, TextAlign.Right);
        HudKit.Text(((int)_shownScore).ToString("N0", _culture), new Vector2(right, y + 14.0f), 44.0f, HudKit.Fade(HudColors.Ink, alpha), TextAlign.Right);

        float accuracy = f.Game.Stats.Accuracy * 100.0f;
        string line = $"{f.Game.Stats.ShotsHit} HITS   {accuracy:0}% ACC";
        HudKit.Tracked(line, new Vector2(right, y + 70.0f), 12.5f, HudKit.Fade(HudColors.Faint, alpha), 1.5f, TextAlign.Right);

        float rowY = y + 100.0f;
        foreach (FeedEntry entry in f.Game.Feed)
        {
            float a = (entry.Age < 0.18f ? entry.Age / 0.18f : entry.Age > 3.3f ? 1.0f - (entry.Age - 3.3f) / 0.7f : 1.0f) * alpha;
            float slide = (1.0f - MathF.Min(1.0f, entry.Age / 0.18f)) * 24.0f;
            float x = right + slide;
            if (entry.Points.Length > 0)
            {
                float pointsWidth = HudKit.Measure(entry.Points, 15.0f).X + 16.0f;
                _kit.Rounded(new Vector2(x - pointsWidth, rowY), new Vector2(pointsWidth, 24.0f), HudKit.Fade(entry.Color * new Vector4(1.0f, 1.0f, 1.0f, 0.2f), a));
                HudKit.Text(entry.Points, new Vector2(x - pointsWidth * 0.5f, rowY + 3.0f), 15.0f, HudKit.Fade(entry.Color, a), TextAlign.Center, shadow: false);
                x -= pointsWidth + 10.0f;
            }

            HudKit.Text(entry.Text, new Vector2(x, rowY + 3.0f), 16.0f, HudKit.Fade(HudColors.Ink, a), TextAlign.Right);
            rowY += 30.0f;
        }
    }

    // ---- Weapon panel -------------------------------------------------------------------------------------------

    private void DrawWeapon(in HudFrame f, float alpha)
    {
        if (f.Weapons is not { } weapons) return;

        WeaponSpec weapon = weapons.Weapon;
        var panel = new Vector2(340.0f, 150.0f);
        var at = new Vector2(f.Size.X - 40.0f - panel.X, f.Size.Y - 40.0f - panel.Y);
        _kit.Card(at, panel, alpha * 0.9f);

        // Slots: an icon per weapon with its key; the one in hand lit and underlined.
        float slotX = at.X + 18.0f;
        for (int i = 0; i < weapons.Slots.Count; i++)
        {
            WeaponSpec slot = weapons.Slots[i];
            bool active = slot == weapon;
            Vector4 tint = active ? slot.Accent : HudColors.Faint;
            HudKit.Text((i + 1).ToString(_culture), new Vector2(slotX, at.Y + 16.0f), 12.0f, HudKit.Fade(active ? HudColors.Ink : HudColors.Faint, alpha));
            HudKit.Image(slot.Launcher ? _kit.LauncherIcon : _kit.RifleIcon, new Vector2(slotX + 12.0f, at.Y + 12.0f), new Vector2(52.0f, 26.0f),
                HudKit.Fade(tint, alpha * (active ? 1.0f : 0.7f)));
            Vector4 underline = weapons.AmmoIn(slot) == 0 ? HudColors.Danger : slot.Accent;
            if (active || weapons.AmmoIn(slot) == 0)
            {
                HudKit.Quad(new Vector2(slotX + 12.0f, at.Y + 41.0f), new Vector2(52.0f, 2.0f), HudKit.Fade(underline, alpha * (active ? 1.0f : 0.6f)));
            }

            slotX += 84.0f;
        }

        string mode = weapon.Kind;
        float modeWidth = HudKit.MeasureTracked(mode, 11.0f, 2.0f) + 16.0f;
        _kit.Rounded(new Vector2(at.X + panel.X - 18.0f - modeWidth, at.Y + 16.0f), new Vector2(modeWidth, 20.0f),
            HudKit.Fade(weapon.Accent * new Vector4(1.0f, 1.0f, 1.0f, 0.18f), alpha));
        HudKit.Tracked(mode, new Vector2(at.X + panel.X - 18.0f - modeWidth * 0.5f, at.Y + 19.5f), 11.0f, HudKit.Fade(weapon.Accent, alpha), 2.0f,
            TextAlign.Center, shadow: false);

        HudKit.Tracked(weapon.Name.ToUpperInvariant(), new Vector2(at.X + 18.0f, at.Y + 55.0f), 13.0f, HudKit.Fade(HudColors.Muted, alpha), 2.0f);

        int ammo = weapons.Ammo;
        bool low = ammo <= weapon.Magazine / 4;
        string count = ammo.ToString(_culture);
        float countWidth = HudKit.Text(count, new Vector2(at.X + 16.0f, at.Y + 70.0f), 50.0f, HudKit.Fade(low ? HudColors.Danger : HudColors.Ink, alpha));
        HudKit.Text("/", new Vector2(at.X + 22.0f + countWidth, at.Y + 92.0f), 22.0f, HudKit.Fade(HudColors.Faint, alpha));
        HudKit.Image(_kit.InfinityIcon, new Vector2(at.X + 36.0f + countWidth, at.Y + 96.0f), new Vector2(30.0f, 15.0f), HudKit.Fade(HudColors.Muted, alpha));

        // The magazine as a row of ticks; reloading turns it into a progress bar.
        var barAt = new Vector2(at.X + 140.0f, at.Y + 104.0f);
        float barWidth = panel.X - 140.0f - 18.0f;
        float reload = weapons.ReloadProgress;
        if (reload >= 0.0f)
        {
            _kit.Rounded(barAt, new Vector2(barWidth, 8.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.1f * alpha), small: true);
            _kit.Rounded(barAt, new Vector2(barWidth * reload, 8.0f), HudKit.Fade(weapon.Accent, alpha), small: true);
            HudKit.Tracked("RELOADING", new Vector2(barAt.X, barAt.Y - 22.0f), 11.0f, HudKit.Fade(weapon.Accent, alpha), 2.5f);
        }
        else
        {
            int magazine = weapon.Magazine;
            float gap = magazine > 12 ? 2.0f : 5.0f;
            float tick = (barWidth - gap * (magazine - 1)) / magazine;
            for (int i = 0; i < magazine; i++)
            {
                bool loaded = i < ammo;
                Vector4 color = loaded ? (low ? HudColors.Danger : weapon.Accent) : new Vector4(1.0f, 1.0f, 1.0f, 0.12f);
                HudKit.Quad(new Vector2(barAt.X + i * (tick + gap), barAt.Y - (weapon.Launcher ? 6.0f : 0.0f)), new Vector2(tick, weapon.Launcher ? 14.0f : 8.0f),
                    HudKit.Fade(color, alpha));
            }

            if (ammo == 0)
            {
                HudKit.Tracked("PRESS R", new Vector2(barAt.X, barAt.Y - 22.0f), 11.0f, HudKit.Fade(HudColors.Danger, alpha), 2.5f);
            }
        }
    }

    // ---- Movement panel -----------------------------------------------------------------------------------------

    private void DrawMovement(in HudFrame f, float alpha)
    {
        if (f.Player is not { } player) return;

        var panel = new Vector2(280.0f, 150.0f);
        var at = new Vector2(40.0f, f.Size.Y - 40.0f - panel.Y);
        _kit.Card(at, panel, alpha * 0.9f);

        float run = f.Game.Controller?.RunSpeed ?? 8.0f;
        float speed = player.Speed;
        bool fast = speed > run * 1.08f;
        HudKit.Tracked("SPEED", new Vector2(at.X + 18.0f, at.Y + 18.0f), 13.0f, HudKit.Fade(HudColors.Muted, alpha), 3.0f);
        float width = HudKit.Text(speed.ToString("0.0", _culture), new Vector2(at.X + 16.0f, at.Y + 32.0f), 50.0f,
            HudKit.Fade(fast ? HudColors.Warm : HudColors.Ink, alpha));
        HudKit.Text("m/s", new Vector2(at.X + 22.0f + width, at.Y + 58.0f), 16.0f, HudKit.Fade(HudColors.Muted, alpha));
        HudKit.Text($"TOP {f.Game.Stats.TopSpeed.ToString("0.0", _culture)}", new Vector2(at.X + panel.X - 18.0f, at.Y + 18.0f), 12.0f,
            HudKit.Fade(HudColors.Faint, alpha), TextAlign.Right);

        // A speed bar to twice the run speed, with a mark at the run speed: anything past it is air-strafing.
        var barAt = new Vector2(at.X + 18.0f, at.Y + 96.0f);
        float barWidth = panel.X - 36.0f;
        float t = Math.Clamp(speed / (run * 2.0f), 0.0f, 1.0f);
        _kit.Rounded(barAt, new Vector2(barWidth, 5.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.1f * alpha), small: true);
        _kit.Rounded(barAt, new Vector2(MathF.Max(4.0f, barWidth * t), 5.0f), HudKit.Fade(fast ? HudColors.Warm : HudColors.Accent, alpha), small: true);
        HudKit.Quad(new Vector2(barAt.X + barWidth * 0.5f - 1.0f, barAt.Y - 3.0f), new Vector2(2.0f, 11.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.4f * alpha));

        // State chips.
        float chipX = at.X + 18.0f;
        float chipY = at.Y + 114.0f;
        string state = player.Flying ? "FLYING" : !player.Grounded ? "AIRBORNE" : player.Crouching ? "CROUCHED" : "GROUNDED";
        chipX += Chip(state, new Vector2(chipX, chipY), player.Flying ? HudColors.Gold : HudColors.Accent, alpha) + 8.0f;
        if (f.Game.SlowMotion)
        {
            chipX += Chip("SLOW-MO", new Vector2(chipX, chipY), HudColors.Warm, alpha) + 8.0f;
        }

        if (fast)
        {
            Chip("STRAFE JUMP", new Vector2(chipX, chipY), HudColors.Warm, alpha);
        }
    }

    private float Chip(string text, Vector2 at, Vector4 color, float alpha)
    {
        float width = HudKit.MeasureTracked(text, 11.5f, 1.8f) + 18.0f;
        _kit.Rounded(at, new Vector2(width, 22.0f), HudKit.Fade(color * new Vector4(1.0f, 1.0f, 1.0f, 0.18f), alpha));
        HudKit.Tracked(text, new Vector2(at.X + width * 0.5f, at.Y + 4.0f), 11.5f, HudKit.Fade(color, alpha), 1.8f, TextAlign.Center, shadow: false);
        return width;
    }

    // ---- Reflex challenge ---------------------------------------------------------------------------------------

    private void DrawChallenge(in HudFrame f)
    {
        if (f.Challenge is not { } challenge) return;

        Vector2 center = f.Size * 0.5f;
        switch (challenge.State)
        {
            case ChallengeState.Countdown:
            {
                int number = 3 - (int)challenge.StateTime;
                float within = challenge.StateTime % 1.0f;
                float pop = 1.0f + 0.5f * MathF.Pow(1.0f - within, 3.0f);
                float alpha = 1.0f - within * within;
                HudKit.Tracked("REFLEX TEST", new Vector2(center.X, center.Y - 170.0f), 16.0f, HudKit.Fade(HudColors.Accent, 0.9f), 6.0f, TextAlign.Center);
                string text = number.ToString(_culture);
                float size = 110.0f * pop;
                HudKit.Image(_kit.Glow, center - new Vector2(130.0f, 230.0f) * pop * 0.5f - new Vector2(0.0f, 70.0f), new Vector2(130.0f, 230.0f) * pop,
                    HudKit.Fade(HudColors.Accent, 0.25f * alpha));
                HudKit.Text(text, new Vector2(center.X, center.Y - 130.0f - (size - 110.0f) * 0.6f), size, HudKit.Fade(HudColors.Ink, alpha), TextAlign.Center);
                break;
            }
            case ChallengeState.Running:
            {
                var panel = new Vector2(380.0f, 74.0f);
                var at = new Vector2(center.X - panel.X * 0.5f, 92.0f);
                _kit.Card(at, panel, 1.0f);
                bool hurry = challenge.TimeLeft < 5.0f;
                float seconds = MathF.Max(0.0f, challenge.TimeLeft);
                HudKit.Text(seconds.ToString("00.0", _culture), new Vector2(center.X, at.Y + 10.0f), 38.0f, hurry ? HudColors.Danger : HudColors.Ink, TextAlign.Center);
                HudKit.Tracked("SCORE", new Vector2(at.X + 20.0f, at.Y + 14.0f), 10.5f, HudColors.Muted, 2.5f);
                HudKit.Text(challenge.Score.ToString("N0", _culture), new Vector2(at.X + 20.0f, at.Y + 30.0f), 24.0f, HudColors.Ink);
                HudKit.Tracked("COMBO", new Vector2(at.X + panel.X - 20.0f, at.Y + 14.0f), 10.5f, HudColors.Muted, 2.5f, TextAlign.Right);
                HudKit.Text($"x{challenge.Multiplier.ToString("0.00", _culture)}", new Vector2(at.X + panel.X - 20.0f, at.Y + 30.0f), 24.0f,
                    challenge.Combo >= 10 ? HudColors.Gold : challenge.Combo > 0 ? HudColors.Accent : HudColors.Faint, TextAlign.Right);
                float progress = seconds / MathF.Max(1.0f, challenge.Duration);
                HudKit.Quad(new Vector2(at.X + 12.0f, at.Y + panel.Y - 6.0f), new Vector2((panel.X - 24.0f) * progress, 2.0f), hurry ? HudColors.Danger : HudColors.Accent);
                break;
            }
            case ChallengeState.Results when challenge.StateTime < 9.0f:
            {
                float t = challenge.StateTime;
                float alpha = t < 0.3f ? t / 0.3f : t > 8.2f ? 1.0f - (t - 8.2f) / 0.8f : 1.0f;
                var panel = new Vector2(420.0f, 270.0f);
                var at = new Vector2(center.X - panel.X * 0.5f, 120.0f + (1.0f - MathF.Min(1.0f, t / 0.3f)) * 16.0f);
                _kit.Card(at, panel, alpha, new Vector4(0.03f, 0.035f, 0.05f, 0.82f));
                HudKit.Tracked("REFLEX TEST COMPLETE", new Vector2(center.X, at.Y + 22.0f), 13.0f, HudKit.Fade(HudColors.Accent, alpha), 4.0f, TextAlign.Center);
                HudKit.Text(challenge.Score.ToString("N0", _culture), new Vector2(center.X, at.Y + 44.0f), 56.0f, HudKit.Fade(HudColors.Ink, alpha), TextAlign.Center);
                if (challenge.NewBest)
                {
                    float pulse = 0.75f + 0.25f * MathF.Sin(t * 6.0f);
                    Chip("NEW BEST", new Vector2(center.X - 42.0f, at.Y + 112.0f), HudColors.Gold * new Vector4(1.0f, 1.0f, 1.0f, pulse), alpha);
                }
                else
                {
                    HudKit.Tracked($"BEST {f.Game.Stats.BestChallenge.ToString("N0", _culture)}", new Vector2(center.X, at.Y + 114.0f), 11.0f,
                        HudKit.Fade(HudColors.Faint, alpha), 2.5f, TextAlign.Center);
                }

                float rowY = at.Y + 150.0f;
                Row("Hits", challenge.Hits.ToString(_culture), at, panel.X, ref rowY, alpha);
                Row("Accuracy", $"{challenge.Accuracy * 100.0f:0}%", at, panel.X, ref rowY, alpha);
                Row("Average reaction", $"{challenge.AverageReaction * 1000.0f:0} ms", at, panel.X, ref rowY, alpha);
                Row("Best combo", challenge.BestCombo.ToString(_culture), at, panel.X, ref rowY, alpha);
                break;
            }
        }
    }

    private static void Row(string label, string value, Vector2 at, float width, ref float y, float alpha)
    {
        HudKit.Text(label, new Vector2(at.X + 32.0f, y), 16.0f, HudKit.Fade(HudColors.Muted, alpha), shadow: false);
        HudKit.Text(value, new Vector2(at.X + width - 32.0f, y), 16.0f, HudKit.Fade(HudColors.Ink, alpha), TextAlign.Right, shadow: false);
        y += 26.0f;
    }

    // ---- Stats board (Tab) --------------------------------------------------------------------------------------

    private void DrawStats(in HudFrame f, float alpha)
    {
        SessionStats s = f.Game.Stats;
        var panel = new Vector2(660.0f, 380.0f);
        var at = (f.Size - panel) * 0.5f + new Vector2(0.0f, (1.0f - alpha) * 14.0f);
        _kit.Card(at, panel, alpha, new Vector4(0.03f, 0.035f, 0.05f, 0.86f));
        HudKit.Tracked("SESSION", new Vector2(at.X + 32.0f, at.Y + 28.0f), 13.0f, HudKit.Fade(HudColors.Accent, alpha), 4.0f);
        TimeSpan played = TimeSpan.FromSeconds(s.PlayTime);
        HudKit.Text($"{(int)played.TotalMinutes}:{played.Seconds:00} played", new Vector2(at.X + panel.X - 32.0f, at.Y + 26.0f), 15.0f,
            HudKit.Fade(HudColors.Muted, alpha), TextAlign.Right, shadow: false);
        HudKit.Text(s.Score.ToString("N0", _culture), new Vector2(at.X + 30.0f, at.Y + 48.0f), 50.0f, HudKit.Fade(HudColors.Ink, alpha), shadow: false);
        HudKit.Tracked("POINTS", new Vector2(at.X + 34.0f + HudKit.Measure(s.Score.ToString("N0", _culture), 50.0f).X, at.Y + 80.0f), 11.0f,
            HudKit.Fade(HudColors.Faint, alpha), 2.5f);

        float column = (panel.X - 64.0f - 40.0f) * 0.5f;
        float leftX = at.X + 32.0f;
        float rightX = leftX + column + 40.0f;
        float y = at.Y + 130.0f;
        float yRight = y;
        Stat("Shots fired", s.ShotsFired.ToString("N0", _culture), leftX, column, ref y, alpha);
        Stat("Hits", s.ShotsHit.ToString("N0", _culture), leftX, column, ref y, alpha);
        Stat("Accuracy", $"{s.Accuracy * 100.0f:0}%", leftX, column, ref y, alpha);
        Stat("Bullseyes", s.Bullseyes.ToString(_culture), leftX, column, ref y, alpha);
        Stat("Targets down", s.TargetsDown.ToString(_culture), leftX, column, ref y, alpha);
        Stat("Reflex test best", s.BestChallenge.ToString("N0", _culture), leftX, column, ref y, alpha);
        Stat("Drones down", s.DronesDown.ToString(_culture), rightX, column, ref yRight, alpha);
        Stat("Crates blown up", s.CratesDestroyed.ToString(_culture), rightX, column, ref yRight, alpha);
        Stat("Rocket jumps", s.RocketJumps.ToString(_culture), rightX, column, ref yRight, alpha);
        Stat("Top speed", $"{s.TopSpeed.ToString("0.0", _culture)} m/s", rightX, column, ref yRight, alpha);
        Stat("Distance", $"{s.Distance.ToString("N0", _culture)} m", rightX, column, ref yRight, alpha);
        Stat("Frame time", $"{FrameStats.FrameTimeMs.ToString("0.0", _culture)} ms", rightX, column, ref yRight, alpha);
    }

    private void Stat(string label, string value, float x, float width, ref float y, float alpha)
    {
        HudKit.Quad(new Vector2(x, y + 33.0f), new Vector2(width, 1.0f), new Vector4(1.0f, 1.0f, 1.0f, 0.06f * alpha));
        HudKit.Text(label, new Vector2(x, y + 7.0f), 16.0f, HudKit.Fade(HudColors.Muted, alpha), shadow: false);
        HudKit.Text(value, new Vector2(x + width, y + 6.0f), 17.0f, HudKit.Fade(HudColors.Ink, alpha), TextAlign.Right, shadow: false);
        y += 36.0f;
    }

    // ---- Controls sheet (F1) ------------------------------------------------------------------------------------

    internal static readonly (string Keys, string Action)[] Controls =
    {
        ("W A S D", "Move"),
        ("Space", "Jump"),
        ("Shift", "Walk"),
        ("Ctrl", "Crouch"),
        ("Mouse", "Look"),
        ("LMB", "Fire"),
        ("RMB", "Aim down sights"),
        ("R", "Reload"),
        ("1  2  Q  Wheel", "Switch weapon"),
        ("T", "Slow motion"),
        ("Backspace", "Reset the playground"),
        ("V", "Fly (noclip)"),
        ("Tab", "Session stats"),
        ("F1", "This sheet"),
        ("Esc  P", "Pause and settings"),
    };

    private void DrawHelp(in HudFrame f, float alpha)
    {
        var panel = new Vector2(700.0f, 404.0f);
        var at = (f.Size - panel) * 0.5f + new Vector2(0.0f, (1.0f - alpha) * 14.0f);
        _kit.Card(at, panel, alpha, new Vector4(0.03f, 0.035f, 0.05f, 0.88f));
        HudKit.Tracked("CONTROLS", new Vector2(at.X + 32.0f, at.Y + 28.0f), 13.0f, HudKit.Fade(HudColors.Accent, alpha), 4.0f);
        HudKit.Text("F1 to close", new Vector2(at.X + panel.X - 32.0f, at.Y + 26.0f), 14.0f, HudKit.Fade(HudColors.Faint, alpha), TextAlign.Right, shadow: false);

        float column = (panel.X - 64.0f - 40.0f) * 0.5f;
        int perColumn = (Controls.Length + 1) / 2;
        for (int i = 0; i < Controls.Length; i++)
        {
            (string keys, string action) = Controls[i];
            float x = at.X + 32.0f + (i < perColumn ? 0.0f : column + 40.0f);
            float y = at.Y + 66.0f + (i % perColumn) * 40.0f;
            HudKit.Text(action, new Vector2(x, y + 4.0f), 16.0f, HudKit.Fade(HudColors.Ink * new Vector4(1.0f, 1.0f, 1.0f, 0.9f), alpha), shadow: false);
            _kit.Keycap(keys, new Vector2(x + column, y), 13.0f, alpha, alignRight: true);
        }
    }

    // ---- Start screen -------------------------------------------------------------------------------------------

    private static readonly (string Keys, string Action)[] StartControls =
    {
        ("W A S D", "Move"),
        ("Space", "Jump"),
        ("Ctrl", "Crouch"),
        ("LMB", "Fire"),
        ("RMB", "Aim down sights"),
        ("1  2  Q", "Switch weapon"),
        ("T", "Slow motion"),
        ("Esc", "Pause and settings"),
    };

    private static readonly string[] Features =
    {
        "Bepu physics", "Character controller", "Raycasts & impulses", "Custom render passes", "HDR + bloom", "Procedural audio", "Runtime UI",
    };

    private void DrawStart(in HudFrame f, float alpha)
    {
        Vector2 size = f.Size;
        HudKit.Quad(Vector2.Zero, size, new Vector4(0.01f, 0.015f, 0.03f, 0.62f * alpha));
        HudKit.Image(_kit.Vignette, Vector2.Zero, size, new Vector4(0.0f, 0.0f, 0.0f, 0.7f * alpha));

        float x = MathF.Max(80.0f, size.X * 0.09f);
        float y = size.Y * 0.27f;
        HudKit.Tracked("SPOT ENGINE  /  SAMPLE PROJECT", new Vector2(x + 2.0f, y), 14.0f, HudKit.Fade(HudColors.Accent, alpha), 4.0f);
        HudKit.Tracked("PROVING", new Vector2(x, y + 26.0f), 86.0f, HudKit.Fade(HudColors.Ink, alpha), 10.0f);
        HudKit.Tracked("GROUNDS", new Vector2(x, y + 116.0f), 86.0f, HudKit.Fade(HudColors.Ink, alpha), 10.0f);
        HudKit.Quad(new Vector2(x + 2.0f, y + 222.0f), new Vector2(120.0f, 3.0f), HudKit.Fade(HudColors.Accent, alpha));

        const string blurb = "A first-person playground built from primitive shapes, synthesized sound and the engine's own physics - "
            + "no models, textures or audio files. Shoot targets, chase drones, blow up crates and bend the movement.";
        UIRenderer.DrawText(HudKit.Font, blurb, new Vector2(x + 2.0f, y + 244.0f), 19.0f, HudKit.Fade(HudColors.Muted, alpha),
            new TextLayoutOptions { MaxWidth = 620.0f, Align = TextAlign.Left, LineSpacing = 1.25f });

        float chipX = x + 2.0f;
        float chipY = y + 330.0f;
        foreach (string feature in Features)
        {
            float width = HudKit.MeasureTracked(feature.ToUpperInvariant(), 11.5f, 1.8f) + 18.0f;
            if (chipX + width > x + 640.0f)
            {
                chipX = x + 2.0f;
                chipY += 28.0f;
            }

            chipX += Chip(feature.ToUpperInvariant(), new Vector2(chipX, chipY), HudColors.Ink * new Vector4(1.0f, 1.0f, 1.0f, 0.8f), alpha) + 8.0f;
        }

        // The call to action, breathing.
        float pulse = 0.65f + 0.35f * (0.5f + 0.5f * MathF.Sin(f.Time * 3.2f));
        var button = new Vector2(250.0f, 52.0f);
        var buttonAt = new Vector2(x + 2.0f, chipY + 64.0f);
        _kit.Rounded(buttonAt, button, HudKit.Fade(HudColors.Accent * new Vector4(1.0f, 1.0f, 1.0f, 0.18f + 0.12f * pulse), alpha));
        _kit.Rounded(buttonAt, new Vector2(4.0f, button.Y), HudKit.Fade(HudColors.Accent, alpha), small: true);
        HudKit.Tracked("CLICK TO DEPLOY", new Vector2(buttonAt.X + button.X * 0.5f, buttonAt.Y + 16.0f), 17.0f, HudKit.Fade(HudColors.Ink, alpha * (0.75f + 0.25f * pulse)),
            3.5f, TextAlign.Center);

        // A compact controls card on the right.
        var panel = new Vector2(380.0f, 46.0f + StartControls.Length * 34.0f);
        var at = new Vector2(size.X - panel.X - MathF.Max(80.0f, size.X * 0.07f), size.Y * 0.5f - panel.Y * 0.5f + 40.0f);
        if (at.X > x + 660.0f)
        {
            _kit.Card(at, panel, alpha * 0.9f);
            HudKit.Tracked("CONTROLS", new Vector2(at.X + 24.0f, at.Y + 22.0f), 12.0f, HudKit.Fade(HudColors.Accent, alpha), 3.5f);
            for (int i = 0; i < StartControls.Length; i++)
            {
                (string keys, string action) = StartControls[i];
                float rowY = at.Y + 52.0f + i * 34.0f;
                HudKit.Text(action, new Vector2(at.X + 24.0f, rowY + 3.0f), 15.0f, HudKit.Fade(HudColors.Ink * new Vector4(1.0f, 1.0f, 1.0f, 0.85f), alpha), shadow: false);
                _kit.Keycap(keys, new Vector2(at.X + panel.X - 24.0f, rowY), 12.0f, alpha, alignRight: true);
            }
        }

        HudKit.Text("F1 shows every control in game", new Vector2(x + 2.0f, size.Y - 60.0f), 14.0f, HudKit.Fade(HudColors.Faint, alpha), shadow: false);
    }

    // ---- Screen effects -----------------------------------------------------------------------------------------

    private void DrawScreenEffects(in HudFrame f)
    {
        float trauma = f.Game.Trauma;
        if (trauma > 0.01f)
        {
            HudKit.Image(_kit.Vignette, Vector2.Zero, f.Size, new Vector4(1.0f, 0.45f, 0.15f, MathF.Min(0.5f, trauma * 0.55f)));
        }

        if (f.Game.SlowMotion)
        {
            HudKit.Image(_kit.Vignette, Vector2.Zero, f.Size, new Vector4(0.2f, 0.6f, 1.0f, 0.35f));
        }
    }

    private static float Wrap360(float degrees)
    {
        float d = degrees % 360.0f;
        return d < 0.0f ? d + 360.0f : d;
    }

    private static float DeltaAngle(float from, float to)
    {
        float delta = (to - from) % 360.0f;
        if (delta > 180.0f) delta -= 360.0f;
        if (delta < -180.0f) delta += 360.0f;
        return delta;
    }
}
