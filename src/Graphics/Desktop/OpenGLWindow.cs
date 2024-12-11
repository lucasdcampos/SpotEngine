using SpotEngine.Utils;
using OpenTK.Graphics.OpenGL.Compatibility;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.ComponentModel;
using OpenTK.Graphics;
using OpenTK.Windowing.GraphicsLibraryFramework;

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
                Profile = compatibility ? ContextProfile.Compatability : ContextProfile.Core,
                StartFocused = true,
                StartVisible = true
            };

            m_native = new NativeWindow(settings);

        }

        protected internal override void Initialize()
        {
            base.Initialize();

            m_native.MakeCurrent();
            GLLoader.LoadBindings(new GLFWBindingsContext());
            GL.Enable(EnableCap.DepthTest);

            m_native.Closing += CloseEvent;
            m_native.KeyDown += (e) => ProcessKeyboardEvent(e, "key_down");
            m_native.KeyUp += (e) => ProcessKeyboardEvent(e, "key_up");
            m_native.MouseDown += (e) => ProcessMouseButtonEvent(e, "key_down");
            m_native.MouseUp += (e) => ProcessMouseButtonEvent(e, "key_up");
            m_native.Resize += Resize;
            m_native.MouseMove += ProcessMouseMoveEvent;
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

            m_native.CursorVisible = Cursor.Visible;
            //m_native.CursorState = Cursor.State == CursorState.Locked ? OpenTK.Windowing.Common.CursorState.Grabbed : OpenTK.Windowing.Common.CursorState.Normal;
        }

        protected internal override void Render(float dt)
        {
            base.Render(dt);
            GL.ClearColor(SptUtils.ToColor4(BackgroundColor));

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            Application.ActiveScene.Render(dt);
            Application.Renderer.Render(dt);
            m_native.Context.SwapBuffers();

        }

        protected internal void Resize(ResizeEventArgs e)
        {
            GL.Viewport(0,0, e.Width, e.Height);
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

        private void ProcessMouseButtonEvent(MouseButtonEventArgs e, string type)
        {
            int i = (int)e.Button;
            MouseButton mb = (MouseButton)Enum.ToObject(typeof(MouseButton), i);

            switch (type)
            {
                case "key_down":
                    Event.TriggerMouseButtonPressedEvent(this, new InputEvents.MouseButtonPressEvent(mb));
                    break;
                case "key_up":
                    Event.TriggerMouseButtonReleasedEvent(this, new InputEvents.MouseButtonReleaseEvent(mb));
                    break;
                default:
                    Log.Error($"Invalid Mouse event type: {type}");
                    break;
            }
        }

        private void ProcessMouseMoveEvent(MouseMoveEventArgs e)
        {
            Event.TriggerMouseMoveEvent(this, new InputEvents.MouseMoveEvent(e.X, e.Y, e.DeltaX, e.DeltaY));
        }

        private void ProcessKeyboardEvent(KeyboardKeyEventArgs e, string type)
        {
            int i = (int)e.Key;
            KeyCode keyCode = (KeyCode)Enum.ToObject(typeof(KeyCode), i);

            switch (type)
            {
                case "key_down":
                    Event.TriggerKeyboardPressedEvent(this, new InputEvents.KeyboardPressEvent(keyCode));
                    break;
                case "key_up":
                    Event.TriggerKeyboardReleasedEvent(this, new InputEvents.KeyboardReleaseEvent(keyCode));
                    break;
                default:
                    Log.Error($"Invalid Keyboard event type: {type}");
                    break;
            }
        }
    }
}