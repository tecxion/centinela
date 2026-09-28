namespace Centinela.Core;

public readonly record struct MotionSample(bool Motion, double ChangedFraction);

/// <summary>Decides from successive small gray frames whether something moves. Implementations are per camera, not thread-safe.</summary>
public interface IMotionDetector
{
    MotionSample Analyze(GrayFrame frame);
    void Reset();
}

/// <summary>
/// Frame difference against a slowly adapting background: a pixel changed if it differs by more than 25;
/// motion when enough pixels changed for N frames in a row. A sudden change of most of the image (lights,
/// reconnection) resets the background instead of reporting motion.
/// </summary>
public sealed class FrameDiffDetector(MotionSensitivity sensitivity) : IMotionDetector
{
    const float Alpha = 0.05f;
    const int PixelThreshold = 25;
    const double GlobalChange = 0.60;

    readonly (double Fraction, int Frames) _trigger = sensitivity switch
    {
        MotionSensitivity.Low => (0.03, 3),
        MotionSensitivity.High => (0.007, 2),
        _ => (0.015, 2),
    };
    float[]? _background;
    int _consecutive;

    public MotionSample Analyze(GrayFrame frame)
    {
        var pixels = frame.Pixels;
        if (_background is null || _background.Length != pixels.Length)
        {
            Initialize(pixels);
            return new MotionSample(false, 0);
        }
        var changed = 0;
        for (var i = 0; i < pixels.Length; i++)
            if (Math.Abs(pixels[i] - _background[i]) > PixelThreshold) changed++;
        var fraction = changed / (double)pixels.Length;
        if (fraction > GlobalChange)
        {
            Initialize(pixels);
            return new MotionSample(false, fraction);
        }
        for (var i = 0; i < pixels.Length; i++) _background[i] += Alpha * (pixels[i] - _background[i]);
        _consecutive = fraction >= _trigger.Fraction ? _consecutive + 1 : 0;
        return new MotionSample(_consecutive >= _trigger.Frames, fraction);
    }

    public void Reset()
    {
        _background = null;
        _consecutive = 0;
    }

    void Initialize(byte[] pixels)
    {
        _background ??= new float[pixels.Length];
        if (_background.Length != pixels.Length) _background = new float[pixels.Length];
        for (var i = 0; i < pixels.Length; i++) _background[i] = pixels[i];
        _consecutive = 0;
    }
}
