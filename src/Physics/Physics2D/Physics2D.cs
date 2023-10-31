namespace SpotEngine
{
    public class Physics2D
    {
        private static List<Collider2D> colliders = new();


        public static void AddCollider(Collider2D collider)
        {
            colliders.Add(collider);
        }

        public void CheckCollisions()
        {
            for (int i = 0; i < colliders.Count; i++)
            {
                for (int j = i + 1; j < colliders.Count; j++)
                {
                    if (colliders[i].IsColliding(colliders[j]))
                    {
                        colliders[i].NotifyCollision(colliders[j]);
                        colliders[j].NotifyCollision(colliders[i]);
                    }
                }
            }
        }
    }

}
