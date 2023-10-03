using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.ComponentModel;
using OpenTK.Mathematics;
using System.Collections.Generic;
using System;
using SpotEngine;

namespace SpotEngine
{
    public class Game
    {
        public List<Entity> entities = new List<Entity>();
        public string title = "Spot Game";

        public void InitEntities()
        {
            foreach (var entity in entities)
            {
                entity.Init();
            }
        }

        public void UpdateEntities(float deltaTime)
        {
            foreach (var entity in entities)
            {
                entity.Flow(deltaTime);
            }
        }

        public void GetEntitiesByName()
        {
            foreach (var entity in entities)
            {
                Console.WriteLine(entity.name);
            }
        }

    }

}
