namespace SpotEngine
{
    public class Collider2D : Component
    {
        public AABB2D BoundingBox { get; set; }
        private List<Collider2D> m_Colliders = new();

        public override void OnStart()
        {
            base.OnStart();

            var min = new Vec2(transform.pos.X, transform.pos.Y);
            var max = new Vec2(transform.pos.X + transform.scale.X, transform.pos.Y + transform.scale.Y);
            BoundingBox = new AABB2D(min, max);

            Physics2D.AddCollider(this);
        }

        public bool IsColliding(Collider2D other)
        {
            return BoundingBox.Intersects(other.BoundingBox);
        }

        public void NotifyCollision(Collider2D other)
        {
            if (!m_Colliders.Contains(other))
                m_Colliders.Add(other);
        }

        public List<Collider2D> GetColliders()
        {
            return m_Colliders;
        }
    }

}
