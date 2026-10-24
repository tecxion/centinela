using Avalonia.OpenGL;

namespace Centinela.Desktop.Video;

/// <summary>The shaders that paint video, and the GLSL header each kind of OpenGL needs.</summary>
static class GlShaders
{
    /// <summary>
    /// The first lines of every shader for this context, or null when it is too old: one- and two-channel textures
    /// (R8, RG8) need OpenGL 3.2 or OpenGL ES 3.0 (Windows draws through ANGLE, which is ES).
    /// </summary>
    public static string? Header(GlProfileType type, int major, int minor) => type switch
    {
        GlProfileType.OpenGLES when major >= 3 => "#version 300 es\nprecision highp float;\nprecision highp sampler2D;\n",
        GlProfileType.OpenGL when major > 3 || (major == 3 && minor >= 2) => "#version 150\n",
        _ => null,
    };

    public const string Vertex = """
        in vec2 aPosition;
        in vec2 aTexCoord;
        out vec2 vTexCoord;
        void main()
        {
            vTexCoord = aTexCoord;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    /// <summary>NV12: Y in one texture, U/V in the red and green of another; uColor turns (Y, U, V, 1) into RGB.</summary>
    public const string Nv12 = """
        in vec2 vTexCoord;
        out vec4 fragColor;
        uniform sampler2D uLuma;
        uniform sampler2D uChroma;
        uniform mat4 uColor;
        void main()
        {
            vec4 yuv = vec4(texture(uLuma, vTexCoord).r, texture(uChroma, vTexCoord).rg, 1.0);
            fragColor = vec4(clamp((uColor * yuv).rgb, 0.0, 1.0), 1.0);
        }
        """;

    /// <summary>BGRA bytes uploaded as RGBA (OpenGL ES cannot upload BGRA): the channels are swapped back here.</summary>
    public const string Bgra = """
        in vec2 vTexCoord;
        out vec4 fragColor;
        uniform sampler2D uLuma;
        void main()
        {
            fragColor = vec4(texture(uLuma, vTexCoord).bgr, 1.0);
        }
        """;
}
