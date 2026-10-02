using Spot.Audio;
using Spot.Rendering;

namespace Spot.Assets;

/// <summary>
/// The engine's asset-reference loading, added to the framework's resource types. A stored reference is either
/// a <c>guid:</c> reference — resolved through the content manifest to its cooked artifact — or a source path,
/// resolved against the project's asset root. Components, serializers and scripts load through these
/// (<c>Texture2D.Load</c>, <c>Font.Load</c>, <c>AudioClip.Load</c>), so they never care whether the project has
/// been cooked. One class per extended type, since each adds a <c>Load(string)</c>.
/// </summary>
public static class TextureAssetLoading
{
    extension(Texture2D)
    {
        /// <summary>
        /// Loads a texture from a stored reference: a <c>guid:</c> reference loads its cooked <c>.spttex</c>, any
        /// other value decodes the source image at that (project-relative or absolute) path.
        /// </summary>
        /// <param name="storedRef">A <c>guid:</c> reference or a source image path.</param>
        /// <returns>The texture.</returns>
        /// <exception cref="FileNotFoundException">The reference resolves to nothing.</exception>
        public static Texture2D Load(string storedRef) =>
            AssetRef.IsGuidRef(storedRef)
                ? Texture2D.FromSpTex(AssetPath.ResolveCooked(storedRef, "texture"))
                : Texture2D.FromFile(AssetPath.Resolve(storedRef));

        /// <summary>
        /// Loads a cooked <c>.spttex</c> texture — raw RGBA decoded at import time — and uploads it verbatim, so no
        /// image decoder runs at runtime.
        /// </summary>
        /// <param name="path">The path to the cooked <c>.spttex</c> file.</param>
        /// <returns>The texture.</returns>
        public static Texture2D FromSpTex(string path)
        {
            SpTexData tex = SpTex.ReadFile(path);
            return new Texture2D(tex.Width, tex.Height, tex.Rgba, tex.PointFilter);
        }
    }
}

/// <summary>
/// Asset-reference loading for <see cref="Font"/> (see <see cref="TextureAssetLoading"/>).
/// </summary>
public static class FontAssetLoading
{
    extension(Font)
    {
        /// <summary>
        /// Loads a font from a stored reference: a <c>guid:</c> reference loads its cooked <c>.sptfont</c>, any
        /// other value loads the source <c>.ttf</c>/<c>.otf</c> at that path.
        /// </summary>
        /// <param name="storedRef">A <c>guid:</c> reference or a source font path.</param>
        /// <returns>The font.</returns>
        /// <exception cref="FileNotFoundException">The reference resolves to nothing.</exception>
        public static Font Load(string storedRef)
        {
            if (!AssetRef.IsGuidRef(storedRef))
            {
                return Font.FromFile(AssetPath.Resolve(storedRef));
            }

            SpFontData font = SpFont.ReadFile(AssetPath.ResolveCooked(storedRef, "font"));
            return new Font(font.Ttf, font.Name);
        }
    }
}

/// <summary>
/// Asset-reference loading for <see cref="AudioClip"/> (see <see cref="TextureAssetLoading"/>).
/// </summary>
public static class AudioClipAssetLoading
{
    extension(AudioClip)
    {
        /// <summary>
        /// Loads a clip from a stored reference: a <c>guid:</c> reference loads its cooked <c>.sptaudio</c>, any
        /// other value decodes the source audio file at that path.
        /// </summary>
        /// <param name="storedRef">A <c>guid:</c> reference or a source audio path.</param>
        /// <returns>The clip.</returns>
        /// <exception cref="FileNotFoundException">The reference resolves to nothing.</exception>
        public static AudioClip Load(string storedRef) =>
            AssetRef.IsGuidRef(storedRef)
                ? AudioClip.FromSpAudio(AssetPath.ResolveCooked(storedRef, "audio"))
                : AudioClip.FromFile(AssetPath.Resolve(storedRef));

        /// <summary>
        /// Loads a cooked <c>.sptaudio</c> clip — PCM decoded at import time — with no audio decoder at runtime.
        /// </summary>
        /// <param name="path">The path to the cooked <c>.sptaudio</c> file.</param>
        /// <returns>The clip.</returns>
        public static AudioClip FromSpAudio(string path)
        {
            SpAudioData data = SpAudio.ReadFile(path);
            return new AudioClip(data.Pcm, data.Channels, data.SampleRate);
        }
    }
}
