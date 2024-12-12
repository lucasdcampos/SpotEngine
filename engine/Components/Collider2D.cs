namespace SpotEngine
{
    public class Collider2D : Component
    {
        public Vec2 Min;
        public Vec2 Max;

        public bool CollidesWith(Collider2D other)
        {
            return !(Max.X < other.Min.X ||
                     Min.X > other.Max.X ||
                     Max.Y < other.Min.Y ||
                     Min.Y > other.Max.Y);
        }

    }

}
