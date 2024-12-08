namespace SpotEngine
{
    /// <summary>
    /// Represents a 4x4 matrix for 3D transformations and projections.
    /// </summary>
    public class Mat4
    {
        /// <summary>
        /// Gets or sets the data array for the matrix.
        /// </summary>
        public float[,] Data { get; set; } = new float[4, 4];

        /// <summary>
        /// Gets the identity matrix (diagonal elements set to 1, all other elements set to 0).
        /// </summary>
        public static Mat4 Identity
        {
            get
            {
                var matrix = new Mat4();
                for (int i = 0; i < 4; i++)
                {
                    matrix.Data[i, i] = 1.0f;
                }
                return matrix;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Mat4"/> class.
        /// </summary>
        public Mat4() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="Mat4"/> class with the specified data array.
        /// </summary>
        /// <param name="data">The data array for the matrix.</param>
        /// <exception cref="ArgumentException">Thrown if the data array is not 4x4.</exception>
        public Mat4(float[,] data)
        {
            if (data.GetLength(0) != 4 || data.GetLength(1) != 4)
            {
                string err = "Matrix must be 4x4";
                Log.Error(err);
                throw new ArgumentException(err);
            }

            Data = data;
        }

        /// <summary>
        /// Creates a rotation matrix around the X-axis.
        /// </summary>
        /// <param name="angle">The rotation angle in radians.</param>
        /// <returns>The rotation matrix.</returns>
        public static Mat4 CreateRotX(float angle)
        {
            var matrix = Identity;
            matrix.Data[1, 1] = (float)Math.Cos(angle);
            matrix.Data[1, 2] = (float)-Math.Sin(angle);
            matrix.Data[2, 1] = (float)Math.Sin(angle);
            matrix.Data[2, 2] = (float)Math.Cos(angle);
            return matrix;
        }

        /// <summary>
        /// Creates a rotation matrix around the Y-axis.
        /// </summary>
        /// <param name="angle">The rotation angle in radians.</param>
        /// <returns>The rotation matrix.</returns>
        public static Mat4 CreateRotY(float angle)
        {
            var matrix = Identity;
            matrix.Data[0, 0] = (float)Math.Cos(angle);
            matrix.Data[0, 2] = (float)Math.Sin(angle);
            matrix.Data[2, 0] = (float)-Math.Sin(angle);
            matrix.Data[2, 2] = (float)Math.Cos(angle);
            return matrix;
        }

        /// <summary>
        /// Creates a rotation matrix around the Z-axis.
        /// </summary>
        /// <param name="angle">The rotation angle in radians.</param>
        /// <returns>The rotation matrix.</returns>
        public static Mat4 CreateRotZ(float angle)
        {
            var matrix = Identity;
            matrix.Data[0, 0] = (float)Math.Cos(angle);
            matrix.Data[0, 1] = (float)-Math.Sin(angle);
            matrix.Data[1, 0] = (float)Math.Sin(angle);
            matrix.Data[1, 1] = (float)Math.Cos(angle);
            return matrix;
        }

        /// <summary>
        /// Creates a translation matrix.
        /// </summary>
        /// <param name="x">Translation along the X axis.</param>
        /// <param name="y">Translation along the Y axis.</param>
        /// <param name="z">Translation along the Z axis.</param>
        /// <returns>The translation matrix.</returns>
        public static Mat4 CreateTranslation(float x, float y, float z)
        {
            var matrix = Identity;
            matrix.Data[0, 3] = x;
            matrix.Data[1, 3] = y;
            matrix.Data[2, 3] = z;
            return matrix;
        }

        /// <summary>
        /// Creates a scale matrix.
        /// </summary>
        /// <param name="x">Scale factor along the X axis.</param>
        /// <param name="y">Scale factor along the Y axis.</param>
        /// <param name="z">Scale factor along the Z axis.</param>
        /// <returns>The scale matrix.</returns>
        public static Mat4 CreateScale(float x, float y, float z)
        {
            var matrix = Identity;
            matrix.Data[0, 0] = x;
            matrix.Data[1, 1] = y;
            matrix.Data[2, 2] = z;
            return matrix;
        }

        /// <summary>
        /// Multiplies two 4x4 matrices.
        /// </summary>
        /// <param name="left">The left matrix.</param>
        /// <param name="right">The right matrix.</param>
        /// <returns>The resulting matrix.</returns>
        public static Mat4 operator *(Mat4 left, Mat4 right)
        {
            var result = new Mat4();
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    result.Data[i, j] = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        result.Data[i, j] += left.Data[i, k] * right.Data[k, j];
                    }
                }
            }
            return result;
        }
    }
}
