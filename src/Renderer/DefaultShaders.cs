namespace SpotEngine.Rendering
{
    public static class DefaultShaders
    {
        public const string DefaultVertexShader = @"
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aColor;

            out vec4 vertexColor;

            uniform mat4 model;
            uniform mat4 view;
            uniform mat4 projection;

            void main()
            {
                gl_Position = projection * view * model * vec4(aPosition, 1.0);
                vertexColor = vec4(aColor, 1.0);
            }
        ";

        public const string DefaultFragmentShader = @"
            #version 330 core
            in vec4 vertexColor;

            out vec4 FragColor;

            uniform vec4 lightColor;

            void main()
            {
                FragColor = lightColor;
            }
        ";
    }

}
