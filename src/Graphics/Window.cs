// Copyright (c) Spot Engine
// Licensed under MIT License.

using SpotEngine;

/// <summary>
/// An abstract class representing a window in the application.
/// </summary>
public abstract class Window
{
    /// <summary>
    /// Gets the width of the window.
    /// </summary>
    public int Width { get; protected set; }

    /// <summary>
    /// Gets the height of the window.
    /// </summary>
    public int Height { get; protected set; }

    /// <summary>
    /// Gets the title of the window.
    /// </summary>
    public string Title { get; protected set; }

    public static int UnitSize = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="Window"/> class with the specified title, width, and height.
    /// </summary>
    /// <param name="title">The title of the window.</param>
    /// <param name="width">The width of the window.</param>
    /// <param name="height">The height of the window.</param>
    public Window(string title, int width, int height)
    {
        Width = width;
        Height = height;
        Title = title;
    }

    /// <summary>
    /// Initializes and creates the window.
    /// </summary>
    protected internal virtual void Initialize()
    {
        Log.Info($"Creating window {Title} ({Width},{Height})");
    }

    /// <summary>
    /// Updates the window's content or state.
    /// </summary>
    protected internal virtual void Update(float dt) { }

    /// <summary>
    /// Renders the content of the window.
    /// </summary>
    protected internal virtual void Render(float dt) { }

    /// <summary>
    /// Closes and disposes of the window.
    /// </summary>
    protected internal virtual void Close()
    {
        Log.Info($"Closing window {Title}, ({Width}x{Height})");
    }
}