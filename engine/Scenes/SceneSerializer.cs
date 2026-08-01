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
    public LightData? Light { get; set; }
    public DynamicCloudsData? DynamicClouds { get; set; }
    public PhysicsBody3DData? PhysicsBody3D { get; set; }
    public BoxCollider3DData? BoxCollider3D { get; set; }
    public PostProcessingData? PostProcessing { get; set; }
    public List<EntityData>? Children { get; set; }
}

public abstract class ComponentData
{
    public bool Enabled { get; set; } = true;
}

public class DirectionalLightData : ComponentData
{
    public float[] Color { get; set; } = new float[3] { 1, 1, 1 };
    public float Intensity { get; set; } = 1.0f;
    public float AmbientIntensity { get; set; } = 0.3f;
}

public class LightData : ComponentData
{
    public int Type { get; set; } = 0;
    public float[] Color { get; set; } = new float[3] { 1, 1, 1 };
    public float Intensity { get; set; } = 1.0f;
    public float AmbientIntensity { get; set; } = 0.3f;
    public bool CastShadows { get; set; } = true;
    public float Range { get; set; } = 10.0f;
}

public class DynamicCloudsData : ComponentData
{
    public float[] ColorTop { get; set; } = new float[3] { 1, 1, 1 };
    public float[] ColorBottom { get; set; } = new float[3] { 0.8f, 0.85f, 0.9f };
    public float Speed { get; set; } = 1.0f;
    public float Density { get; set; } = 0.57f;
    public float Height { get; set; } = 0.3f;
    public float Opacity { get; set; } = 0.6f;
    public float Volume { get; set; } = 1.35f;
}

public class CameraComponentData : ComponentData
{
    public bool Primary { get; set; }
    public bool FixedAspectRatio { get; set; }
    public float ZoomLevel { get; set; }
    public float[]? BackgroundColor { get; set; }
    public int ProjectionType { get; set; } = 0;
    public float FieldOfView { get; set; } = 45.0f;
}

public class PhysicsBody2DData : ComponentData
{
    public float[] Velocity { get; set; } = new float[2];
    public float GravityScale { get; set; } = 1.0f;
    public bool IsDynamic { get; set; } = true;
}

public class BoxCollider2DData : ComponentData
{
    public float[] Size { get; set; } = new float[2];
    public float[] Offset { get; set; } = new float[2];
}

public class PhysicsBody3DData : ComponentData
{
    public float[] Velocity { get; set; } = new float[3];
    public float GravityScale { get; set; } = 1.0f;
    public bool IsDynamic { get; set; } = true;
}

public class BoxCollider3DData : ComponentData
{
    public float[] Size { get; set; } = new float[3];
    public float[] Offset { get; set; } = new float[3];
}

public class ScriptComponentData : ComponentData
{
    public List<string> ScriptNames { get; set; } = new();
}

public class PostProcessingData : ComponentData
{
    public float Exposure { get; set; } = 1.0f;
    public float Gamma { get; set; } = 2.2f;
    public bool EnableVignette { get; set; } = true;
    public float VignetteIntensity { get; set; } = 0.25f;
    public bool EnableBloom { get; set; } = true;
    public float BloomThreshold { get; set; } = 1.0f;
    public float BloomIntensity { get; set; } = 1.0f;
}

public class TransformData : ComponentData
{
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = new float[3];
    public float[] Scale { get; set; } = new float[3] { 1, 1, 1 };
}

public class Sprite2DData : ComponentData
{
    public float[] Color { get; set; } = new float[4] { 1, 1, 1, 1 };
    public string? TexturePath { get; set; }
}

