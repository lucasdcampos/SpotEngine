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
            if (Input.IsMouseButtonPressed(MouseButton.Right))
            {
                Cursor.Visible = false;
                MoveCamera(dt);
            }
            else
            {
                Cursor.Visible = true;
            }
        }

        private void MoveCamera(float dt)
        {
            float movementSpeed = Input.IsKeyPressed(KeyCode.LeftShift) ? 10 : 5;
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

            float mouseSensitivity = 0.03f;

            float deltaX = Input.MouseDeltaX;
            float deltaY = Input.MouseDeltaY;

            float yawOffset = deltaX * mouseSensitivity;
            float pitchOffset = deltaY * mouseSensitivity;

            renderer.Camera.Rotate(yawOffset, -pitchOffset);
        }

        private float rotationAngle = 0.0f;

        protected override void OnRender(float dt)
        {
            Vec3 position = new Vec3(0.0f, 0.0f, 0);
            Vec3 scale = new Vec3(1.0f, 1.0f, 1.0f);
            Vec3 rotation = new Vec3(0.0f, 1.0f, 0.0f);
            Color color = new Color(1.0f, 0.0f, 0.0f, 1.0f);

            rotationAngle += dt;

            if (rotationAngle >= 360.0f)
                rotationAngle -= 360.0f;

            renderer.DrawTriangle(position, scale, new Vec3(0.0f, rotationAngle, 0.0f), new SpotEngine.Rendering.Material(Color.White));
            
        }
    }
}
