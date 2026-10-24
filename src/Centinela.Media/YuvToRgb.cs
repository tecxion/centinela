namespace Centinela.Media;

/// <summary>
/// The YUV→RGB conversion as a 4×4 matrix for a shader: (Y, U, V, 1) with samples normalised to 0–1 gives (R, G, B, 1).
/// Column-major, as OpenGL takes it. It matches swscale (tested), so both ways of painting show the same colours.
/// </summary>
public static class YuvToRgb
{
    public static float[] Matrix(YuvMatrix matrix, bool fullRange)
    {
        var (kr, kb) = matrix switch
        {
            YuvMatrix.Bt709 => (0.2126, 0.0722),
            YuvMatrix.Bt2020 => (0.2627, 0.0593),
            _ => (0.299, 0.114),
        };
        var kg = 1 - kr - kb;
        // Limited range: Y spans 16–235 and U/V 16–240 (of 255); full range uses all of 0–255.
        double ys = fullRange ? 1 : 255.0 / 219, yo = fullRange ? 0 : 16.0 / 255;
        double cs = fullRange ? 1 : 255.0 / 224, co = 128.0 / 255;
        // R = Y' + 2(1−kr)·V'   G = Y' − 2kb(1−kb)/kg·U' − 2kr(1−kr)/kg·V'   B = Y' + 2(1−kb)·U'
        double rv = 2 * (1 - kr), gu = -2 * kb * (1 - kb) / kg, gv = -2 * kr * (1 - kr) / kg, bu = 2 * (1 - kb);
        double[,] rows =
        {
            { ys, 0, rv * cs, -ys * yo - rv * cs * co },
            { ys, gu * cs, gv * cs, -ys * yo - (gu + gv) * cs * co },
            { ys, bu * cs, 0, -ys * yo - bu * cs * co },
            { 0, 0, 0, 1 },
        };
        var result = new float[16];
        for (var column = 0; column < 4; column++)
            for (var row = 0; row < 4; row++)
                result[column * 4 + row] = (float)rows[row, column];
        return result;
    }
}
