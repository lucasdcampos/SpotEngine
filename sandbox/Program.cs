using System;
using Spot.Core;

namespace Sandbox;

class Program
{
    static void Main(string[] args)
    {
        var spec = ApplicationSpec.Load("game.manifest");
        var app = new Application(spec);
        app.Run();
    }
}
