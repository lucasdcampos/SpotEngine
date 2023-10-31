﻿using SpotEngine;

internal class Program
{
    public static void Main(string[] args)
    {
        Application app = Application.GetApp();
        

        Scene scene = new Scene();
        Scene.current = scene;

        Entity e = new Entity();
        e.AddComponent<SpriteRenderer>();
        e.GetComponent<SpriteRenderer>().sprite = new Sprite("Player.png", Color.White);
        e.transform.scale = new Vec3(1f, 1f, 1f);
        scene.RegisterEntity(e);

        Entity e2 = new Entity();
        e2.AddComponent<SpriteRenderer>();
        e2.transform.pos = new Vec3(1, 0, 0);
        e2.transform.scale = new Vec3(1f, 1f, 1f);
        scene.RegisterEntity(e2);

        Entity square = new Entity();
        square.AddComponent<SpriteRenderer>();
        square.transform.pos = new Vec3(0, -2, 0);
        square.transform.scale = new Vec3(10, 1, 1);
        scene.RegisterEntity(square);

        square.GetComponent<SpriteRenderer>().sprite.color = Color.Blue;
        app.Run();
        
    }
}