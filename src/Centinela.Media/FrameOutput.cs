namespace Centinela.Media;

/// <summary>
/// The layout decoding sessions publish: BGRA until the app knows the graphics card paints NV12. One setting for the
/// whole app, since every view of a shared session paints the same way; sessions follow a change from their next frame.
/// </summary>
public static class FrameOutput
{
    static volatile FrameLayout _layout = FrameLayout.Bgra;

    public static FrameLayout Layout
    {
        get => _layout;
        set => _layout = value;
    }
}
