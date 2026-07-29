using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public ScriptComponentData? Scripts { get; set; }
    public CameraComponentData? Camera { get; set; }
    public List<EntityData>? Children { get; set; }
}

public class CameraComponentData
{
    public bool Primary { get; set; }
    public bool FixedAspectRatio { get; set; }
    public float ZoomLevel { get; set; }
    public float[]? BackgroundColor { get; set; }
}

public class ScriptComponentData
{
    public List<string> ScriptNames { get; set; } = new();
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

    public string SerializeToString()
    {
        var data = new SceneData();

        foreach (var entity in _scene.View<TagComponent>())
        {
            if (entity.Parent == null)
            {
                data.Entities.Add(SerializeEntity(entity));
            }
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(data, options);
    }

    private EntityData SerializeEntity(Entity entity)
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

        if (entity.HasComponent<CameraComponent>())
        {
            var camera = entity.GetComponent<CameraComponent>();
            entityData.Camera = new CameraComponentData
            {
                Primary = camera.Primary,
                FixedAspectRatio = camera.FixedAspectRatio,
                ZoomLevel = camera.ZoomLevel,
                BackgroundColor = new[] { camera.BackgroundColor.X, camera.BackgroundColor.Y, camera.BackgroundColor.Z, camera.BackgroundColor.W }
            };
        }

        if (entity.HasComponent<ScriptComponent>())
        {
            var scriptComp = entity.GetComponent<ScriptComponent>();
            var scriptNames = new List<string>();
            foreach (var script in scriptComp.Scripts)
            {
                scriptNames.Add(script.GetType().AssemblyQualifiedName!);
            }
            entityData.Scripts = new ScriptComponentData { ScriptNames = scriptNames };
        }

        var children = entity.Children.ToList();
        if (children.Count > 0)
        {
            entityData.Children = new List<EntityData>();
            foreach (var child in children)
            {
                entityData.Children.Add(SerializeEntity(child));
            }
        }

        return entityData;
    }

    public void Serialize(string filepath)
    {
        string json = SerializeToString();
        File.WriteAllText(filepath, json);
    }

    public bool DeserializeFromString(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<SceneData>(json, options);

        if (data == null)
        {
            return false;
        }

        foreach (var entityData in data.Entities)
        {
            DeserializeEntity(entityData, null);
        }

        return true;
    }

    private void DeserializeEntity(EntityData entityData, Entity? parent)
    {
        string name = entityData.Tag?.Name ?? "Entity";
        var entity = _scene.Instantiate(name);

        if (parent != null)
        {
            entity.SetParent(parent);
        }

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

        if (entityData.Camera != null)
        {
            var camera = new CameraComponent();
            camera.Primary = entityData.Camera.Primary;
            camera.FixedAspectRatio = entityData.Camera.FixedAspectRatio;
            camera.ZoomLevel = entityData.Camera.ZoomLevel;
            if (entityData.Camera.BackgroundColor != null && entityData.Camera.BackgroundColor.Length == 4)
            {
                camera.BackgroundColor = new System.Numerics.Vector4(
                    entityData.Camera.BackgroundColor[0],
                    entityData.Camera.BackgroundColor[1],
                    entityData.Camera.BackgroundColor[2],
                    entityData.Camera.BackgroundColor[3]
                );
            }
            entity.AddComponent(camera);
        }

        if (entityData.Scripts != null)
        {
            foreach (var scriptName in entityData.Scripts.ScriptNames)
            {
                var type = Type.GetType(scriptName);
                if (type != null && typeof(EntityBehaviour).IsAssignableFrom(type))
                {
                    var scriptInstance = (EntityBehaviour)Activator.CreateInstance(type)!;
                    entity.AddScript(scriptInstance);
                }
            }
        }

        if (entityData.Children != null)
        {
            foreach (var childData in entityData.Children)
            {
                DeserializeEntity(childData, entity);
            }
        }
    }

    public bool Deserialize(string filepath)
    {
        if (!File.Exists(filepath))
        {
            return false;
        }

        string json = File.ReadAllText(filepath);
        return DeserializeFromString(json);
    }
}
