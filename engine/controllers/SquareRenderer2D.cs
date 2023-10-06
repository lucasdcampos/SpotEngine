using System.Drawing;

namespace SpotEngine
{
    public class SquareRenderer2D : Controller
    {
        public System.Drawing.Color color = System.Drawing.Color.Red;
        public override void Init()
        {

        }

        public override void Flow()
        {
            entity.transform.pos = new Vec3(entity.transform.pos.x + 10 * Time.deltaTime(), 0, 0);
        }

        public void Render(System.Drawing.Graphics g)
        {
            float x = entity.transform.pos.x;
            float y = entity.transform.pos.y;
            Vec2 scale = new Vec2(entity.transform.scale.x, entity.transform.scale.y);

            using (var brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, x, y, scale.x, scale.y);
            }
            

        }
    }
}
