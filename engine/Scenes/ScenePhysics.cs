using System.Numerics;
using Spot.Core;
using Spot.Physics;

namespace Spot.Scenes;

/// <summary>
/// Owns a scene's runtime physics: the 3D and 2D backends and their collision dispatchers. Split out of
/// <see cref="Scene"/> so the backend lifetime, stepping, and raycasts live in one place; the scene's
/// physics <see cref="ISystem"/>s and <c>Raycast</c> API delegate here.
/// </summary>
/// <remarks>
/// Each backend is built lazily from <see cref="PhysicsSettings"/> on the first step, falling back to the
/// legacy AABB solver if the preferred backend fails to initialize so play never dies. Backends are native
/// (Bepu) or managed and must be disposed via <see cref="Teardown"/> when the scene is exited.
/// </remarks>
internal sealed class ScenePhysics
{
    private readonly Scene _scene;
    private IPhysics3D? _physics3D;
    private IPhysics2D? _physics2D;
    private CollisionDispatcher? _collisions3D;
    private CollisionDispatcher? _collisions2D;

    public ScenePhysics(Scene scene) => _scene = scene;

    /// <summary>Runs the 3D physics step and dispatches the contacts it produced as collision events.</summary>
    public void Step3D(float deltaTime)
    {
        IPhysics3D physics = Ensure3D();
        physics.Step(_scene, deltaTime);
        (_collisions3D ??= new CollisionDispatcher()).Dispatch(physics.Contacts);
    }

    /// <summary>Runs the 2D physics step and dispatches the contacts it produced as collision events.</summary>
    public void Step2D(float deltaTime)
    {
        IPhysics2D physics = Ensure2D();
        physics.Step(_scene, deltaTime);
        (_collisions2D ??= new CollisionDispatcher()).Dispatch(physics.Contacts);
    }

    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
    {
        if (_physics3D is null)
        {
            hit = default;
            return false;
        }
        return _physics3D.Raycast(_scene, origin, direction, maxDistance, out hit);
    }

    public bool Raycast2D(Vector2 origin, Vector2 direction, float maxDistance, out RaycastHit2D hit)
    {
        if (_physics2D is null)
        {
            hit = default;
            return false;
        }
        return _physics2D.Raycast(_scene, origin, direction, maxDistance, out hit);
    }

    /// <summary>
    /// Disposes both backends if built. Called when the scene is exited so native simulations and their
    /// buffers are released. Safe to call when no backend exists; a later step rebuilds one on demand.
    /// </summary>
    public void Teardown()
    {
        if (_physics3D is not null)
        {
            try
            {
                _physics3D.Dispose();
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to dispose the 3D physics backend: {0}", ex.Message);
            }
            _physics3D = null;
            _collisions3D?.Reset();
        }

        if (_physics2D is not null)
        {
            try
            {
                _physics2D.Dispose();
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to dispose the 2D physics backend: {0}", ex.Message);
            }
            _physics2D = null;
            _collisions2D?.Reset();
        }
    }

    // Lazily builds the 3D backend from PhysicsSettings.Backend, falling back to the legacy solver.
    private IPhysics3D Ensure3D()
    {
        if (_physics3D is not null)
        {
            return _physics3D;
        }

        if (PhysicsSettings.Backend == Physics3DBackend.Bepu)
        {
            try
            {
                _physics3D = new Physics.Bepu.BepuPhysics3D();
                return _physics3D;
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to initialize the Bepu physics backend ({0}); falling back to the legacy solver.", ex.Message);
            }
        }

        _physics3D = new LegacyPhysics3D();
        return _physics3D;
    }

    // Lazily builds the 2D backend from PhysicsSettings.Backend2D, falling back to the legacy solver.
    private IPhysics2D Ensure2D()
    {
        if (_physics2D is not null)
        {
            return _physics2D;
        }

        if (PhysicsSettings.Backend2D == Physics2DBackend.Aether)
        {
            try
            {
                _physics2D = new Physics.Aether.AetherPhysics2D();
                return _physics2D;
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to initialize the Aether 2D physics backend ({0}); falling back to the legacy solver.", ex.Message);
            }
        }

        _physics2D = new LegacyPhysics2D();
        return _physics2D;
    }
}
