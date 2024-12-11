namespace SpotEngine.Rendering
{
    public static class DefaultShaders
    {
        public const string DefaultVertexShader = @"
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aColor;
            layout(location = 2) in vec2 aTexCoord;

            out vec4 vertexColor;
            out vec2 TexCoord;

            uniform mat4 model;
            uniform mat4 view;
            uniform mat4 projection;

            void main()
            {
                gl_Position = projection * view * model * vec4(aPosition, 1.0);
                vertexColor = vec4(aColor, 1.0);
                TexCoord = aTexCoord;
            }
        ";

        public const string DefaultFragmentShader = @"
            #version 330 core
            in vec4 vertexColor;
            in vec2 TexCoord;

            out vec4 FragColor;

            uniform vec4 materialColor;
            uniform sampler2D materialTexture;
            uniform float materialShininess;
            uniform float materialReflectivity;

            void main()
            {
                vec4 textureColor = texture(materialTexture, TexCoord);
                vec4 finalColor = materialColor;
                
                FragColor = finalColor;
            }
        ";
    }

}
