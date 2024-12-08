namespace SpotEngine
{
    public class Actor : Entity
    {
        public Actor()
        {
            SpriteRenderer = AddComponent<SpriteRenderer>();
        }

        public SpriteRenderer SpriteRenderer { get; set; }

        public float X => transform.Pos.X;
        public float Y => transform.Pos.Y;
        public float Z => transform.Pos.Z;
    }
}