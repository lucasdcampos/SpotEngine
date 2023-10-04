namespace SpotEngine
{
    public class Game
    {
        public List<Entity> entities = new List<Entity>();
        public string title = "Spot Game";

        public void InitEntities()
        {
            foreach (var entity in entities)
            {
                entity.Init();
            }
        }

        public void UpdateEntities()
        {
            foreach (var entity in entities)
            {
                entity.Flow();
            }
        }

        public void GetEntitiesByName()
        {
            foreach (var entity in entities)
            {
                Console.WriteLine(entity.name);
            }
        }



    }

}
