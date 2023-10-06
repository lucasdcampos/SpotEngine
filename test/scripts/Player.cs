using SpotEngine;

public class Player : Script
{
    public float gravity = 9;
    float playerGravity;
    public float speed = 7;
    public float jumpForce = 10;
    bool isGrounded;
    bool isJumping;
    public override void Init()
    {
        Camera.main.backgroundColor = new Color(70, 70, 150, 255);
        Input.PrintKeyStates();
    }
    public override void Flow()
    {
        float x = entity.transform.pos.x;
        float y = entity.transform.pos.y;

        if (Input.IsKeyPressed("Escape"))
        {
            Game.LoadLevel(new Level2());
        }

        if (Input.IsKeyPressed("Return"))
        {
            Game.LoadLevel(new MainLevel());
        }

        FlappyBird(x, y);
    }

    float defCooldown = 1.5f;
    float cooldown = 1;
    void FlappyBird(float x, float y)
    {
        cooldown -= Time.deltaTime();

        InputAction jump = new InputAction(new string[] { "Space", "W" });

        if (Input.IsActionTriggering(jump))
        {
            entity.transform.pos = new Vec3(0, y + gravity * Time.deltaTime(), 0);
        }
        else
        {
            entity.transform.pos = new Vec3(0, y - gravity * Time.deltaTime(), 0);
        }

        Collider2D col = (Collider2D)entity.GetController<Collider2D>();

        if (col.OnCollision() != null)
        {
            if(col.OnCollision().entity.name == "Ground" || col.OnCollision().entity.name == "Tube")
            {
                Echo.Alert($"Colidindo com {col.OnCollision().entity.name} em " +
                    $"{col.OnCollision().entity.transform.pos.x}:{col.OnCollision().transform.pos.y}. Minha posição: {entity.transform.pos.x}:{entity.transform.pos.y}");

                Game.LoadLevel(new MainLevel());
            }
        }


        if(cooldown <= 0)
        {
            
            Entity topTube = new Entity();
            topTube.name = "Tube";
            topTube.transform.pos = new Vec3(9, 7 + Rand.Between(0f, 1.3f), 0);
            topTube.transform.scale = new Vec3(1, 8, 0);
            topTube.AddController(new Tube());
            topTube.AddController(new SpriteRenderer());
            topTube.AddController(new Collider2D());
            Entity.SpawnEntity(topTube);

            Entity downTube = new Entity();
            downTube.name = "Tube";
            downTube.transform.pos = new Vec3(9, -7 + Rand.Between(0f, -1.3f), 0);
            downTube.transform.scale = new Vec3(1, 8, 0);
            downTube.AddController(new Tube());
            downTube.AddController(new SpriteRenderer());
            downTube.AddController(new Collider2D());
            //Entity.SpawnEntity(downTube);

            cooldown = defCooldown;
        }


    }

    void PlatformMovement(float x, float y)
    {
        if (entity.transform.pos.y > -4.5f && !isJumping)
        {
            playerGravity = gravity;
            isGrounded = false;
        }
        else
        {
            isJumping = false;
            playerGravity = 0;
            isGrounded = true;
        }

        entity.transform.pos = new Vec3(x, y - playerGravity * Time.deltaTime(), 0);

        if (Input.IsKeyPressed("D"))
        {
            entity.transform.pos = new Vec3(x + speed * Time.deltaTime(), y - playerGravity * Time.deltaTime(), 0);
        }

        if (Input.IsKeyPressed("A"))
        {
            entity.transform.pos = new Vec3(x - speed * Time.deltaTime(), y - playerGravity * Time.deltaTime(), 0);
        }

        if (Input.IsKeyPressed("Space") && isGrounded)
        {
            isGrounded = false;
            isJumping = true;
            entity.transform.pos = new Vec3(x, y + jumpForce * Time.deltaTime(), 0);
            return;
        }
    }
}

