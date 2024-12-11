using OpenTK.Mathematics;
using SpotEngine.Internal.Rendering;

namespace SpotEngine.Rendering
{
    /// <summary>
    /// Represents a material with properties such as color, texture, shininess, and reflectivity.
    /// </summary>
    public class Material
    {
        /// <summary>
        /// Gets or sets the color of the material.
        /// </summary>
        public Color Color { get; set; }

        /// <summary>
        /// Gets or sets the texture applied to the material.
        /// </summary>
        public Texture2D Texture { get; set; }

        /// <summary>
        /// Gets or sets the shininess of the material, which affects how reflective it is.
        /// A higher value results in a shinier material.
        /// </summary>
        public float Shininess { get; set; }

        /// <summary>
        /// Gets or sets the reflectivity of the material.
        /// A higher value makes the material more reflective.
        /// </summary>
        public float Reflectivity { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Material"/> class with the specified color, texture, shininess, and reflectivity.
        /// </summary>
        /// <param name="color">The color of the material.</param>
        /// <param name="texture">The texture applied to the material (optional).</param>
        /// <param name="shininess">The shininess of the material (optional, default is 32.0f).</param>
        /// <param name="reflectivity">The reflectivity of the material (optional, default is 0.5f).</param>
        public Material(Color color, Texture2D texture = null, float shininess = 32.0f, float reflectivity = 0.5f)
        {
            Color = color;
            Texture = texture;
            Shininess = shininess;
            Reflectivity = reflectivity;
        }

        /// <summary>
        /// Applies the material properties to the specified shader.
        /// This method sets the material's color, texture, shininess, and reflectivity as shader uniforms.
        /// </summary>
        /// <param name="shader">The shader to apply the material properties to.</param>
        public void ApplyMaterial(Shader shader)
        {
            shader.SetUniform("materialColor", new Vector4(Color.R, Color.G, Color.B, Color.A));

            if (Texture != null)
            {
                //shader.SetUniform("materialTexture", 0); // Assuming texture is used on unit 0
                //Texture.Bind(0);
            }

            shader.SetUniform("materialShininess", Shininess);
            shader.SetUniform("materialReflectivity", Reflectivity);
        }
    }


}
