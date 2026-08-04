using System;
using System.Numerics;
using Spot.Core;
using Spot.Scenes;
using Spot.Physics;
using Spot.Rendering;

namespace Spot.Game;

public class ItemGrabber : EntityBehaviour
{
    private Entity? _heldItem = null;
    private float _originalGravityScale = 1.0f;
    
    public float GrabDistance = 3.0f;
    public float HoldDistance = 2.0f;
    public float SpringForce = 15.0f;

    public override void OnUpdate(float deltaTime)
    {
        if (Input.GetMouseButtonDown(MouseButton.Left))
        {
            TryGrabItem();
        }
        else if (Input.GetMouseButtonUp(MouseButton.Left) && _heldItem != null)
        {
            DropItem();
        }

        UpdateHeldItem(deltaTime);
    }

    private void TryGrabItem()
    {
        if (_heldItem != null) return;

        var cam = GetCamera();
        if (cam == null) return;

        var camT = cam.Value.GetComponent<TransformComponent>();
        Vector3 camPos = camT.WorldPosition;
        Vector3 camFwd = GetForward(camT);

        Entity? bestEntity = null;
        float bestDot = 0.95f; // Precisa estar olhando relativamente perto do centro
        float bestDist = GrabDistance;

        foreach (var e in Scene.View<PhysicsBody3DComponent, TransformComponent>())
        {
            if (e == Entity) continue; // Não pegar a si mesmo
            if (!IsInteractable(e)) continue; // Apenas objetos marcados como interagíveis

            var body = e.GetComponent<PhysicsBody3DComponent>();
            var t = e.GetComponent<TransformComponent>();

            if (!body.IsDynamic || !body.Enabled) continue;

            Vector3 dir = t.WorldPosition - camPos;
            float dist = dir.Length();
            if (dist < 0.1f || dist > GrabDistance) continue;

            dir /= dist;
            float dot = Vector3.Dot(camFwd, dir);

            if (dot > bestDot && dist < bestDist)
            {
                bestDot = dot;
                bestDist = dist;
                bestEntity = e;
            }
        }

        if (bestEntity.HasValue)
        {
            _heldItem = bestEntity;
            var body = _heldItem.Value.GetComponent<PhysicsBody3DComponent>();
            _originalGravityScale = body.GravityScale;
            
            // "Desativamos" a gravidade e usamos velocidade para mover o item até a posição de hold.
            // Mantemos IsDynamic = true para que o sistema de física resolva colisões com o chão/paredes,
            // garantindo que o objeto não atravesse paredes.
            body.GravityScale = 0f;
        }
    }

    private void DropItem()
    {
        if (_heldItem != null && _heldItem.Value.IsActiveInHierarchy())
        {
            var body = _heldItem.Value.GetComponent<PhysicsBody3DComponent>();
            if (body != null)
            {
                // Reativa a gravidade normal para o objeto cair
                body.GravityScale = _originalGravityScale;
            }
        }
        _heldItem = null;
    }

    private void UpdateHeldItem(float deltaTime)
    {
        if (_heldItem == null) return;

        if (!_heldItem.Value.IsActiveInHierarchy())
        {
            _heldItem = null;
            return;
        }

        var cam = GetCamera();
        if (cam == null) return;

        var camT = cam.Value.GetComponent<TransformComponent>();
        Vector3 holdPos = camT.WorldPosition + GetForward(camT) * HoldDistance;

        var itemT = _heldItem.Value.GetComponent<TransformComponent>();
        var body = _heldItem.Value.GetComponent<PhysicsBody3DComponent>();

        if (body != null)
        {
            // Puxa o objeto em direção à posição alvo através de velocidade.
            // Isso previne que ele passe por paredes, pois a física do motor irá barrá-lo.
            Vector3 diff = holdPos - itemT.WorldPosition;
            body.Velocity = diff * SpringForce;
        }
    }

    private Entity? GetCamera()
    {
        if (Entity.TryGetComponent(out RelationshipComponent? rel))
        {
            foreach (var child in rel.Children)
            {
                if (child.HasComponent<CameraComponent>()) return child;
            }
        }

        foreach (var cam in Scene.View<CameraComponent>())
        {
            if (cam.GetComponent<CameraComponent>().Primary) return cam;
        }

        return null;
    }

    private Vector3 GetForward(TransformComponent t)
    {
        // No Matrix4x4, a coluna 3 (Z) é o forward (negativo na convenção direita)
        var m = t.Matrix;
        return Vector3.Normalize(new Vector3(-m.M31, -m.M32, -m.M33));
    }

    private bool IsInteractable(Entity e)
    {
        if (e.TryGetComponent(out ScriptComponent? scripts))
        {
            // Verificamos se há algum script que contenha InteractableItem no nome da classe
            foreach (var className in scripts.ClassNames)
            {
                if (className.Contains("InteractableItem")) return true;
            }
        }
        return false;
    }
}
