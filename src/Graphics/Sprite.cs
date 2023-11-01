using System.IO;

namespace SpotEngine
{
    public class Sprite
    {
        public FileStream texturePath;

        public Color color;

        public int layer = 0;

        public Sprite()
        {
            texturePath = null;
            color = Color.White;
        }

        public Sprite(FileStream filePath, Color color, int layer)
        {
            try
            {
                this.texturePath = new FileStream($"Assets/{filePath}", FileMode.Open);
            }
            catch (Exception)
            {
                Log.Error($"Asset 'Assets/{filePath}' could not be opended!");
                return;
            }

            this.color = color == null ? Color.White : color;

            this.layer = layer;
        }

       public Sprite(string filePath, Color color)
        {
            try
            {
                this.texturePath = new FileStream($"Assets/{filePath}", FileMode.Open);
            }
            catch (Exception)
            {
                Log.Error($"Asset 'Assets/{filePath}' could not be opended!");
                return;
            }
            
            this.color = color == null? Color.White : color;

        }
    }
}
