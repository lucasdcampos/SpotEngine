using SpotEngine.Utils;
using OpenTK.Graphics.OpenGL.Compatibility;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.ComponentModel;

namespace SpotEngine.Internal.Graphics
{
    internal class OpenGLWindow : Window
    {
        NativeWindow m_native;
        internal IGLFWGraphicsContext Context => m_native.Context;

        internal OpenGLWindow(string title, int width, int height, bool compatibility = false) : base(title, width, height)
        {
            var settings = new NativeWindowSettings()
            {
                Size = new Vector2i(width, height),
                Title = title,
                Flags = ContextFlags.ForwardCompatible,
                // We should be using Core if we can
                Profile = compatibility? ContextProfile.Compatability : ContextProfile.Core
                
            };

            m_native = new NativeWindow(settings);

        }

        protected internal override void Initialize()
        {
            base.Initialize();

            m_native.MakeCurrent();

            m_native.Closing += CloseEvent;
            m_native.KeyDown += KeyDownEvent;
            m_native.KeyUp += KeyUpEvent;

            string glVersion = $"OpenGL API {GL.GetString(StringName.Version)}\n";
            glVersion += "                 ";
            glVersion += $"Vendor: {GL.GetString(StringName.Vendor)}";

            Log.Custom(glVersion, ConsoleColor.Cyan);

        }

        protected internal override void Update(float dt)
        {
            base.Update(dt);

            // obsolete
            m_native.ProcessEvents();
            m_native.ProcessInputEvents();

            Width = m_native.Size.X;
            Height = m_native.Size.Y;

        }

        protected internal override void Render(float dt)
        {
            base.Render(dt);
            GL.ClearColor(OpenTKUtils.SptColorToTKColor(BackgroundColor));

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            Application.ActiveScene.Render(dt);

            m_native.Context.SwapBuffers();

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