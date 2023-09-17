using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.ComponentModel;

namespace SpotEngine
{
    public class Game : GameWindow
    {
        public List<Entity> entities = new List<Entity>();

        public Game(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
            : base(gameWindowSettings, nativeWindowSettings)
        {
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            InitEntities();
        }

        public void InitEntities()
        {
            foreach (var entity in entities)
            {
                entity.Init();
            }
        }

        public void UpdateEntities(float deltaTime)
        {
            foreach (var entity in entities)
            {
                entity.Flow(deltaTime);
            }
        }

        public void GetEntitiesByName()
        {
            foreach (var entity in entities)
            {
                Console.WriteLine(entity.name);
            }
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);

            float deltaTime = (float)e.Time;

            UpdateEntities(deltaTime);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            // rendering logic
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Render scene



            SwapBuffers(); 
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // closing game
        }


    }

}
