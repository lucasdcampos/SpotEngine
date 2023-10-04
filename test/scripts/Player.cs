using SpotEngine;
using System.Collections;

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
        Input.PrintKeyStates();
    }
    public override void Flow()
    {
        float x = entity.transform.pos.x;
        float y = entity.transform.pos.y;

        if(entity.transform.pos.y > -4.5f && !isJumping)
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

        entity.transform.pos = new SpotEngine.Math.Vec3(x, y - playerGravity * Spot.deltaTime, 0);

        if (Input.IsKeyPressed("D"))
        {
            entity.transform.pos = new SpotEngine.Math.Vec3(x + speed * Spot.deltaTime, y - playerGravity * Spot.deltaTime, 0);
        }

        if (Input.IsKeyPressed("A"))
        {
            entity.transform.pos = new SpotEngine.Math.Vec3(x -speed * Spot.deltaTime, y - playerGravity * Spot.deltaTime, 0);
        }

        if (Input.IsKeyPressed("Space") && isGrounded)
        {
            isGrounded = false;
            isJumping = true;
            entity.transform.pos = new SpotEngine.Math.Vec3(x, y + jumpForce * Spot.deltaTime, 0);
            return;
        }

    }
}

