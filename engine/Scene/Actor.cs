using System.Numerics;

namespace SpotEngine
{
    public class Actor : Entity
    {
        public Actor()
        {
            SpriteRenderer = AddComponent<SpriteRenderer>();
            Collider = AddComponent<Collider2D>();
        }

        public SpriteRenderer SpriteRenderer { get; set; }
        public Collider2D Collider { get; set; }

        public float X => transform.Pos.X;
        public float Y => transform.Pos.Y;
        public float Z => transform.Pos.Z;

        public bool CollidesWith(Actor other)
        {
            return Collider.CollidesWith(other.Collider);
        }

        protected override void OnUpdate(float dt)
        {
            Collider.Min = new Vec2(
                transform.Pos.X - (transform.Scale.X / 2f),
                transform.Pos.Y - (transform.Scale.Y / 2f)
            );

            Collider.Max = new Vec2(
                transform.Pos.X + (transform.Scale.X / 2f),
                transform.Pos.Y + (transform.Scale.Y / 2f)
            );
        }

    }
}