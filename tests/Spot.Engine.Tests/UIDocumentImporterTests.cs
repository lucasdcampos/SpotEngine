using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spot.Assets;
using Xunit;

namespace Spot.Engine.Tests;

public class UIDocumentImporterTests
{
    // A minimal valid 24-bit BMP so the referenced texture gets a guid during Refresh/CookAll.
    private static byte[] MakeBmp(int width, int height)
    {
        int rowSize = (width * 3 + 3) & ~3;
        int pixelData = rowSize * height;
        int fileSize = 54 + pixelData;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(fileSize);
        w.Write(0);
        w.Write(54);
        w.Write(40);
        w.Write(width);
        w.Write(height);
        w.Write((short)1);
        w.Write((short)24);
        w.Write(0);
        w.Write(pixelData);
        w.Write(2835); w.Write(2835);
        w.Write(0); w.Write(0);
        for (int i = 0; i < pixelData; i++) w.Write((byte)200);
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void CookAll_RewritesUIDocumentTextureRefToGuid()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Textures"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "UI"));

        File.WriteAllBytes(Path.Combine(temp.Path, "Textures", "pixel.bmp"), MakeBmp(2, 2));

        // A document whose single Image widget references the texture by source path.
        File.WriteAllText(Path.Combine(temp.Path, "UI", "Main.sptui"), """
        {
          "ScaleMode": "ScaleWithHeight",
          "ReferenceHeight": 1080,
          "Widgets": [
            { "Type": "Image", "Name": "Logo", "TextureRef": "Textures/pixel.bmp",
              "Rect": { "Anchor": [0,0], "Pivot": [0,0], "Position": [0,0], "Size": [100,100] } }
          ]
        }
        """);

        string contentRoot = Path.Combine(temp.Path, "..", "content-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            string manifestPath = AssetDatabase.CookAll(temp.Path, contentRoot).ManifestPath;
            var doc = JsonSerializer.Deserialize<ManifestDocument>(File.ReadAllText(manifestPath), AssetManifest.JsonOptions)!;

            Assert.True(AssetDatabase.TryGetGuid("Textures/pixel.bmp", out string imageGuid));
            Assert.True(AssetDatabase.TryGetGuid("UI/Main.sptui", out string uiGuid));
            Assert.Equal("uidoc", doc.Entries[uiGuid].Type);

            JsonObject cooked = JsonNode.Parse(File.ReadAllText(Path.Combine(contentRoot, doc.Entries[uiGuid].File)))!.AsObject();
            string cookedRef = cooked["Widgets"]!.AsArray()[0]!["TextureRef"]!.GetValue<string>();
            Assert.Equal(AssetRef.MakeGuidRef(imageGuid), cookedRef);
        }
        finally
        {
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
        }
    }
}
