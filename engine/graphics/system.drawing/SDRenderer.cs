using SpotEngine;
using System.Drawing;
using System.Windows.Forms;

public class SDGraphicsRenderer : Form, IGraphicsRenderer
{
    private readonly List<DrawableSquare> squares = new List<DrawableSquare>();
    private Bitmap backBuffer;
    int width, height;
    public SDGraphicsRenderer(int width, int height)
    {
        this.Text = Spot.Instance.game.title;
        this.Size = new Size(width, height);
        this.Width = width;
        this.Height = height;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true;
        this.DoubleBuffered = true;
        this.BackColor = Color.Black;
        this.FormClosed += (sender, e) => Application.Exit();
        this.Paint += (sender, e) =>
        {

        };


    }

    public void Initialize()
    {
        backBuffer = new Bitmap(width, height);

        Thread guiThread = new Thread(() =>
        {
            Application.Run(this);
        });

        guiThread.Start();


    }


    public void RenderFrame()
    {

        DateTime startTime = DateTime.Now;

        DateTime currentTime = DateTime.Now;
        TimeSpan timePassed = currentTime - startTime;
        float deltaTime = (float)timePassed.TotalSeconds;

        startTime = currentTime;

        using (Graphics backBufferGraphics = Graphics.FromImage(backBuffer))
        {
            backBufferGraphics.Clear(Color.Black);

            foreach (var square in squares)
            {
                using (var brush = new SolidBrush(square.Color))
                {
                    backBufferGraphics.FillRectangle(brush, square.X, square.Y, square.Size, square.Size);
                }
            }

            RenderEntities(backBufferGraphics, Spot.Instance.game.entities);
        }

        using (Graphics g = this.CreateGraphics())
        {
            g.DrawImage(backBuffer, 0, 0);
        }


    }
    public void Cleanup()
    {

    }

    public void DrawSquare(float x, float y, float size, Color color)
    {
        float centerX = this.ClientSize.Width / 2.0f;
        float centerY = this.ClientSize.Height / 2.0f;

        float adjustedX = x - size / 2.0f + centerX;
        float adjustedY = y - size / 2.0f + centerY;

        this.Invoke(new Action(() =>
        {
            squares.Add(new DrawableSquare { X = adjustedX, Y = adjustedY, Size = size, Color = color });

            Refresh();
        }));
    }

    private class DrawableSquare
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Size { get; set; }
        public Color Color { get; set; }
    }

    public void RenderEntities(Graphics g, List<Entity> entities)
    {
        foreach (var entity in entities)
        {
            var squareRenderer = entity.GetController<SquareRenderer2D>();
            if (squareRenderer != null)
            {
                (squareRenderer as SquareRenderer2D).Render(g);
            }
        }
    }
}