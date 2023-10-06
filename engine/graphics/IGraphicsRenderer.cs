namespace SpotEngine.Internal.Graphic
{
    internal interface IGraphicsRenderer
    {
        void Initialize();
        void RenderFrame();
        void Cleanup();

    }
}
