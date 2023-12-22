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
                if(col.transform.pos.Y <= transform.pos.Y)
                {
                    return;
                }
            }

            if(transform.pos.Y >= -1f)
            {
                transform.pos = new Vec3(
                    transform.pos.X + Velocity.X * Time.deltaTime,
                    transform.pos.Y + (Velocity.Y - _defaultVelocity) * Time.deltaTime,
                    0);
            }
        }
    }
}
