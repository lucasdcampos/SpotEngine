using System;
using Spot;
using Spot.Core;

namespace Sandbox;

class Program
{
    static void Main(string[] args)
    {
        var app = SpotEngine.CreateApplication();
        app.Run();
    }
}
