using Avalonia.OpenGL;
using Centinela.Desktop.Video;

namespace Centinela.Desktop.Tests;

public class GlShadersTests
{
    [Theory]
    [InlineData(GlProfileType.OpenGLES, 3, 0, "#version 300 es")]
    [InlineData(GlProfileType.OpenGLES, 3, 2, "#version 300 es")]
    [InlineData(GlProfileType.OpenGL, 3, 2, "#version 150")]
    [InlineData(GlProfileType.OpenGL, 4, 6, "#version 150")]
    public void Supported_contexts_get_their_glsl_version(GlProfileType type, int major, int minor, string version) =>
        Assert.StartsWith(version + "\n", GlShaders.Header(type, major, minor));

    [Theory]
    [InlineData(GlProfileType.OpenGLES, 2, 0)]
    [InlineData(GlProfileType.OpenGL, 3, 1)]
    [InlineData(GlProfileType.OpenGL, 2, 1)]
    public void Old_contexts_are_not_used(GlProfileType type, int major, int minor) =>
        Assert.Null(GlShaders.Header(type, major, minor));

    [Fact]
    public void Es_header_sets_high_precision() =>
        Assert.Contains("precision highp float;", GlShaders.Header(GlProfileType.OpenGLES, 3, 0));
}
