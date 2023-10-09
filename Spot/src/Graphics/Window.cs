// Copyright (c) Trivalent Studios
// Licensed under MIT License.

using SpotEngine;

public abstract class Window
{
    public int Width { get; protected set; }
    public int Height { get; protected set; }
    public string Title { get; protected set; }


    public Window(string title, int width, int height)
    {
        Width = width;
        Height = height;
        Title = title;
    }

    public abstract void Initialize();
    public abstract void Update();
    public abstract void Render();
    public abstract void Close();


}
