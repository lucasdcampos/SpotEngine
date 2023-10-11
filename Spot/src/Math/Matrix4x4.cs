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
    }
}