public class MeshRendererData : ComponentData
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
                Enabled = transform.Enabled,
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
                Enabled = sprite.Enabled,
                Color = new[] { sprite.Color.X, sprite.Color.Y, sprite.Color.Z, sprite.Color.W },
                TexturePath = sprite.TexturePath != null ? Assets.AssetPath.MakeRelative(sprite.TexturePath) : null
            };
        }

        if (entity.HasComponent<MeshRenderer>())
        {
            var meshRenderer = entity.GetComponent<MeshRenderer>();
            entityData.MeshRenderer = new MeshRendererData
            {
                Enabled = meshRenderer.Enabled,
                Color = new[] { meshRenderer.Color.X, meshRenderer.Color.Y, meshRenderer.Color.Z, meshRenderer.Color.W },
                ModelPath = meshRenderer.ModelPath != null ? Assets.AssetPath.MakeRelative(meshRenderer.ModelPath) : null,
                MaterialPath = meshRenderer.MaterialPath != null ? Assets.AssetPath.MakeRelative(meshRenderer.MaterialPath) : null
            };
        }

        if (entity.HasComponent<CameraComponent>())
        {
            var camera = entity.GetComponent<CameraComponent>();
            entityData.Camera = new CameraComponentData
            {
                Enabled = camera.Enabled,
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
                Enabled = body.Enabled,
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
                Enabled = collider.Enabled,
                Size = new[] { collider.Size.X, collider.Size.Y },
                Offset = new[] { collider.Offset.X, collider.Offset.Y }
            };
        }

        if (entity.HasComponent<PhysicsBody3DComponent>())
        {
            var body = entity.GetComponent<PhysicsBody3DComponent>();
            entityData.PhysicsBody3D = new PhysicsBody3DData
            {
                Enabled = body.Enabled,
                Velocity = new[] { body.Velocity.X, body.Velocity.Y, body.Velocity.Z },
                GravityScale = body.GravityScale,
                IsDynamic = body.IsDynamic
            };
        }

        if (entity.HasComponent<BoxCollider3DComponent>())
        {
            var collider = entity.GetComponent<BoxCollider3DComponent>();
            entityData.BoxCollider3D = new BoxCollider3DData
            {
                Enabled = collider.Enabled,
                Size = new[] { collider.Size.X, collider.Size.Y, collider.Size.Z },
                Offset = new[] { collider.Offset.X, collider.Offset.Y, collider.Offset.Z }
            };
        }

        if (entity.HasComponent<LightComponent>())
        {
            var light = entity.GetComponent<LightComponent>();
            entityData.Light = new LightData
            {
                Enabled = light.Enabled,
                Type = (int)light.Type,
                Color = new[] { light.Color.X, light.Color.Y, light.Color.Z },
                Intensity = light.Intensity,
                AmbientIntensity = light.AmbientIntensity,
                CastShadows = light.CastShadows,
                Range = light.Range
            };
        }

        if (entity.HasComponent<DynamicCloudsComponent>())
        {
            var clouds = entity.GetComponent<DynamicCloudsComponent>();
            entityData.DynamicClouds = new DynamicCloudsData
            {
                Enabled = clouds.Enabled,
                ColorTop = new[] { clouds.ColorTop.X, clouds.ColorTop.Y, clouds.ColorTop.Z },
                ColorBottom = new[] { clouds.ColorBottom.X, clouds.ColorBottom.Y, clouds.ColorBottom.Z },
                Speed = clouds.Speed,
                Density = clouds.Density,
                Height = clouds.Height,
                Opacity = clouds.Opacity,
                Volume = clouds.Volume
            };
        }

        if (entity.HasComponent<ScriptComponent>())
        {
            var scriptComp = entity.GetComponent<ScriptComponent>();
            var scriptNames = new List<string>(scriptComp.ClassNames);
            entityData.Scripts = new ScriptComponentData { Enabled = scriptComp.Enabled,
                ScriptNames = scriptNames };
        }

        if (entity.HasComponent<PostProcessingComponent>())
        {
            var pp = entity.GetComponent<PostProcessingComponent>();
            entityData.PostProcessing = new PostProcessingData
            {
                Enabled = pp.Enabled,
                Exposure = pp.Exposure,
                Gamma = pp.Gamma,
                EnableVignette = pp.EnableVignette,
                VignetteIntensity = pp.VignetteIntensity,
                EnableBloom = pp.EnableBloom,
                BloomThreshold = pp.BloomThreshold,
                BloomIntensity = pp.BloomIntensity
            };
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

        if (entityData.Tag != null) { entity.Enabled = entityData.Tag.Enabled; }

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
            sprite.Enabled = entityData.Sprite.Enabled;
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
            meshRenderer.Enabled = entityData.MeshRenderer.Enabled;
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
            camera.Enabled = entityData.Camera.Enabled;
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
            body.Enabled = entityData.PhysicsBody2D.Enabled;
            body.Velocity = new System.Numerics.Vector2(entityData.PhysicsBody2D.Velocity[0], entityData.PhysicsBody2D.Velocity[1]);
            body.GravityScale = entityData.PhysicsBody2D.GravityScale;
            body.IsDynamic = entityData.PhysicsBody2D.IsDynamic;
            entity.AddComponent(body);
        }

        if (entityData.BoxCollider2D != null)
        {
            var collider = new BoxCollider2DComponent();
            collider.Enabled = entityData.BoxCollider2D.Enabled;
            collider.Size = new System.Numerics.Vector2(entityData.BoxCollider2D.Size[0], entityData.BoxCollider2D.Size[1]);
            collider.Offset = new System.Numerics.Vector2(entityData.BoxCollider2D.Offset[0], entityData.BoxCollider2D.Offset[1]);
            entity.AddComponent(collider);
        }

        if (entityData.PhysicsBody3D != null)
        {
            var body = new PhysicsBody3DComponent();
            body.Enabled = entityData.PhysicsBody3D.Enabled;
            body.Velocity = new System.Numerics.Vector3(entityData.PhysicsBody3D.Velocity[0], entityData.PhysicsBody3D.Velocity[1], entityData.PhysicsBody3D.Velocity[2]);
            body.GravityScale = entityData.PhysicsBody3D.GravityScale;
            body.IsDynamic = entityData.PhysicsBody3D.IsDynamic;
            entity.AddComponent(body);
        }

        if (entityData.BoxCollider3D != null)
        {
            var collider = new BoxCollider3DComponent();
            collider.Enabled = entityData.BoxCollider3D.Enabled;
            collider.Size = new System.Numerics.Vector3(entityData.BoxCollider3D.Size[0], entityData.BoxCollider3D.Size[1], entityData.BoxCollider3D.Size[2]);
            collider.Offset = new System.Numerics.Vector3(entityData.BoxCollider3D.Offset[0], entityData.BoxCollider3D.Offset[1], entityData.BoxCollider3D.Offset[2]);
            entity.AddComponent(collider);
        }

        if (entityData.Light != null)
        {
            var light = new LightComponent();
            light.Enabled = entityData.Light.Enabled;
            light.Type = (LightType)entityData.Light.Type;
            light.Color = new System.Numerics.Vector3(entityData.Light.Color[0], entityData.Light.Color[1], entityData.Light.Color[2]);
            light.Intensity = entityData.Light.Intensity;
            light.AmbientIntensity = entityData.Light.AmbientIntensity;
            light.CastShadows = entityData.Light.CastShadows;
            light.Range = entityData.Light.Range;
            entity.AddComponent(light);
        }
        else if (entityData.DirectionalLight != null)
        {
            var light = new LightComponent { Type = LightType.Directional };
            light.Enabled = entityData.DirectionalLight.Enabled;
            light.Color = new System.Numerics.Vector3(entityData.DirectionalLight.Color[0], entityData.DirectionalLight.Color[1], entityData.DirectionalLight.Color[2]);
            light.Intensity = entityData.DirectionalLight.Intensity;
            light.AmbientIntensity = entityData.DirectionalLight.AmbientIntensity;
            entity.AddComponent(light);
        }

        if (entityData.DynamicClouds != null)
        {
            var clouds = new DynamicCloudsComponent();
            clouds.Enabled = entityData.DynamicClouds.Enabled;
            clouds.ColorTop = new System.Numerics.Vector3(entityData.DynamicClouds.ColorTop[0], entityData.DynamicClouds.ColorTop[1], entityData.DynamicClouds.ColorTop[2]);
            clouds.ColorBottom = new System.Numerics.Vector3(entityData.DynamicClouds.ColorBottom[0], entityData.DynamicClouds.ColorBottom[1], entityData.DynamicClouds.ColorBottom[2]);
            clouds.Speed = entityData.DynamicClouds.Speed;
            clouds.Density = entityData.DynamicClouds.Density;
            clouds.Height = entityData.DynamicClouds.Height;
            clouds.Opacity = entityData.DynamicClouds.Opacity;
            clouds.Volume = entityData.DynamicClouds.Volume;
            entity.AddComponent(clouds);
        }

        if (entityData.Scripts != null)
        {
            var scriptComp = new ScriptComponent();
            scriptComp.Enabled = entityData.Scripts.Enabled;
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

        if (entityData.PostProcessing != null)
        {
            var pp = new PostProcessingComponent();
            pp.Enabled = entityData.PostProcessing.Enabled;
            pp.Exposure = entityData.PostProcessing.Exposure;
            pp.Gamma = entityData.PostProcessing.Gamma;
            pp.EnableVignette = entityData.PostProcessing.EnableVignette;
            pp.VignetteIntensity = entityData.PostProcessing.VignetteIntensity;
            pp.EnableBloom = entityData.PostProcessing.EnableBloom;
            pp.BloomThreshold = entityData.PostProcessing.BloomThreshold;
            pp.BloomIntensity = entityData.PostProcessing.BloomIntensity;
            entity.AddComponent(pp);
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
