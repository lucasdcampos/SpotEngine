using Spot.Rendering;

namespace Spot.Engine.Tests;

public class GlslTranspilerTests
{
    [Fact]
    public void ToGlslEs300_ReplacesCoreVersionDirective()
    {
        const string core = "#version 330 core\nvoid main() {}\n";

        string es = GlslTranspiler.ToGlslEs300(core);

        Assert.StartsWith("#version 300 es\n", es);
        Assert.DoesNotContain("#version 330 core", es);
    }

    [Fact]
    public void ToGlslEs300_EmitsExactlyOneVersionDirective()
    {
        const string core = "#version 330 core\nlayout (location = 0) in vec3 aPosition;\nvoid main() {}\n";

        string es = GlslTranspiler.ToGlslEs300(core);

        int count = es.Split("#version").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void ToGlslEs300_InjectsFragmentFloatPrecision()
    {
        const string core = "#version 330 core\nout vec4 fragColor;\nvoid main() { fragColor = vec4(1.0); }\n";

        string es = GlslTranspiler.ToGlslEs300(core);

        Assert.Contains("precision highp float;", es);
    }

    [Fact]
    public void ToGlslEs300_PreservesShaderBody()
    {
        const string core =
            "#version 330 core\n" +
            "layout (location = 0) in vec3 aPosition;\n" +
            "uniform mat4 uViewProjection;\n" +
            "void main() { gl_Position = uViewProjection * vec4(aPosition, 1.0); }\n";

        string es = GlslTranspiler.ToGlslEs300(core);

        Assert.Contains("layout (location = 0) in vec3 aPosition;", es);
        Assert.Contains("uniform mat4 uViewProjection;", es);
        Assert.Contains("gl_Position = uViewProjection * vec4(aPosition, 1.0);", es);
    }

    [Fact]
    public void ToGlslEs300_PrependsDirectiveWhenSourceHasNoVersion()
    {
        const string core = "void main() {}\n";

        string es = GlslTranspiler.ToGlslEs300(core);

        Assert.StartsWith("#version 300 es\n", es);
        Assert.Contains("void main() {}", es);
    }

    [Fact]
    public void ToGlslEs300_VersionDirectiveIsFirstLine()
    {
        // ES (like core) requires #version to precede every other statement, including the injected
        // precision defaults.
        const string core = "#version 330 core\nprecision test;\n";

        string es = GlslTranspiler.ToGlslEs300(core);

        string firstLine = es.Split('\n')[0];
        Assert.Equal("#version 300 es", firstLine);
    }
}
