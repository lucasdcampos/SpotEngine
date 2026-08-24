namespace Spot.Rendering;

/// <summary>How particle pixels combine with what's already on screen.</summary>
/// <remarks>
/// Lives apart from the desktop <see cref="ParticleRenderer"/> because it is a plain rendering enum carried
/// by the neutral <c>ParticleSystemComponent</c>, so it must compile for the browser target too.
/// </remarks>
public enum ParticleBlend
{
    /// <summary>Standard transparency: <c>src.a</c> over the background. Good for smoke, dust, soft sprites.</summary>
    Alpha,

    /// <summary>Additive: pixels only ever brighten the background. Good for fire, sparks, magic, glow.</summary>
    Additive,
}
