// Level 2 — Spot.Framework in 3D: a camera, lit primitives, an instanced field of cubes, and (optionally) a model
// loaded straight from a source file with Assimp — no cooking, no engine — animated with a Skeleton when it is
// rigged. Drag with the left mouse button to orbit, scroll to zoom, Escape quits.
//
//   dotnet run --project samples/Hello3D                      primitives only
//   dotnet run --project samples/Hello3D -- path/to/model.fbx  plus a model (its first clip plays if it has one)
//   ... --frames N                                            exits after N frames (smoke tests)
using System.Numerics;
using Spot.Framework;
using Spot.Framework.Animation;
using Spot.Framework.Assimp;
using Spot.Framework.Graphics;

int maxFrames = -1;
string? modelPath = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--frames" && i + 1 < args.Length && int.TryParse(args[i + 1], out int n))
    {
        maxFrames = n;
        i++;
    }
    else
    {
        modelPath = args[i];
    }
}

using var window = new Window(new WindowSpec { Title = "Spot — Hello 3D (level 2)", Width = 1280, Height = 720 });
Renderer.SetClearColor(0.55f, 0.68f, 0.82f, 1.0f);

var camera = new Camera3D { Target = new Vector3(0, 0.8f, 0) };
float yaw = 0.6f, pitch = 0.35f, distance = 9.0f;

Model cube = PrimitiveModelFactory.Create("cube");
Model sphere = PrimitiveModelFactory.Create("sphere");
Model plane = PrimitiveModelFactory.Create("plane");

using var checker = Texture2D.CreateCheckerboard();
var ground = new BasicMaterial { Color = new Vector4(0.8f, 0.8f, 0.85f, 1), Texture = checker };
var red = new BasicMaterial { Color = new Vector4(0.9f, 0.25f, 0.2f, 1) };
var glow = new BasicMaterial { Color = new Vector4(1.0f, 0.85f, 0.3f, 1), Lit = false };
var teal = new BasicMaterial { Color = new Vector4(0.2f, 0.65f, 0.7f, 1) };

// A field of cubes drawn with one instanced call.
Matrix4x4[] field = Enumerable.Range(0, 15 * 15)
    .Select(i => Matrix4x4.CreateScale(0.3f) * Matrix4x4.CreateTranslation((i % 15 - 7) * 0.9f, 0.15f, (i / 15 - 7) * 0.9f - 12f))
    .ToArray();

// Optional: load a source model directly (FBX, glTF, OBJ, ...), and animate it if it is rigged.
Model? model = null;
Skeleton? skeleton = null;
Matrix4x4[] pose = Array.Empty<Matrix4x4>();
Matrix4x4[] palette = new Matrix4x4[BasicRenderer3D.MaxBones];
float modelScale = 1.0f;
if (modelPath is not null)
{
    try
    {
        var importer = new AssimpModelImporter();
        ModelImporter.Register(importer);
        model = ModelImporter.Load(modelPath);

        // Scale the model to about 2 units tall, whatever its authoring units.
        float height = model.LocalBounds.Max.Y - model.LocalBounds.Min.Y;
        modelScale = height > 0.0001f ? 2.0f / height : 1.0f;

        if (model.HasSkinning)
        {
            skeleton = Skeleton.FromModelNodes(importer.ImportSceneInfo(modelPath).Root);
            pose = new Matrix4x4[skeleton.NodeCount];
        }
    }
    catch (Exception ex)
    {
        Log.Error("Could not load '{0}': {1}", modelPath, ex.Message);
    }
}

Vector2 lastMouse = Input.MousePosition;
for (int frame = 0; window.IsOpen && frame != maxFrames; frame++)
{
    window.PollEvents();
    Time.Tick();
    if (Input.GetKeyDown(Key.Escape))
    {
        window.Close();
    }

    // Orbit and zoom.
    Vector2 mouse = Input.MousePosition;
    if (Input.GetMouseButton(MouseButton.Left))
    {
        yaw -= (mouse.X - lastMouse.X) * 0.008f;
        pitch = Math.Clamp(pitch + (mouse.Y - lastMouse.Y) * 0.008f, -0.2f, 1.4f);
    }

    lastMouse = mouse;
    distance = Math.Clamp(distance - Input.MouseScrollDelta.Y * 0.6f, 3.0f, 30.0f);
    camera.Position = camera.Target + distance * new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
    camera.SetViewport(window.FramebufferWidth, window.FramebufferHeight);

    Renderer.Clear();
    BasicRenderer3D.BeginScene(camera, lightDirection: new Vector3(-0.4f, -1.0f, -0.3f), ambient: 0.3f);

    BasicRenderer3D.DrawModel(plane, Matrix4x4.CreateScale(30.0f), ground);
    BasicRenderer3D.DrawModel(cube, Matrix4x4.CreateRotationY(Time.ElapsedTime) * Matrix4x4.CreateTranslation(-2.5f, 0.5f, 0), red);
    BasicRenderer3D.DrawModel(sphere, Matrix4x4.CreateTranslation(2.5f, 0.5f + 0.25f * MathF.Sin(Time.ElapsedTime * 2), 0), glow);
    BasicRenderer3D.DrawMeshInstanced(cube.Meshes[0], field, teal);

    if (model is not null)
    {
        Matrix4x4 world = Matrix4x4.CreateScale(modelScale) * Matrix4x4.CreateTranslation(0, 0, 1.5f);
        AnimationClip? clip = model.Animations.Count > 0 ? model.Animations[0] : null;
        skeleton?.SamplePose(clip, Time.ElapsedTime, loop: true, pose);

        for (int i = 0; i < model.Meshes.Count; i++)
        {
            if (skeleton is not null && model.BonesFor(i) is { Count: > 0 } bones)
            {
                Span<Matrix4x4> bonePalette = palette.AsSpan(0, Math.Min(bones.Count, palette.Length));
                skeleton.ComputeSkinningPalette(bones.Take(bonePalette.Length).ToList(), pose, world, bonePalette);
                BasicRenderer3D.DrawSkinnedMesh(model.Meshes[i], bonePalette);
            }
            else
            {
                BasicRenderer3D.DrawMesh(model.Meshes[i], world);
            }
        }
    }

    BasicRenderer3D.EndScene();
    window.SwapBuffers();
}
