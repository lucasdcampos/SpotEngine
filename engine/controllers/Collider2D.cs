using SpotEngine;
using SpotEngine.Internal.Physics;

namespace SpotEngine
{
    public class Collider2D : Controller
    {
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float MinY { get; set; }
        public float MaxY { get; set; }

        public override void Init()
        {
            //
        }

        Collider2D col;
        public override void Flow()
        {
            col = DetectCollision();
            float x = entity.transform.pos.x;
            float y = entity.transform.pos.y;
            float width = entity.transform.scale.x;
            float height = entity.transform.scale.y;

            MinX = x;
            MaxX = x + width;
            MinY = y;
            MaxY = y + height;

        }

        public Collider2D OnCollision()
        {
            return col;
        }

        public bool CollidesWith(Collider2D other)
        {
            bool xOverlap = MaxX >= other.MinX && MinX <= other.MaxX;
            bool yOverlap = MaxY >= other.MinY && MinY <= other.MaxY;

            // if both true, then there is a collision
            return xOverlap && yOverlap;
        }

        // note: is recommended to have a list of entities with colliders, instead of looping
        // through every entity in the game looking for colliders, neet to add that later
        private Collider2D DetectCollision()
        {
            foreach (var otherEntity in Game.entities)
            {
                if (otherEntity != entity)
                {
                    Collider2D otherCol = otherEntity.GetController<Collider2D>() as Collider2D;
                    if (otherCol != null && CollidesWith(otherCol))
                    {
                        col = otherCol;
                        return otherCol;
                    }
                }
            }

            return null;
        }
    }
}
