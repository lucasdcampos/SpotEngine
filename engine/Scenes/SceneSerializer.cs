using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Spot.Core;
using Spot.Rendering;
using Spot.Physics;
using Spot.Assets;

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
    public MeshRendererData? MeshRenderer { get; set; }
    public ScriptComponentData? Scripts { get; set; }
    public CameraComponentData? Camera { get; set; }
    public PhysicsBody2DData? PhysicsBody2D { get; set; }
    public BoxCollider2DData? BoxCollider2D { get; set; }
    public DirectionalLightData? DirectionalLight { get; set; }
    public List<EntityData>? Children { get; set; }
}

public class DirectionalLightData
{
    public float[] Color { get; set; } = new float[3] { 1, 1, 1 };
    public float Intensity { get; set; } = 1.0f;
    public float AmbientIntensity { get; set; } = 0.3f;
}

public class CameraComponentData
{
    public bool Primary { get; set; }
    public bool FixedAspectRatio { get; set; }
    public float ZoomLevel { get; set; }
    public float[]? BackgroundColor { get; set; }
    public int ProjectionType { get; set; } = 0;
    public float FieldOfView { get; set; } = 45.0f;
}

public class PhysicsBody2DData
{
    public float[] Velocity { get; set; } = new float[2];
    public float GravityScale { get; set; } = 1.0f;
    public bool IsDynamic { get; set; } = true;
}

public class BoxCollider2DData
{
    public float[] Size { get; set; } = new float[2];
    public float[] Offset { get; set; } = new float[2];
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
    public string? TexturePath { get; set; }
}

