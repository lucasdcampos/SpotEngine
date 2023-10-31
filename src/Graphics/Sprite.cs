using System.IO;

namespace SpotEngine
{
    public class Sprite
    {
        public FileStream texturePath;

        public Color color;

        public Sprite()
        {
            texturePath = null;
            color = Color.White;
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
