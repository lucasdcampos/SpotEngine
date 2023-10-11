namespace SpotEngine
{
    public class Matrix4x4
    {
        public float[,] Data { get; set; } = new float[4, 4];

        public static Matrix4x4 Identity
        {
            get
            {
                var matrix = new Matrix4x4();
                for (int i = 0; i < 4; i++)
                {
                    matrix.Data[i, i] = 1.0f;
                }
                return matrix;
            }
        }

        public Matrix4x4() { }

        public Matrix4x4(float[,] data)
        {
            if (data.GetLength(0) != 4 || data.GetLength(1) != 4)
                throw new ArgumentException("Matrix must be 4x4");
            Data = data;
        }
    }
}