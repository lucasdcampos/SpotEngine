namespace SpotEngine.Rendering
{
    public static class DefaultShaders
    {
        public const string DefaultVertexShader = @"
            #version 330 core

            layout(location = 0) in vec3 aPosition;

            uniform mat4 uModel;        // Matriz de Modelagem
            uniform mat4 uView;         // Matriz de Visualização (Câmera)
            uniform mat4 uProjection;   // Matriz de Projeção

            void main()
            {
                gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
            }
        ";

        public const string DefaultFragmentShader = @"
            #version 330 core

            out vec4 FragColor;

            uniform vec4 uColor;

            void main()
            {
                FragColor = uColor;
            }
        ";

    }

}
