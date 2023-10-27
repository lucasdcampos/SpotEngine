using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpotEngine
{
    public class Rand
    {
        public static float Between(float x, float y)
        {
            Random _rand = new Random();
            double num = _rand.NextDouble() * (y - x) + x;

            return (float)num;
        }
    }
}
