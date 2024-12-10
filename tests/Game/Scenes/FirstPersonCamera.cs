using OpenTK.Mathematics;
using SpotEngine;


namespace Game
{
    internal class FirstPersonCamera : Scene
    {
        protected override void OnStart()
        {
            base.OnStart();
            BackgroundColor = Color.Black;
        }
        protected override void OnUpdate(float dt)
        {
            float movementSpeed = 5.0f;
            Vector3 moveDirection = Vector3.Zero;

            if (Input.IsKeyPressed(KeyCode.W))
                moveDirection += renderer.Camera.Front;
            if (Input.IsKeyPressed(KeyCode.S))
                moveDirection -= renderer.Camera.Front;
            if (Input.IsKeyPressed(KeyCode.A))
                moveDirection -= renderer.Camera.Right;
            if (Input.IsKeyPressed(KeyCode.D))
                moveDirection += renderer.Camera.Right;

            if (Input.IsKeyPressed(KeyCode.Space))
                moveDirection += renderer.Camera.Up;
            if (Input.IsKeyPressed(KeyCode.LeftControl))
                moveDirection -= renderer.Camera.Up;

            if (moveDirection.LengthSquared > 0)
            {
                moveDirection = Vector3.Normalize(moveDirection);
                renderer.Camera.SetPosition(renderer.Camera.Position + moveDirection * movementSpeed * dt);
            }

            float yawRotationSpeed = 100;
            if (Input.IsKeyPressed(KeyCode.Q))
                renderer.Camera.Rotate(-yawRotationSpeed * dt, 0.0f);
            if (Input.IsKeyPressed(KeyCode.E))
                renderer.Camera.Rotate(yawRotationSpeed * dt, 0.0f);
        }

        private float rotationAngle = 0.0f;

        protected override void OnRender(float dt)
        {
            Vec3 position = new Vec3(0.0f, 0.0f, 0);
            Vec3 scale = new Vec3(1.0f, 1.0f, 1.0f);
            Vec3 rotation = new Vec3(0.0f, 1.0f, 0.0f);
            Color4<Rgba> color = new Color4<Rgba>(1.0f, 0.0f, 0.0f, 1.0f);

            //rotationAngle += dt;

            if (rotationAngle >= 360.0f)
                rotationAngle -= 360.0f;

            renderer.DrawTriangle(position, scale, new Vec3(0.0f, rotationAngle, 0.0f), color);
        }
    }
}
