using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpotEngine;

public class Player : Script
{
    public float gravity = 9;
    
    public override void Flow()
    {
        entity.transform.pos = new SpotEngine.Math.Vec3(0, entity.transform.pos.y - gravity * Spot.deltaTime, 0);
        Spot.Instance.game.title = $"{entity.transform.pos.x} / {entity.transform.pos.y.ToString("F1")}";
    }
}

