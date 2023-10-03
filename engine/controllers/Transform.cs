using SpotEngine.Math;


namespace SpotEngine
{
    public class Transform
    {
        public Vec3 pos { get; set; }
        public Vec3 rot { get; set; }
        public Vec3 scale { get; set; }

        public Transform()
        {
            pos = Vec3.Zero;
            rot = Vec3.Zero;
            scale = Vec3.One;
        }
    }
}