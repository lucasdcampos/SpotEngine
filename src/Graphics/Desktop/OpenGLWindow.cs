using OpenTK.Graphics.OpenGL.Compatibility;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System;
using System.ComponentModel;


namespace SpotEngine.Internal.Graphics
{
    internal class OpenGLWindow : Window
    {
        GameWindow m_window;
        internal OpenGLWindow(string title, int width, int height) : base(title, width, height)
        {

            var settings = new NativeWindowSettings()
            {
                Size = new Vector2i(width, height),
                Title = title,
                Flags = ContextFlags.ForwardCompatible,
                Profile = ContextProfile.Compatability // only for debugging purposes
            };

            m_window = new GameWindow(GameWindowSettings.Default, settings);
        }

        protected internal override void Initialize()
        {
            base.Initialize();

            m_window.UpdateFrequency = 60;
            m_window.MakeCurrent();

            m_window.Closing += CloseEvent;
            m_window.KeyDown += KeyDownEvent;
            m_window.KeyUp += KeyUpEvent;

            GL.ClearColor(Color4.Black);

            var version = $"OpenGL API {GL.GetString(StringName.Version)}\n";
                version += "                 "; // lol
                version += $"Vendor: {GL.GetString(StringName.Vendor)}";

            Log.Custom(version, ConsoleColor.Cyan);
            
        }
        protected internal override void Update(float dt)
        {
            base.Update(dt);

            // obsolete
            m_window.ProcessEvents();

        }

        protected internal override void Render(float dt)
        {
            base.Render(dt);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            DrawTestTriangle();

            m_window.SwapBuffers();

        }

        internal void DrawTestTriangle()
        {
            GL.Begin(PrimitiveType.Triangles);

            GL.Color3f(1, 0, 0);
            GL.Vertex2f(0.0f, 0.5f);

            GL.Color3f(0, 1, 0);
            GL.Vertex2f(0.5f, -0.5f);

            GL.Color3f(0, 0, 1);
            GL.Vertex2f(-0.5f, -0.5f);
            GL.End();
        }

        protected internal override void Close()
        {
            base.Close();

            var e = new WindowEvents.WindowCloseEvent();
            Event.TriggerWindowCloseEvent(this, e);
        }

        private void CloseEvent(CancelEventArgs e)
        {
            Close();
        }

        private void KeyDownEvent(KeyboardKeyEventArgs e)
        {
            int i = (int)e.Key;
            var ev = new InputEvents.KeyboardPressEvent((KeyCode)Enum.ToObject(typeof(KeyCode), i));
            Event.TriggerKeyboardPressedEvent(this, ev);

        }

        private void KeyUpEvent(KeyboardKeyEventArgs e)
        {
            int i = (int)e.Key;
            var ev = new InputEvents.KeyboardReleaseEvent((KeyCode)Enum.ToObject(typeof(KeyCode), i));
            Event.TriggerKeyboardReleasedEvent(this, ev);
        }
    }
}