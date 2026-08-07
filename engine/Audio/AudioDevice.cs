using Silk.NET.OpenAL;
using Spot.Core;

namespace Spot.Audio;

/// <summary>
/// Owns the process-wide OpenAL device and context. Opening is wrapped end to end so a machine with no
/// audio device — or a missing native OpenAL runtime — leaves the engine running silently instead of
/// crashing, honoring the engine's never-crash directive. When <see cref="Available"/> is false every
/// audio call downstream becomes a no-op.
/// </summary>
internal static unsafe class AudioDevice
{
    private static ALContext? s_alc;
    private static Device* s_device;
    private static Context* s_context;

    /// <summary>Gets the OpenAL core API, or <c>null</c> when audio is unavailable.</summary>
    public static AL? Al { get; private set; }

    /// <summary>Gets whether a usable OpenAL device and context are current.</summary>
    public static bool Available { get; private set; }

    /// <summary>Opens the default device and makes a context current. Safe to call more than once.</summary>
    public static void Open()
    {
        if (Available)
        {
            return;
        }

        try
        {
            s_alc = ALContext.GetApi();
            AL al = AL.GetApi();

            s_device = s_alc.OpenDevice("");
            if (s_device == null)
            {
                Log.CoreWarn("No OpenAL device could be opened; audio will run muted.");
                Cleanup();
                return;
            }

            s_context = s_alc.CreateContext(s_device, null);
            if (s_context == null || !s_alc.MakeContextCurrent(s_context))
            {
                Log.CoreWarn("Failed to create an OpenAL context; audio will run muted.");
                Cleanup();
                return;
            }

            al.GetError(); // clear any startup error latch
            Al = al;
            Available = true;
            Log.CoreInfo("OpenAL audio device initialized.");
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Audio initialization failed ({0}); audio will run muted.", ex.Message);
            Cleanup();
        }
    }

    /// <summary>Tears down the context and device. Safe to call when audio never opened.</summary>
    public static void Close()
    {
        try
        {
            if (s_alc != null)
            {
                s_alc.MakeContextCurrent(null);
                if (s_context != null)
                {
                    s_alc.DestroyContext(s_context);
                }

                if (s_device != null)
                {
                    s_alc.CloseDevice(s_device);
                }
            }
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Error while closing the audio device: {0}", ex.Message);
        }

        Cleanup();
    }

    private static void Cleanup()
    {
        s_device = null;
        s_context = null;
        Al?.Dispose();
        Al = null;
        s_alc?.Dispose();
        s_alc = null;
        Available = false;
    }
}
