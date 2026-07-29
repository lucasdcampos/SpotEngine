using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Spot.Rendering;

namespace Spot.Scenes;

public class SceneData
{
    public List<EntityData> Entities { get; set; } = new();
}

public class EntityData
{
    public TagComponent? Tag { get; set; }
    public TransformData? Transform { get; set; }
    public Sprite2DData? Sprite { get; set; }
}

public class TransformData
{
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = new float[3];
    public float[] Scale { get; set; } = new float[3] { 1, 1, 1 };
}

public class Sprite2DData
{
    public float[] Color { get; set; } = new float[4] { 1, 1, 1, 1 };
}

public class SceneSerializer
{
    private readonly Scene _scene;

    public SceneSerializer(Scene scene)
    {
        _scene = scene;
    }

    public void Serialize(string filepath)
    {
        var data = new SceneData();

        foreach (var entity in _scene.View<TagComponent>())
        {
            var entityData = new EntityData();

            entityData.Tag = entity.GetComponent<TagComponent>();

            if (entity.HasComponent<Transform>())
            {
                var transform = entity.GetComponent<Transform>();
                entityData.Transform = new TransformData
                {
                    Position = new[] { transform.Position.X, transform.Position.Y, transform.Position.Z },
                    Rotation = new[] { transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z },
                    Scale = new[] { transform.Scale.X, transform.Scale.Y, transform.Scale.Z }
                };
            }

            if (entity.HasComponent<Sprite2D>())
            {
                var sprite = entity.GetComponent<Sprite2D>();
                entityData.Sprite = new Sprite2DData
                {
                    Color = new[] { sprite.Color.X, sprite.Color.Y, sprite.Color.Z, sprite.Color.W }
                };
            }

            data.Entities.Add(entityData);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(filepath, json);
    }

    public bool Deserialize(string filepath)
    {
        if (!File.Exists(filepath))
        {
            return false;
        }

        string json = File.ReadAllText(filepath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<SceneData>(json, options);

        if (data == null)
        {
            return false;
        }

        foreach (var entityData in data.Entities)
        {
            string name = entityData.Tag?.Name ?? "Entity";
            var entity = _scene.Instantiate(name);

            if (entityData.Transform != null)
            {
                var transform = entity.GetComponent<Transform>();
                transform.Position = new System.Numerics.Vector3(entityData.Transform.Position[0], entityData.Transform.Position[1], entityData.Transform.Position[2]);
                transform.Rotation = new System.Numerics.Vector3(entityData.Transform.Rotation[0], entityData.Transform.Rotation[1], entityData.Transform.Rotation[2]);
                transform.Scale = new System.Numerics.Vector3(entityData.Transform.Scale[0], entityData.Transform.Scale[1], entityData.Transform.Scale[2]);
            }

            if (entityData.Sprite != null)
            {
                var sprite = new Sprite2D();
                sprite.Color = new System.Numerics.Vector4(entityData.Sprite.Color[0], entityData.Sprite.Color[1], entityData.Sprite.Color[2], entityData.Sprite.Color[3]);
                entity.AddComponent(sprite);
            }
        }

        return true;
    }
}
