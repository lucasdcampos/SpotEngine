using System.Numerics;
using Spot.Rendering;

namespace Spot.UI;

/// <summary>The pointer state handed to widgets each frame, in UI (virtual) pixel coordinates.</summary>
/// <param name="Pointer">The pointer position in UI coordinates.</param>
/// <param name="Down">Whether the primary button is currently held.</param>
/// <param name="Pressed">Whether the primary button went down this frame.</param>
/// <param name="Released">Whether the primary button came up this frame.</param>
public readonly record struct UIInput(Vector2 Pointer, bool Down, bool Pressed, bool Released);

/// <summary>
/// The base of the retained UI tree: a node with an anchored <see cref="Rect"/>, optional children and
/// virtual hooks for layout, drawing and pointer interaction. UI is built in code — construct widgets and
/// add them to the active scene's <see cref="UIRoot"/> (see <c>EntityBehaviour.UI</c>), or use the builder
/// methods here (<see cref="Panel()"/>, <see cref="Button(string)"/>, …) which create a child and return it
/// for configuration.
/// </summary>
public abstract class Widget
{
    private readonly List<Widget> _children = new();

    /// <summary>Where this widget sits inside its parent.</summary>
    public UIRect Rect = UIRect.Default;

    /// <summary>Whether this widget (and its subtree) is drawn.</summary>
    public bool Visible = true;

    /// <summary>Whether this widget (and its subtree) responds to the pointer.</summary>
    public bool Enabled = true;

    /// <summary>The parent widget, or <see langword="null"/> for a root.</summary>
    public Widget? Parent { get; private set; }

    /// <summary>This widget's children, drawn in order (later children on top).</summary>
    public IReadOnlyList<Widget> Children => _children;

    /// <summary>The absolute screen rectangle computed by the last layout pass, as <c>(x, y, width, height)</c>.</summary>
    public Vector4 ScreenRect { get; private set; }

    /// <summary>Whether the pointer is currently over this widget. Set each frame for interactive widgets.</summary>
    protected bool Hovered { get; private set; }

    /// <summary>Whether this widget is currently pressed by the pointer. Set each frame for interactive widgets.</summary>
    protected bool Held { get; private set; }

    /// <summary>Whether this widget takes part in pointer hit-testing. Interactive widgets override this.</summary>
    protected virtual bool IsInteractive => false;

    /// <summary>Adds a child widget, reparenting it if needed, and returns it.</summary>
    /// <typeparam name="T">The concrete widget type.</typeparam>
    /// <param name="child">The child to add.</param>
    public T Add<T>(T child) where T : Widget
    {
        child.Parent?.Remove(child);
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    /// <summary>Removes a direct child widget.</summary>
    /// <param name="child">The child to remove.</param>
    public void Remove(Widget child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
        }
    }

    /// <summary>Removes all children.</summary>
    public void Clear()
    {
        foreach (Widget child in _children)
        {
            child.Parent = null;
        }

        _children.Clear();
    }

    /// <summary>Creates a <see cref="Panel"/> child and returns it.</summary>
    public Panel Panel() => Add(new Panel());

    /// <summary>Creates an <see cref="Image"/> child for the given texture and returns it.</summary>
    /// <param name="texture">The texture to display.</param>
    public Image Image(Texture2D texture) => Add(new Image { Texture = texture });

    /// <summary>Creates a <see cref="Text"/> child with the given content and returns it.</summary>
    /// <param name="text">The text to display.</param>
    public Text Text(string text) => Add(new Text { Content = text });

    /// <summary>Creates a <see cref="Button"/> child with the given label and returns it.</summary>
    /// <param name="label">The button label.</param>
    public Button Button(string label) => Add(new Button { Label = label });

    /// <summary>Creates a <see cref="Slider"/> child and returns it.</summary>
    public Slider Slider() => Add(new Slider());

    /// <summary>Creates a <see cref="Toggle"/> child with the given label and returns it.</summary>
    /// <param name="label">The toggle label.</param>
    public Toggle Toggle(string label) => Add(new Toggle { Label = label });

    /// <summary>Draws the widget's own visuals. Children are drawn afterward by the tree.</summary>
    protected virtual void OnDraw()
    {
    }

    /// <summary>Adjusts children after this widget's <see cref="ScreenRect"/> is known (layout containers override).</summary>
    protected virtual void OnLayout()
    {
    }

    /// <summary>Handles pointer interaction while this widget is the active target (fires callbacks here).</summary>
    /// <param name="input">The current pointer state.</param>
    /// <param name="over">Whether the pointer is over this widget.</param>
    protected virtual void OnInteract(in UIInput input, bool over)
    {
    }

    internal static bool Contains(Vector4 rect, Vector2 p) =>
        p.X >= rect.X && p.X <= rect.X + rect.Z && p.Y >= rect.Y && p.Y <= rect.Y + rect.W;

    internal void Layout(Vector4 parentRect)
    {
        ScreenRect = Rect.Resolve(parentRect);
        OnLayout();
        foreach (Widget child in _children)
        {
            child.Layout(ScreenRect);
        }
    }

    internal void RenderTree()
    {
        if (!Visible) return;

        OnDraw();
        foreach (Widget child in _children)
        {
            child.RenderTree();
        }
    }

    // Returns the top-most interactive widget under the pointer, searching children (drawn last, so on top)
    // before the widget itself. A hidden or disabled subtree is skipped entirely.
    internal Widget? HitTest(Vector2 pointer)
    {
        if (!Visible || !Enabled) return null;

        for (int i = _children.Count - 1; i >= 0; i--)
        {
            Widget? hit = _children[i].HitTest(pointer);
            if (hit is not null) return hit;
        }

        return IsInteractive && Contains(ScreenRect, pointer) ? this : null;
    }

    internal void ResetInteractionState()
    {
        Hovered = false;
        Held = false;
        foreach (Widget child in _children)
        {
            child.ResetInteractionState();
        }
    }

    internal void Dispatch(in UIInput input, bool over)
    {
        Hovered = over;
        Held = over && input.Down;
        OnInteract(input, over);
    }
}
