using System.IO;
using System.Numerics;
using Spot.Rendering;
using Spot.UI;
using Spot.UI.Serialization;
using Xunit;

namespace Spot.Engine.Tests;

public class UISerializerTests
{
    // Builds a small tree covering every widget type, with names, a nested hierarchy, and guid asset
    // references (guid refs never touch GL on load, so the round-trip is deterministic and headless).
    private static UIRoot BuildDocument()
    {
        var root = new UIRoot { ScaleMode = UIScaleMode.ConstantPixel, ReferenceHeight = 720f };

        var panel = root.Panel();
        panel.Name = "Root";
        panel.Color = new Vector4(0.1f, 0.2f, 0.3f, 0.9f);
        panel.SpriteRef = "guid:1111";
        panel.Rect = new UIRect { Anchor = new Vector2(0.5f, 0.5f), Pivot = new Vector2(0.5f, 0.5f), Position = new Vector2(4f, 8f), Size = new Vector2(400f, 300f) };

        var button = panel.Button("Play");
        button.Name = "Play";
        button.FontRef = "guid:2222";
        button.FontSize = 30f;
        button.TextColor = new Vector4(1f, 0.9f, 0.8f, 1f);

        var text = panel.Text("Hello");
        text.Name = "Title";
        text.Align = TextAlign.Center;
        text.Wrap = true;

        var image = panel.Add(new Image { Name = "Logo", TextureRef = "guid:3333", Color = new Vector4(1f, 0f, 0f, 1f) });
        _ = image;

        var slider = panel.Slider();
        slider.Name = "Volume";
        slider.Min = 0f;
        slider.Max = 2f;
        slider.Value = 1.5f;

        var toggle = panel.Toggle("Mute");
        toggle.Name = "Mute";
        toggle.On = true;

        return root;
    }

    [Fact]
    public void RoundTrip_PreservesStructureFieldsAndAssetRefs()
    {
        UIRoot original = BuildDocument();
        string path = Path.Combine(Path.GetTempPath(), $"ui_{System.Guid.NewGuid():N}.sptui");
        try
        {
            UISerializer.Save(original, path);
            UIRoot loaded = UISerializer.Load(path);

            Assert.Equal(UIScaleMode.ConstantPixel, loaded.ScaleMode);
            Assert.Equal(720f, loaded.ReferenceHeight);

            Assert.Single(loaded.Children);
            var panel = Assert.IsType<Panel>(loaded.Children[0]);
            Assert.Equal("Root", panel.Name);
            Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.9f), panel.Color);
            Assert.Equal("guid:1111", panel.SpriteRef);
            Assert.Equal(new Vector2(400f, 300f), panel.Rect.Size);
            Assert.Equal(new Vector2(0.5f, 0.5f), panel.Rect.Anchor);
            Assert.Equal(5, panel.Children.Count);

            var button = panel.Find<Button>("Play");
            Assert.NotNull(button);
            Assert.Equal("Play", button!.Label);
            Assert.Equal("guid:2222", button.FontRef);
            Assert.Equal(30f, button.FontSize);

            var text = panel.Find<Text>("Title");
            Assert.NotNull(text);
            Assert.Equal("Hello", text!.Content);
            Assert.Equal(TextAlign.Center, text.Align);
            Assert.True(text.Wrap);

            var image = panel.Find<Image>("Logo");
            Assert.NotNull(image);
            Assert.Equal("guid:3333", image!.TextureRef);

            var slider = panel.Find<Slider>("Volume");
            Assert.NotNull(slider);
            Assert.Equal(2f, slider!.Max);
            Assert.Equal(1.5f, slider.Value);

            var toggle = panel.Find<Toggle>("Mute");
            Assert.NotNull(toggle);
            Assert.True(toggle!.On);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Find_ReturnsFirstMatchByNameAndType()
    {
        UIRoot root = BuildDocument();

        Assert.Same(root.Find("Play"), root.Find<Button>("Play"));
        Assert.Null(root.Find<Text>("Play"));   // wrong type
        Assert.Null(root.Find("DoesNotExist"));
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyDocumentWithoutThrowing()
    {
        UIRoot loaded = UISerializer.Load(Path.Combine(Path.GetTempPath(), "no_such_ui_file.sptui"));
        Assert.Empty(loaded.Children);
    }

    [Fact]
    public void Clone_ProducesIndependentCopy()
    {
        UIRoot root = BuildDocument();
        Widget panel = root.Children[0];

        Widget copy = UISerializer.Clone(panel);
        Assert.NotSame(panel, copy);
        Assert.Equal("Root", copy.Name);
        Assert.Equal(panel.Children.Count, copy.Children.Count);

        copy.Name = "Changed";
        Assert.Equal("Root", panel.Name); // mutating the copy leaves the original untouched
    }
}
