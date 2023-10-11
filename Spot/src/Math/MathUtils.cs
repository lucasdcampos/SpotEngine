using System;

namespace SpotEngine
{
    public class Vector2
    {
        public float X { get; set; }
        public float Y { get; set; }

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Length()
        {
            return (float)Math.Sqrt(X * X + Y * Y);
        }

        public static Vector2 Normalize(Vector2 vector)
        {
            float length = vector.Length();
            if (length > 0)
            {
                return new Vector2(vector.X / length, vector.Y / length);
            }
            return Vector2.Zero;
        }

        public static Vector2 Zero => new Vector2(0, 0);
    }

    public class Vector3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float Length()
        {
            return (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        }

        public static Vector3 Normalize(Vector3 vector)
        {
            float length = vector.Length();
            if (length > 0)
            {
                return new Vector3(vector.X / length, vector.Y / length, vector.Z / length);
            }
            return Vector3.Zero;
        }

        public static Vector3 Zero => new Vector3(0, 0, 0);
    }

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

    public class Quaternion
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }

        public Quaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static Quaternion Identity => new Quaternion(0, 0, 0, 1);
    }

    public class Vector4
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }

        public Vector4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float Length()
        {
            return (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
        }

        public static Vector4 Normalize(Vector4 vector)
        {
            float length = vector.Length();
            if (length > 0)
            {
                return new Vector4(vector.X / length, vector.Y / length, vector.Z / length, vector.W / length);
            }
            return Vector4.Zero;
        }

        public static Vector4 Zero => new Vector4(0, 0, 0, 0);
    }

    public class MathUtils
    {
        public static float DegreesToRadians(float degrees)
        {
            return (float)(degrees * Math.PI / 180.0);
        }

        public static float RadiansToDegrees(float radians)
        {
            return (float)(radians * 180.0 / Math.PI);
        }
    }
}


