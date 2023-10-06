
using SpotEngine;

internal class MainLevel : Level
{
    public override void OnLoad()
    {
        name = "Main Level";
        Camera.main.backgroundColor = new Color(70,70,150,255);
        Entity player = Entity.SpawnEntity(new Entity());
        player.AddController(new Player());
        player.AddController(new SpriteRenderer());
        player.AddController(new Collider2D());
        SpriteRenderer spriteRenderer = (SpriteRenderer)player.GetController<SpriteRenderer>();
        spriteRenderer.sprite.texturePath = "assets/cat.png";
        player.name = "Gatinho";
        player.transform.scale = new Vec3(1.5f, 1.5f, 0);

        Entity ground = Entity.SpawnEntity(new Entity());
        ground.transform.pos = new Vec3(0, -9.5f, 0);
        ground.transform.scale = new Vec3(100, 1.5f, 0);
        ground.AddController(new SpriteRenderer());
        ground.AddController(new Collider2D());
        ground.name = "Ground";
    }
}

internal class Level2 : Level
{
    public override void OnLoad()
    {
        name = "Level 2";
        Camera.main.backgroundColor = new Color(0, 0, 150, 255);
        Entity player = Entity.SpawnEntity(new Entity());
        player.AddController(new Player());
        player.AddController(new SpriteRenderer());
        player.AddController(new Collider2D());
        SpriteRenderer spriteRenderer = (SpriteRenderer)player.GetController<SpriteRenderer>();
        spriteRenderer.sprite.texturePath = "assets/cat.png";
        player.name = "Gatinho";
        player.transform.scale = new Vec3(3f, 3f, 0);

    }
}

