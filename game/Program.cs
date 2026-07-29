using Spot.Core;
using Spot.Game.Scenes;

var spec = new ApplicationSpec
{
    Name = "My Game",
};
spec.Window.Title = "My Game";
spec.Window.Width = 1280;
spec.Window.Height = 720;

var app = new Application(spec);
app.Run(new MenuScene());
