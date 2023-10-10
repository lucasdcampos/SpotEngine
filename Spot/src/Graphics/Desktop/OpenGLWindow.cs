using System;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace SpotEngine.Internal.Graphics
{
    internal class OpenGLWindow : Window
    {
        GameWindow window;
        public OpenGLWindow(string title, int width, int height) : base(title, width, height)
        {
            window = new GameWindow(GameWindowSettings.Default, NativeWindowSettings.Default);
            window.Title = title;
            window.Size = new Vector2i(width, height);
            Initialize();
        }

        public override void Close()
        {
            throw new NotImplementedException();
        }

        public override void Initialize()
        {
            base.Initialize();
            window.Run();
        }

        public override void Render()
        {
            GL.ClearColor(Color4.Magenta);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            window.SwapBuffers();
        }

        public override void Update()
        {
            // Do stuff
            Render();
        }
    }
}
