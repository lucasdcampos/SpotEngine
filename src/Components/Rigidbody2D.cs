namespace SpotEngine
{
    public class Rigidbody2D : Component
    {
        public Vec2 Velocity { get; set; }
        public Collider2D Collider { get; set; }

        private float _defaultVelocity = 5;

        public override void OnStart()
        {
            base.OnStart();
            if(Velocity == null) { Velocity = new Vec2(0, 0); }
            
            Collider = entity.GetComponent<Collider2D>();
        }

        public override void OnUpdate()
        {

            transform.pos = new Vec3(
                transform.pos.X + Velocity.X * Time.deltaTime, 
                transform.pos.Y + (Velocity.Y - _defaultVelocity) * Time.deltaTime,
                0);

            if(Collider != null)
            {
                Collider.BoundingBox.Min.X = transform.pos.X;
                Collider.BoundingBox.Min.Y = transform.pos.Y;
                Collider.BoundingBox.Max.X = transform.pos.X + Collider.BoundingBox.Width;
                Collider.BoundingBox.Max.Y = transform.pos.Y + Collider.BoundingBox.Height;

            }

        }
    }
}
