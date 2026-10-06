import os
import re

rename_mapping = {
    "Spot.Engine.Physics.BoxCollider2DComponent": "Spot.Engine.BoxCollider2D",
    "Spot.Engine.Physics.BoxCollider3DComponent": "Spot.Engine.BoxCollider3D",
    "Spot.Engine.Physics.CapsuleCollider3DComponent": "Spot.Engine.CapsuleCollider3D",
    "Spot.Engine.Physics.CharacterController3DComponent": "Spot.Engine.CharacterController3D",
    "Spot.Engine.Physics.CircleCollider2DComponent": "Spot.Engine.CircleCollider2D",
    "Spot.Engine.Physics.Collider2DComponent": "Spot.Engine.Collider2D",
    "Spot.Engine.Physics.Collider3DComponent": "Spot.Engine.Collider3D",
    "Spot.Engine.Physics.PhysicsBody2DComponent": "Spot.Engine.PhysicsBody2D",
    "Spot.Engine.Physics.PhysicsBody3DComponent": "Spot.Engine.PhysicsBody3D",
    "Spot.Engine.Physics.SphereCollider3DComponent": "Spot.Engine.SphereCollider3D",
    "Spot.Engine.AnimatorComponent": "Spot.Engine.Animator",
    "Spot.Engine.AudioListenerComponent": "Spot.Engine.AudioListener",
    "Spot.Engine.AudioSourceComponent": "Spot.Engine.AudioSource",
    "Spot.Engine.CameraComponent": "Spot.Engine.Camera",
    "Spot.Engine.DynamicCloudsComponent": "Spot.Engine.DynamicClouds",
    "Spot.Engine.LabelComponent": "Spot.Engine.Label",
    "Spot.Engine.LightComponent": "Spot.Engine.Light",
    "Spot.Engine.MeshComponent": "Spot.Engine.MeshRenderer",
    "Spot.Engine.ParticleSystemComponent": "Spot.Engine.ParticleSystemRenderer",
    "Spot.Engine.PostProcessingComponent": "Spot.Engine.PostProcessing",
    "Spot.Engine.PrefabComponent": "Spot.Engine.PrefabInstance",
    "Spot.Engine.RelationshipComponent": "Spot.Engine.Relationship",
    "Spot.Engine.SkinnedMeshComponent": "Spot.Engine.SkinnedMeshRenderer",
    "Spot.Engine.SkyboxComponent": "Spot.Engine.Skybox",
    "Spot.Engine.Sprite2DComponent": "Spot.Engine.Sprite2D",
    "Spot.Engine.TextComponent": "Spot.Engine.TextRenderer",
    "Spot.Engine.TransformComponent": "Spot.Engine.Transform",
    "Spot.Engine.UICanvasComponent": "Spot.Engine.UICanvas",
    
    # Catch any components saved with Spot.Engine.Scenes namespace
    "Spot.Engine.Scenes.AnimatorComponent": "Spot.Engine.Animator",
    "Spot.Engine.Scenes.AudioListenerComponent": "Spot.Engine.AudioListener",
    "Spot.Engine.Scenes.AudioSourceComponent": "Spot.Engine.AudioSource",
    "Spot.Engine.Scenes.CameraComponent": "Spot.Engine.Camera",
    "Spot.Engine.Scenes.DynamicCloudsComponent": "Spot.Engine.DynamicClouds",
    "Spot.Engine.Scenes.LabelComponent": "Spot.Engine.Label",
    "Spot.Engine.Scenes.LightComponent": "Spot.Engine.Light",
    "Spot.Engine.Scenes.MeshComponent": "Spot.Engine.MeshRenderer",
    "Spot.Engine.Scenes.ParticleSystemComponent": "Spot.Engine.ParticleSystemRenderer",
    "Spot.Engine.Scenes.PostProcessingComponent": "Spot.Engine.PostProcessing",
    "Spot.Engine.Scenes.PrefabComponent": "Spot.Engine.PrefabInstance",
    "Spot.Engine.Scenes.RelationshipComponent": "Spot.Engine.Relationship",
    "Spot.Engine.Scenes.SkinnedMeshComponent": "Spot.Engine.SkinnedMeshRenderer",
    "Spot.Engine.Scenes.SkyboxComponent": "Spot.Engine.Skybox",
    "Spot.Engine.Scenes.Sprite2DComponent": "Spot.Engine.Sprite2D",
    "Spot.Engine.Scenes.TextComponent": "Spot.Engine.TextRenderer",
    "Spot.Engine.Scenes.TransformComponent": "Spot.Engine.Transform",
    "Spot.Engine.Scenes.UICanvasComponent": "Spot.Engine.UICanvas",
}

def replace_in_file(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
            
        original = content
        for old, new in rename_mapping.items():
            # Standard string replacement is safer here because we are targeting fully qualified names 
            # in JSON strings. e.g. "Spot.Engine.TransformComponent" -> "Spot.Engine.Transform"
            content = content.replace(old, new)
            
        if original != content:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(content)
            print(f"Updated {filepath}")
    except Exception as e:
        print(f"Error reading {filepath}: {e}")

for root, _, files in os.walk("."):
    for file in files:
        if file.endswith((".sptscene", ".sptprefab", ".sptui")):
            filepath = os.path.join(root, file)
            # ignore obj, bin, .git
            if "obj\\" in filepath or "bin\\" in filepath or ".git\\" in filepath:
                continue
            replace_in_file(filepath)

print("Done fixing assets!")
