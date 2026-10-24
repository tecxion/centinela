using Centinela.Media;

namespace Centinela.Desktop.Video;

/// <summary>
/// Whether the graphics card paints the video (OpenGL, NV12 frames) or the processor does (BGRA bitmaps), for the
/// whole app. Undecided at start: sessions publish BGRA until a video view's OpenGL works, then NV12. Any failure
/// switches it off for the rest of the run. CENTINELA_CPU_VIDEO=1 keeps it off (to compare, or to get past a bad
/// driver). UI thread only.
/// </summary>
static class GpuVideo
{
    public enum Mode { Undecided, On, Off }

    static readonly string[] SoftwareRenderers = ["llvmpipe", "softpipe", "swiftshader", "software", "basic render"];

    public static Mode State { get; private set; } =
        Environment.GetEnvironmentVariable("CENTINELA_CPU_VIDEO") == "1" ? Mode.Off : Mode.Undecided;

    /// <summary>Raised once, when painting goes back to the processor: views drop their OpenGL child.</summary>
    public static event Action? TurnedOff;

    /// <summary>A view's OpenGL works: sessions publish NV12 from their next frame.</summary>
    public static void TurnOn()
    {
        if (State != Mode.Undecided) return;
        State = Mode.On;
        FrameOutput.Layout = FrameLayout.Nv12;
    }

    public static void TurnOff()
    {
        if (State == Mode.Off) return;
        State = Mode.Off;
        FrameOutput.Layout = FrameLayout.Bgra;
        TurnedOff?.Invoke();
    }

    /// <summary>OpenGL drawn by the processor (Mesa in a VM, Windows' basic driver) is slower than the bitmap path.</summary>
    public static bool IsSoftwareRenderer(string? renderer) =>
        renderer is null || SoftwareRenderers.Any(name => renderer.Contains(name, StringComparison.OrdinalIgnoreCase));
}
