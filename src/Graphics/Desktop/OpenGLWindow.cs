// idk why 2 classes, i'll fix it later

using System;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Input;

namespace SpotEngine.Internal.Graphics
{
    internal class OpenGLWindow : Window
    {
        private OpenTKWindow window;

        internal class OpenTKWindow : GameWindow
        {
            private OpenGLWindow baseWindow;

            public OpenTKWindow(string title, int width, int height) : base(new GameWindowSettings(), new NativeWindowSettings())
            {
                Title = title;
                Size = new Vector2i(width, height);
                
            }

            protected override void OnLoad()
            {
                base.OnLoad();
                GL.ClearColor(Color4.Magenta);
            }

            protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
            {
                base.OnClosing(e);
                baseWindow.Close();
            }

            protected override void OnRenderFrame(FrameEventArgs e)
            {
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                SwapBuffers();
            }

            protected override void OnUpdateFrame(FrameEventArgs e)
            {
            }

            protected override void OnKeyDown(KeyboardKeyEventArgs e)
            {
                int i = (int)e.Key;
                var ev = new InputEvents.KeyboardPressEvent((KeyCode)Enum.ToObject(typeof(KeyCode), i));
                Event.TriggerKeyboardPressedEvent(this, ev);
            }

            protected override void OnKeyUp(KeyboardKeyEventArgs e)
            {
                int i = (int)e.Key;
                var ev = new InputEvents.KeyboardReleaseEvent((KeyCode)Enum.ToObject(typeof(KeyCode), i));
                Event.TriggerKeyboardReleasedEvent(this, ev);
            }

            public void SetBaseWindow(OpenGLWindow window)
            {
                if (baseWindow != null) { Log.Warn("There was already a Base Window defined."); }
                Log.Info($"Setting Base Window of OpenTK to: '{window}'");
                baseWindow = window;
            }
        }

        public OpenGLWindow(string title, int width, int height) : base(title, width, height)
        {
            window = new OpenTKWindow(title, width, height);
            window.SetBaseWindow(this);
            Initialize();
        }

        public override void Close()
        {
            var e = new WindowEvents.WindowCloseEvent();
            Event.TriggerWindowCloseEvent(this, e);
            
        }

        public override void Initialize()
        {
            base.Initialize();
            window.Run();
        }

    }
}
