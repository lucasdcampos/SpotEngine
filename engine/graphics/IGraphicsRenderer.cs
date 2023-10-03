using System.Drawing;

public interface IGraphicsRenderer
{
    void Initialize(int width, int height);
    void RenderFrame();
    void Cleanup();

}