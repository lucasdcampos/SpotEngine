using System;
using Spot.Engine;

namespace HelloEngine;

class Program
{
    static void Main(string[] args)
    {
        var app = SpotEngine.CreateApplication();
        app.Run();
    }
}
