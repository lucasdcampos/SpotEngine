using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spot.Animation;
using Spot.Assets;
using Spot.Rendering;
using Xunit;

namespace Spot.Engine.Tests;

public class AssetPipelineTests
{
    [Fact]
    public void AssetMeta_ReadOrCreate_MintsStable32HexGuid()
    {
        using var temp = new TempDir();
        string source = Path.Combine(temp.Path, "tex.png");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3 });

        AssetMeta created = AssetMeta.ReadOrCreate(source, "texture");
        Assert.Equal(32, created.Guid.Length);
        Assert.Equal("texture", created.Importer);
        created.Save(source);

        AssetMeta reloaded = AssetMeta.ReadOrCreate(source, "texture");
        Assert.Equal(created.Guid, reloaded.Guid);
        Assert.True(File.Exists(AssetMeta.MetaPathFor(source)));
    }

    [Fact]
    public void AssetMeta_ComputeSourceHash_ChangesWithContentAndSettings()
    {
        using var temp = new TempDir();
        string source = Path.Combine(temp.Path, "model.bin");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });

        string first = AssetMeta.ComputeSourceHash(source);
        Assert.Equal(first, AssetMeta.ComputeSourceHash(source));

        File.WriteAllBytes(source, new byte[] { 9, 9, 9, 9 });
        Assert.NotEqual(first, AssetMeta.ComputeSourceHash(source));

        var settings = new JsonObject { ["pointFilter"] = true };
        Assert.NotEqual(AssetMeta.ComputeSourceHash(source), AssetMeta.ComputeSourceHash(source, settings));
    }

    [Fact]
    public void SpMesh_RoundTrips_MultipleSubmeshes()
    {
        var submeshes = new[]
        {
            new MeshData(
                new float[] { 0, 0, 0, 0, 1, 0, 0.5f, 0.5f, 1, 0, 0, 0, 1, 0, 1, 1 },
                new uint[] { 0, 1, 0 }),
            new MeshData(
                new float[] { -1, -1, -1, 0, 0, 1, 0, 0 },
                new uint[] { 0 }),
        };

        byte[] blob = SpMesh.Write(submeshes);
        var loaded = SpMesh.Read(blob);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(submeshes[0].Vertices, loaded[0].Vertices);
        Assert.Equal(submeshes[0].Indices, loaded[0].Indices);
        Assert.Equal(submeshes[1].Vertices, loaded[1].Vertices);
        Assert.Equal(submeshes[1].Indices, loaded[1].Indices);
    }

    [Fact]
    public void SpMesh_Read_RejectsBadMagic()
    {
        Assert.Throws<InvalidDataException>(() => SpMesh.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    [Fact]
    public void SpMesh_RoundTrips_SkinnedBonesAndClips()
    {
        var bones = new[]
        {
            new BoneInfo("mixamorig:Hips", Matrix4x4.CreateTranslation(1, 2, 3)),
            new BoneInfo("mixamorig:Spine", Matrix4x4.CreateScale(2)),
        };

        // One skinned vertex: eight rigid floats followed by four bone indices and four weights.
        var skinnedVerts = new float[Mesh.SkinnedFloatsPerVertex];
        for (int i = 0; i < skinnedVerts.Length; i++) skinnedVerts[i] = i * 0.25f;

        var skinned = new MeshData(skinnedVerts, new uint[] { 0 }, skinned: true, bones);
        var rigid = new MeshData(new float[] { 0, 0, 0, 0, 1, 0, 0.5f, 0.5f }, new uint[] { 0 });

        var channel = new AnimationChannel(
            "mixamorig:Hips",
            new[] { new Keyframe<Vector3>(0f, Vector3.Zero), new Keyframe<Vector3>(1f, new Vector3(1, 2, 3)) },
            new[] { new Keyframe<Quaternion>(0f, Quaternion.Identity) },
            new[] { new Keyframe<Vector3>(0f, Vector3.One) });
        var clip = new AnimationClip("Run", 1.5f, new[] { channel });

        byte[] blob = SpMesh.Write(new CookedModel(new[] { skinned, rigid }, new[] { clip }));
        CookedModel loaded = SpMesh.ReadModel(blob);

        Assert.Equal(2, loaded.Submeshes.Count);

        Assert.True(loaded.Submeshes[0].Skinned);
        Assert.Equal(skinnedVerts, loaded.Submeshes[0].Vertices);
        Assert.NotNull(loaded.Submeshes[0].Bones);
        Assert.Equal(2, loaded.Submeshes[0].Bones!.Count);
        Assert.Equal("mixamorig:Hips", loaded.Submeshes[0].Bones![0].Name);
        Assert.Equal(bones[0].InverseBind, loaded.Submeshes[0].Bones![0].InverseBind);

        Assert.False(loaded.Submeshes[1].Skinned);
        Assert.Null(loaded.Submeshes[1].Bones);

        Assert.Single(loaded.Animations);
        AnimationClip loadedClip = loaded.Animations[0];
        Assert.Equal("Run", loadedClip.Name);
        Assert.Equal(1.5f, loadedClip.Duration, 3);
        Assert.Single(loadedClip.Channels);
        Assert.Equal("mixamorig:Hips", loadedClip.Channels[0].NodeName);
        Assert.Equal(2, loadedClip.Channels[0].PositionKeys.Count);
        Assert.Equal(new Vector3(1, 2, 3), loadedClip.Channels[0].PositionKeys[1].Value);
    }

    [Fact]
    public void SpMesh_Reads_Version1BlobAsRigidWithoutBonesOrClips()
    {
        // Hand-write a version-1 blob (no floatsPerVertex, no bones, no clip table) to prove the reader still
        // accepts assets cooked by older builds.
        var vertices = new float[] { 0, 0, 0, 0, 1, 0, 0.5f, 0.5f };
        var indices = new uint[] { 0 };

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(new byte[] { (byte)'S', (byte)'P', (byte)'M', (byte)'H' });
            w.Write(1u);                        // version
            w.Write(1u);                        // submeshCount
            w.Write((uint)vertices.Length);
            w.Write((uint)indices.Length);
            foreach (float f in vertices) w.Write(f);
            foreach (uint i in indices) w.Write(i);
        }

        CookedModel loaded = SpMesh.ReadModel(ms.ToArray());

        Assert.Single(loaded.Submeshes);
        Assert.False(loaded.Submeshes[0].Skinned);
        Assert.Null(loaded.Submeshes[0].Bones);
        Assert.Equal(vertices, loaded.Submeshes[0].Vertices);
        Assert.Empty(loaded.Animations);
    }

    [Fact]
    public void SpTex_RoundTrips_PixelsAndFilterFlag()
    {
        uint w = 2, h = 2;
        byte[] rgba =
        {
            255, 0, 0, 255,   0, 255, 0, 255,
            0, 0, 255, 255,   255, 255, 255, 128,
        };

        byte[] blob = SpTex.Write(w, h, rgba, pointFilter: true);
        SpTexData loaded = SpTex.Read(blob);

        Assert.Equal(w, loaded.Width);
        Assert.Equal(h, loaded.Height);
        Assert.True(loaded.PointFilter);
        Assert.Equal(rgba, loaded.Rgba);
    }

    [Fact]
    public void SpTex_Write_RejectsMismatchedPixelCount()
    {
        Assert.Throws<System.ArgumentException>(() => SpTex.Write(4, 4, new byte[4], pointFilter: false));
    }

    [Fact]
    public void SpTex_Read_RejectsTruncatedPixels()
    {
        byte[] blob = SpTex.Write(2, 2, new byte[16], pointFilter: false);
        Assert.Throws<InvalidDataException>(() => SpTex.Read(blob.AsSpan(0, blob.Length - 4)));
    }

    // A minimal, valid 2x2 32-bit BMP — enough to exercise the texture importer's decode path without
    // depending on an encoder. (BMP is one of the importer's supported source extensions.)
    private static byte[] MakeBmp(uint width, uint height)
    {
        uint pixelBytes = width * height * 4;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'B');
        w.Write((byte)'M');
        w.Write(54u + pixelBytes);   // file size
        w.Write(0u);                 // reserved
        w.Write(54u);                // pixel data offset
        w.Write(40u);                // info header size
        w.Write((int)width);
        w.Write((int)height);
        w.Write((ushort)1);          // planes
        w.Write((ushort)32);         // bits per pixel
        w.Write(0u);                 // BI_RGB
        w.Write(pixelBytes);
        w.Write(0);                  // x pixels-per-meter
        w.Write(0);                  // y pixels-per-meter
        w.Write(0u);                 // colors used
        w.Write(0u);                 // colors important
        for (uint i = 0; i < width * height; i++)
        {
            w.Write((byte)32);       // B
            w.Write((byte)64);       // G
            w.Write((byte)128);      // R
            w.Write((byte)255);      // A
        }

        w.Flush();
        return ms.ToArray();
    }

    private static string SeedProject(string assetsRoot)
    {
        Directory.CreateDirectory(Path.Combine(assetsRoot, "Textures"));
        Directory.CreateDirectory(Path.Combine(assetsRoot, "Materials"));
        Directory.CreateDirectory(Path.Combine(assetsRoot, "Scenes"));

        string image = Path.Combine(assetsRoot, "Textures", "pixel.bmp");
        File.WriteAllBytes(image, MakeBmp(2, 2));
        File.WriteAllText(Path.Combine(assetsRoot, "Materials", "mat.sptmat"),
            "{\"Color\":[1,1,1,1],\"TexturePath\":\"Textures/pixel.bmp\"}");
        File.WriteAllText(Path.Combine(assetsRoot, "Scenes", "Main.sptscene"), "{\n  \"Entities\": []\n}\n");
        return image;
    }

    [Fact]
    public void AssetDatabase_Refresh_MintsGuidsAndResolvesReferences()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);

        AssetDatabase.Refresh(temp.Path);

        Assert.True(AssetDatabase.TryGetGuid("Textures/pixel.bmp", out string guid));
        Assert.Equal(32, guid.Length);
        Assert.True(AssetDatabase.TryGetSource(guid, out _));
        Assert.True(File.Exists(AssetMeta.MetaPathFor(Path.Combine(temp.Path, "Textures", "pixel.bmp"))));

        Assert.Equal(AssetRef.MakeGuidRef(guid), AssetDatabase.ToGuidRef("Textures/pixel.bmp"));
    }

    [Fact]
    public void AssetDatabase_ToGuidRef_PassesThroughPseudoGuidAndUnknown()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);
        AssetDatabase.Refresh(temp.Path);

        Assert.Equal("primitive:Cube", AssetDatabase.ToGuidRef("primitive:Cube"));
        Assert.Equal("editor:Checkerboard", AssetDatabase.ToGuidRef("editor:Checkerboard"));
        Assert.Equal("guid:abc123", AssetDatabase.ToGuidRef("guid:abc123"));
        Assert.Equal("Textures/missing.bmp", AssetDatabase.ToGuidRef("Textures/missing.bmp"));
    }

    [Fact]
    public void AssetPath_TreatsGuidRefAsPseudoPath()
    {
        const string guidRef = "guid:0123456789abcdef0123456789abcdef";
        string previousRoot = AssetPath.Root;
        try
        {
            AssetPath.Root = Path.GetTempPath();
            Assert.True(AssetPath.IsPseudoPath(guidRef));
            // Pass through both directions untouched, exactly like primitive:/editor:.
            Assert.Equal(guidRef, AssetPath.Resolve(guidRef));
            Assert.Equal(guidRef, AssetPath.MakeRelative(guidRef));
        }
        finally
        {
            AssetPath.Root = previousRoot;
        }
    }

    [Fact]
    public void AssetPath_ResolveContent_UsesInstalledHookForGuidRefsOnly()
    {
        AssetPath.ContentResolver = r => r == "guid:abc" ? "/cooked/abc.sptex" : null;
        try
        {
            Assert.Equal("/cooked/abc.sptex", AssetPath.ResolveContent("guid:abc"));
            Assert.Null(AssetPath.ResolveContent("guid:unknown"));
            Assert.Null(AssetPath.ResolveContent("Textures/pixel.bmp")); // not a guid ref -> hook not consulted
        }
        finally
        {
            AssetPath.ContentResolver = null;
        }
    }

    [Fact]
    public void AssetManifest_ResolvesGuidRefToCookedFileUnderContent()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);
        string contentRoot = Path.Combine(temp.Path, "Content");
        AssetDatabase.CookAll(temp.Path, contentRoot);

        AssetManifest manifest = AssetManifest.Load(Path.Combine(contentRoot, "manifest.json"));
        AssetDatabase.TryGetGuid("Textures/pixel.bmp", out string imageGuid);

        string? resolved = manifest.ResolveGuidRef(AssetRef.MakeGuidRef(imageGuid));
        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved));
        Assert.Null(manifest.ResolveGuidRef("guid:doesnotexist"));
    }

    [Fact]
    public void AssetDatabase_ToDisplayPath_MapsGuidBackToSource()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);
        AssetDatabase.Refresh(temp.Path);

        AssetDatabase.TryGetGuid("Textures/pixel.bmp", out string guid);
        Assert.EndsWith("pixel.bmp", AssetDatabase.ToDisplayPath(AssetRef.MakeGuidRef(guid)));
        Assert.Equal("primitive:Cube", AssetDatabase.ToDisplayPath("primitive:Cube"));
    }

    [Fact]
    public void AssetDatabase_ToGuidRef_IndexesSourceAddedAfterRefresh()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);
        AssetDatabase.Refresh(temp.Path);

        string added = Path.Combine(temp.Path, "Textures", "added.bmp");
        File.WriteAllBytes(added, MakeBmp(2, 2));

        string? guidRef = AssetDatabase.ToGuidRef("Textures/added.bmp");
        Assert.StartsWith("guid:", guidRef);
        Assert.True(File.Exists(AssetMeta.MetaPathFor(added)));
    }

    [Fact]
    public void AssetDatabase_LibraryResolver_CooksAndResolvesGuidThroughAssetPath()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);
        AssetDatabase.Refresh(temp.Path);
        AssetDatabase.InstallLibraryResolver(Path.Combine(temp.Path, "Library"));
        try
        {
            AssetDatabase.TryGetGuid("Textures/pixel.bmp", out string guid);
            string? cooked = AssetPath.ResolveContent(AssetRef.MakeGuidRef(guid));
            Assert.NotNull(cooked);
            Assert.True(File.Exists(cooked));
            Assert.EndsWith(".sptex", cooked);
        }
        finally
        {
            AssetPath.ContentResolver = null;
        }
    }

    [Fact]
    public void AssetDatabase_CookAll_WritesManifestCookedTextureAndRewrittenMaterial()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);
        string contentRoot = Path.Combine(temp.Path, "..", "content-" + System.Guid.NewGuid().ToString("N"));

        try
        {
            string manifestPath = AssetDatabase.CookAll(temp.Path, contentRoot).ManifestPath;
            Assert.True(File.Exists(manifestPath));

            var doc = JsonSerializer.Deserialize<ManifestDocument>(File.ReadAllText(manifestPath), AssetManifest.JsonOptions)!;

            AssetDatabase.TryGetGuid("Textures/pixel.bmp", out string imageGuid);
            AssetDatabase.TryGetGuid("Materials/mat.sptmat", out string matGuid);

            Assert.Equal("texture", doc.Entries[imageGuid].Type);
            Assert.Equal("material", doc.Entries[matGuid].Type);

            // Cooked texture is a real 2x2 .sptex.
            SpTexData tex = SpTex.ReadFile(Path.Combine(contentRoot, doc.Entries[imageGuid].File));
            Assert.Equal(2u, tex.Width);
            Assert.Equal(2u, tex.Height);

            // Cooked material's texture reference was rewritten from the source path to the texture's guid.
            JsonObject cookedMat = JsonNode.Parse(File.ReadAllText(Path.Combine(contentRoot, doc.Entries[matGuid].File)))!.AsObject();
            Assert.Equal(AssetRef.MakeGuidRef(imageGuid), cookedMat["TexturePath"]!.GetValue<string>());

            // Scenes are copied through by path, not cooked to a guid.
            Assert.True(File.Exists(Path.Combine(contentRoot, "Scenes", "Main.sptscene")));
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void AssetDatabase_CookAll_RewritesSceneModelAndMaterialReferencesToGuids()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);

        // A model source alongside the seeded texture/material...
        File.WriteAllText(Path.Combine(temp.Path, "model.obj"), "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");

        // ...and a scene that references both by raw source path — exactly what dragging a model into the scene
        // leaves behind before any guid migration. This overwrites the empty scene the seed wrote.
        File.WriteAllText(Path.Combine(temp.Path, "Scenes", "Main.sptscene"), """
        {
          "Entities": [
            {
              "Tag": { "Name": "Model", "Enabled": true },
              "Mesh": { "ModelPath": "model.obj", "SubmeshIndex": 0, "MaterialPath": "Materials/mat.sptmat" }
            }
          ]
        }
        """);

        string contentRoot = Path.Combine(temp.Path, "..", "content-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            AssetDatabase.CookAll(temp.Path, contentRoot);

            Assert.True(AssetDatabase.TryGetGuid("model.obj", out string modelGuid));
            Assert.True(AssetDatabase.TryGetGuid("Materials/mat.sptmat", out string matGuid));

            // The shipped scene must reference cooked artifacts by guid (resolved via the manifest at runtime),
            // never the raw source paths that have no counterpart in Content — otherwise the model and its
            // materials fail to load in the built game even though they render in the editor.
            JsonObject mesh = JsonNode.Parse(File.ReadAllText(Path.Combine(contentRoot, "Scenes", "Main.sptscene")))!
                .AsObject()["Entities"]!.AsArray()[0]!.AsObject()["Mesh"]!.AsObject();
            Assert.Equal(AssetRef.MakeGuidRef(modelGuid), mesh["ModelPath"]!.GetValue<string>());
            Assert.Equal(AssetRef.MakeGuidRef(matGuid), mesh["MaterialPath"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void AssetDatabase_CookAll_RemovesStaleContentWhenSourceDeleted()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);
        string contentRoot = Path.Combine(temp.Path, "..", "content-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            AssetDatabase.CookAll(temp.Path, contentRoot);
            Assert.True(AssetDatabase.TryGetGuid("Textures/pixel.bmp", out string imageGuid));
            string cookedTexture = Path.Combine(contentRoot, imageGuid[..2], imageGuid + ".sptex");
            Assert.True(File.Exists(cookedTexture));

            // Delete the source (and its .meta/dependent material) and re-cook. Because Content/ is rebuilt
            // from scratch, the deleted source's cooked artifact must not linger in the shipped output.
            string image = Path.Combine(temp.Path, "Textures", "pixel.bmp");
            File.Delete(image);
            File.Delete(AssetMeta.MetaPathFor(image));
            File.Delete(Path.Combine(temp.Path, "Materials", "mat.sptmat"));

            var result = AssetDatabase.CookAll(temp.Path, contentRoot);

            Assert.False(File.Exists(cookedTexture));
            var doc = JsonSerializer.Deserialize<ManifestDocument>(File.ReadAllText(result.ManifestPath), AssetManifest.JsonOptions)!;
            Assert.False(doc.Entries.ContainsKey(imageGuid));
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void AssetDatabase_CookAll_ReportsFailureCountAndOmitsBadAssetFromManifest()
    {
        using var temp = new TempDir();
        SeedProject(temp.Path);

        // A .png the importer can't decode: cooking must fail for this one source, keep going for the rest,
        // and the failure must be counted (not silently swallowed) so callers can surface it.
        string broken = Path.Combine(temp.Path, "Textures", "broken.png");
        File.WriteAllBytes(broken, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });

        string contentRoot = Path.Combine(temp.Path, "..", "content-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var result = AssetDatabase.CookAll(temp.Path, contentRoot);

            Assert.True(result.Failed >= 1);
            Assert.True(AssetDatabase.TryGetGuid("Textures/broken.png", out string brokenGuid));
            var doc = JsonSerializer.Deserialize<ManifestDocument>(File.ReadAllText(result.ManifestPath), AssetManifest.JsonOptions)!;
            Assert.False(doc.Entries.ContainsKey(brokenGuid));

            // The healthy texture alongside it still cooks.
            Assert.True(AssetDatabase.TryGetGuid("Textures/pixel.bmp", out string goodGuid));
            Assert.True(doc.Entries.ContainsKey(goodGuid));
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }
}