public class MeshRendererData
{
    public float[] Color { get; set; } = new float[4] { 1, 1, 1, 1 };
    public string? ModelPath { get; set; }
    public string? MaterialPath { get; set; }
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
                Color = new[] { sprite.Color.X, sprite.Color.Y, sprite.Color.Z, sprite.Color.W },
                TexturePath = sprite.TexturePath
            };
        }

        if (entity.HasComponent<MeshRenderer>())
        {
            var meshRenderer = entity.GetComponent<MeshRenderer>();
            entityData.MeshRenderer = new MeshRendererData
            {
                Color = new[] { meshRenderer.Color.X, meshRenderer.Color.Y, meshRenderer.Color.Z, meshRenderer.Color.W },
                ModelPath = meshRenderer.ModelPath,
                MaterialPath = meshRenderer.MaterialPath
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
                BackgroundColor = new[] { camera.BackgroundColor.X, camera.BackgroundColor.Y, camera.BackgroundColor.Z, camera.BackgroundColor.W },
                ProjectionType = (int)camera.ProjectionType,
                FieldOfView = camera.FieldOfView
            };
        }

        if (entity.HasComponent<PhysicsBody2DComponent>())
        {
            var body = entity.GetComponent<PhysicsBody2DComponent>();
            entityData.PhysicsBody2D = new PhysicsBody2DData
            {
                Velocity = new[] { body.Velocity.X, body.Velocity.Y },
                GravityScale = body.GravityScale,
                IsDynamic = body.IsDynamic
            };
        }

        if (entity.HasComponent<BoxCollider2DComponent>())
        {
            var collider = entity.GetComponent<BoxCollider2DComponent>();
            entityData.BoxCollider2D = new BoxCollider2DData
            {
                Size = new[] { collider.Size.X, collider.Size.Y },
                Offset = new[] { collider.Offset.X, collider.Offset.Y }
            };
        }

        if (entity.HasComponent<DirectionalLightComponent>())
        {
            var light = entity.GetComponent<DirectionalLightComponent>();
            entityData.DirectionalLight = new DirectionalLightData
            {
                Color = new[] { light.Color.X, light.Color.Y, light.Color.Z },
                Intensity = light.Intensity,
                AmbientIntensity = light.AmbientIntensity
            };
        }

        if (entity.HasComponent<ScriptComponent>())
        {
            var scriptComp = entity.GetComponent<ScriptComponent>();
            var scriptNames = new List<string>(scriptComp.ClassNames);
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
        try
        {
            string json = SerializeToString();
            File.WriteAllText(filepath, json);
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to save scene '{0}': {1}", filepath, ex.Message);
        }
    }

    public bool DeserializeFromString(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Log.CoreError("Failed to load scene: the file is empty.");
            return false;
        }

        // Some editors save JSON with a leading UTF-8 BOM; strip it so the parser doesn't choke.
        json = json.TrimStart('\uFEFF');

        SceneData? data;
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            data = JsonSerializer.Deserialize<SceneData>(json, options);
        }
        catch (JsonException ex)
        {
            Log.CoreError("Failed to parse scene: {0}", ex.Message);
            return false;
        }

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
            if (!string.IsNullOrEmpty(entityData.Sprite.TexturePath))
            {
                sprite.TexturePath = entityData.Sprite.TexturePath;
                try
                {
                    sprite.Texture = new Texture2D(sprite.TexturePath);
                }
                catch (Exception ex)
                {
                    Log.CoreError("Failed to load texture '{0}': {1}", sprite.TexturePath, ex.Message);
                }
            }
            entity.AddComponent(sprite);
        }

        if (entityData.MeshRenderer != null)
        {
            var meshRenderer = new MeshRenderer();
            meshRenderer.Color = new System.Numerics.Vector4(entityData.MeshRenderer.Color[0], entityData.MeshRenderer.Color[1], entityData.MeshRenderer.Color[2], entityData.MeshRenderer.Color[3]);
            if (!string.IsNullOrEmpty(entityData.MeshRenderer.ModelPath))
            {
                meshRenderer.ModelPath = entityData.MeshRenderer.ModelPath;
                try
                {
                    meshRenderer.Model = Model.Load(meshRenderer.ModelPath);
                }
                catch (Exception ex)
                {
                    Log.CoreError("Failed to load model '{0}': {1}", meshRenderer.ModelPath, ex.Message);
                }
            }
            if (!string.IsNullOrEmpty(entityData.MeshRenderer.MaterialPath))
            {
                meshRenderer.MaterialPath = entityData.MeshRenderer.MaterialPath;
                try
                {
                    meshRenderer.Material = Material.Load(meshRenderer.MaterialPath);
                }
                catch (Exception ex)
                {
                    Log.CoreError("Failed to load material '{0}': {1}", meshRenderer.MaterialPath, ex.Message);
                }
            }
            entity.AddComponent(meshRenderer);
        }

        if (entityData.Camera != null)
        {
            var camera = new CameraComponent();
            camera.Primary = entityData.Camera.Primary;
            camera.FixedAspectRatio = entityData.Camera.FixedAspectRatio;
            camera.ZoomLevel = entityData.Camera.ZoomLevel;
            camera.ProjectionType = (SceneCameraProjection)entityData.Camera.ProjectionType;
            camera.FieldOfView = entityData.Camera.FieldOfView;
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

        if (entityData.PhysicsBody2D != null)
        {
            var body = new PhysicsBody2DComponent();
            body.Velocity = new System.Numerics.Vector2(entityData.PhysicsBody2D.Velocity[0], entityData.PhysicsBody2D.Velocity[1]);
            body.GravityScale = entityData.PhysicsBody2D.GravityScale;
            body.IsDynamic = entityData.PhysicsBody2D.IsDynamic;
            entity.AddComponent(body);
        }

        if (entityData.BoxCollider2D != null)
        {
            var collider = new BoxCollider2DComponent();
            collider.Size = new System.Numerics.Vector2(entityData.BoxCollider2D.Size[0], entityData.BoxCollider2D.Size[1]);
            collider.Offset = new System.Numerics.Vector2(entityData.BoxCollider2D.Offset[0], entityData.BoxCollider2D.Offset[1]);
            entity.AddComponent(collider);
        }

        if (entityData.DirectionalLight != null)
        {
            var light = new DirectionalLightComponent();
            light.Color = new System.Numerics.Vector3(entityData.DirectionalLight.Color[0], entityData.DirectionalLight.Color[1], entityData.DirectionalLight.Color[2]);
            light.Intensity = entityData.DirectionalLight.Intensity;
            light.AmbientIntensity = entityData.DirectionalLight.AmbientIntensity;
            entity.AddComponent(light);
        }

        if (entityData.Scripts != null)
        {
            var scriptComp = new ScriptComponent();
            entity.AddComponent(scriptComp);
            
            foreach (var scriptName in entityData.Scripts.ScriptNames)
            {
                scriptComp.ClassNames.Add(scriptName);
                
                string className = scriptName.EndsWith(".cs") ? scriptName.Substring(0, scriptName.Length - 3) : scriptName;
                
                Type? scriptType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    scriptType = assembly.GetTypes().FirstOrDefault(t => t.Name == className && t.IsSubclassOf(typeof(EntityBehaviour)));
                    if (scriptType != null)
                    {
                        break;
                    }
                }
                
                if (scriptType != null)
                {
                    try
                    {
                        var scriptInstance = (EntityBehaviour)Activator.CreateInstance(scriptType)!;
                        scriptInstance.Entity = entity;
                        scriptComp.Scripts.Add(scriptInstance);
                    }
                    catch (Exception ex)
                    {
                        Log.CoreError("Failed to instantiate script '{0}': {1}", scriptName, ex.Message);
                    }
                }
                else
                {
                    Log.CoreWarn("Failed to load script '{0}'. Type not found.", scriptName);
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

        string json;
        try
        {
            json = File.ReadAllText(filepath);
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to read scene '{0}': {1}", filepath, ex.Message);
            return false;
        }

        return DeserializeFromString(json);
    }
}
