namespace SpotEngine.Internal.Physics
{
    internal class BoundingBox
    {
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float MinY { get; set; }
        public float MaxY { get; set; }

        public BoundingBox(float x, float y, float width, float height)
        {
            MinX = x;
            MaxX = x + width;
            MinY = y;
            MaxY = y + height;
        }

        public bool CollidesWith(BoundingBox other)
        {
            bool xOverlap = MaxX >= other.MinX && MinX <= other.MaxX;
            bool yOverlap = MaxY >= other.MinY && MinY <= other.MaxY;

            // if both true, then there is a collision
            return xOverlap && yOverlap;
        }
    }
}
