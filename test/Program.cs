/*
This file is used for testing purposes only
You can test a simple game with it, or just
debug specific things
*/

using SpotEngine;
int width = 800;
int height = 600;

Entity player = Entity.SpawnEntity(new Entity());
player.AddController(new SpriteRenderer());
SpriteRenderer spriteRenderer = (SpriteRenderer)player.GetController<SpriteRenderer>();
spriteRenderer.sprite = "test/assets/cat.png";

Entity ground = Entity.SpawnEntity(new Entity());
ground.transform.pos = new SpotEngine.Math.Vec3(0, -5.5f, 0);
ground.transform.scale = new SpotEngine.Math.Vec3(100, 1, 0);
ground.AddController(new SpriteRenderer());

Spot.Instance.game.title = "Meu jogo incrivel feito com Spot Engine";
Spot.Instance.Run(RenderMode.Default, width, height);