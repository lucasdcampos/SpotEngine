/*
This file is used for testing purposes only
You can test a simple game with it, or just
debug specific things
*/

using SpotEngine;
using SpotEngine.Math;

Entity mySquare = new Entity();
mySquare.transform.scale = new Vec3(64, 64, 0);

//mySquare.AddController(new SquareRenderer2D());
Entity.SpawnEntity(mySquare);


Spot.Instance.Run(new SFMLRenderer(1024,768));