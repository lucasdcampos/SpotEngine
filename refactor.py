import os
import re

components_to_rename = {
    r"engine\Physics\BoxCollider2DComponent.cs": "BoxCollider2D",
    r"engine\Physics\BoxCollider3DComponent.cs": "BoxCollider3D",
    r"engine\Physics\CapsuleCollider3DComponent.cs": "CapsuleCollider3D",
    r"engine\Physics\CharacterController3DComponent.cs": "CharacterController3D",
    r"engine\Physics\CircleCollider2DComponent.cs": "CircleCollider2D",
    r"engine\Physics\Collider2DComponent.cs": "Collider2D",
    r"engine\Physics\Collider3DComponent.cs": "Collider3D",
    r"engine\Physics\PhysicsBody2DComponent.cs": "PhysicsBody2D",
    r"engine\Physics\PhysicsBody3DComponent.cs": "PhysicsBody3D",
    r"engine\Physics\SphereCollider3DComponent.cs": "SphereCollider3D",
    
    r"engine\Scenes\Component.cs": "Component",
    r"engine\Scenes\Components\AnimatorComponent.cs": "Animator",
    r"engine\Scenes\Components\AudioListenerComponent.cs": "AudioListener",
    r"engine\Scenes\Components\AudioSourceComponent.cs": "AudioSource",
    r"engine\Scenes\Components\CameraComponent.cs": "Camera",
    r"engine\Scenes\Components\DynamicCloudsComponent.cs": "DynamicClouds",
    r"engine\Scenes\Components\LabelComponent.cs": "Label",
    r"engine\Scenes\Components\LightComponent.cs": "Light",
    r"engine\Scenes\Components\MeshComponent.cs": "MeshRenderer",
    r"engine\Scenes\Components\ParticleSystemComponent.cs": "ParticleSystemRenderer",
    r"engine\Scenes\Components\PostProcessingComponent.cs": "PostProcessing",
    r"engine\Scenes\Components\PrefabComponent.cs": "Prefab",
    r"engine\Scenes\Components\RelationshipComponent.cs": "Relationship",
    r"engine\Scenes\Components\SkinnedMeshComponent.cs": "SkinnedMeshRenderer",
    r"engine\Scenes\Components\SkyboxComponent.cs": "Skybox",
    r"engine\Scenes\Components\Sprite2DComponent.cs": "Sprite2D",
    r"engine\Scenes\Components\TextComponent.cs": "TextRenderer",
    r"engine\Scenes\Components\TransformComponent.cs": "Transform",
    r"engine\Scenes\Components\UICanvasComponent.cs": "UICanvas",
    r"engine\Scenes\Components\MissingComponents.cs": "MissingComponents"
}

os.makedirs(r"engine\Components", exist_ok=True)

# 1. Move and modify the files
for old_path, new_name in components_to_rename.items():
    if not os.path.exists(old_path):
        print(f"Skipping {old_path} as it doesn't exist.")
        continue
    
    new_path = os.path.join(r"engine\Components", new_name + ".cs")
    print(f"Moving {old_path} -> {new_path}")
    
    with open(old_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Change namespace to Spot.Engine
    content = re.sub(r'namespace\s+Spot\.Engine(\.\w+)*\s*;', 'namespace Spot.Engine;', content)
    
    # Change class names (only the exact matches)
    old_class_name = os.path.basename(old_path).replace(".cs", "")
    if new_name != old_class_name:
        # Match "class OldClassName" or "interface OldClassName" etc.
        content = re.sub(r'\b' + old_class_name + r'\b', new_name, content)
        
    with open(new_path, 'w', encoding='utf-8') as f:
        f.write(content)
        
    os.remove(old_path)

# 2. String replacement across ALL .cs files
rename_mapping = {
    "BoxCollider2DComponent": "BoxCollider2D",
    "BoxCollider3DComponent": "BoxCollider3D",
    "CapsuleCollider3DComponent": "CapsuleCollider3D",
    "CharacterController3DComponent": "CharacterController3D",
    "CircleCollider2DComponent": "CircleCollider2D",
    "Collider2DComponent": "Collider2D",
    "Collider3DComponent": "Collider3D",
    "PhysicsBody2DComponent": "PhysicsBody2D",
    "PhysicsBody3DComponent": "PhysicsBody3D",
    "SphereCollider3DComponent": "SphereCollider3D",
    "AnimatorComponent": "Animator",
    "AudioListenerComponent": "AudioListener",
    "AudioSourceComponent": "AudioSource",
    "CameraComponent": "Camera",
    "DynamicCloudsComponent": "DynamicClouds",
    "LabelComponent": "Label",
    "LightComponent": "Light",
    "MeshComponent": "MeshRenderer",
    "ParticleSystemComponent": "ParticleSystemRenderer",
    "PostProcessingComponent": "PostProcessing",
    "PrefabComponent": "Prefab",
    "RelationshipComponent": "Relationship",
    "SkinnedMeshComponent": "SkinnedMeshRenderer",
    "SkyboxComponent": "Skybox",
    "Sprite2DComponent": "Sprite2D",
    "TextComponent": "TextRenderer",
    "TransformComponent": "Transform",
    "UICanvasComponent": "UICanvas"
}

def replace_in_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
        
    original = content
    for old, new in rename_mapping.items():
        content = re.sub(r'\b' + old + r'\b', new, content)
        
    if original != content:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Updated {filepath}")

for root, _, files in os.walk("."):
    for file in files:
        if file.endswith(".cs") or file.endswith(".sptproj") or file.endswith(".csproj") or file.endswith(".json") or file.endswith(".md"):
            filepath = os.path.join(root, file)
            # ignore obj, bin, .git
            if "obj\\" in filepath or "bin\\" in filepath or ".git\\" in filepath:
                continue
            replace_in_file(filepath)

print("Done!")
