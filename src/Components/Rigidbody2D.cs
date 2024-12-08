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
            Velocity = new Vec2(0, 0);
            
            Collider = entity.GetComponent<Collider2D>();
        }

        public override void OnUpdate()
        {
            
            foreach (Collider2D col in Collider.GetColliders())
            {
                if(col.transform.Pos.Y <= transform.Pos.Y)
                {
                    return;
                }
            }

            if(transform.Pos.Y >= -1f)
            {
                transform.Pos = new Vec3(
                    transform.Pos.X + Velocity.X * Time.deltaTime,
                    transform.Pos.Y + (Velocity.Y - _defaultVelocity) * Time.deltaTime,
                    0);
            }
        }
    }
}
