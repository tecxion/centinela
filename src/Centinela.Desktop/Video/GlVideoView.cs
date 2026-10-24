using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Centinela.Desktop.Services.Ui;
using Centinela.Media;
using static Avalonia.OpenGL.GlConsts;

namespace Centinela.Desktop.Video;

/// <summary>
/// The OpenGL half of a <see cref="VideoSurface"/>: uploads its newest frame and draws it with a shader (NV12 turned
/// into RGB on the graphics card; BGRA as it is). Tells <see cref="GpuVideo"/> whether OpenGL works here. Avalonia
/// calls it on the UI thread.
/// </summary>
sealed unsafe class GlVideoView : OpenGlControlBase
{
    // Missing from Avalonia's GlConsts.
    const int GL_TEXTURE1 = 0x84C1;
    const int GL_TEXTURE_WRAP_S = 0x2802, GL_TEXTURE_WRAP_T = 0x2803, GL_CLAMP_TO_EDGE = 0x812F;
    const int GL_UNPACK_ALIGNMENT = 0x0CF5;
    const int GL_R8 = 0x8229, GL_RG8 = 0x822B, GL_RED = 0x1903, GL_RG = 0x8227;
    const int GL_STREAM_DRAW = 0x88E0, GL_TRIANGLE_STRIP = 0x0005, GL_BLEND = 0x0BE2;

    sealed record Shader(int Program, int Color);

    readonly VideoSurface _owner;
    delegate* unmanaged<int, int, int, int, int, int, int, int, IntPtr, void> _texSubImage2D;
    delegate* unmanaged<int, int, void> _pixelStorei;
    delegate* unmanaged<int, int, void> _uniform1i;
    Shader? _nv12, _bgra;
    int _luma, _chroma, _buffer, _vertexArray;
    (int Format, int Width, int Height) _lumaShape, _chromaShape;
    FrameLayout? _layout; // of the uploaded frame; null = nothing to draw yet
    (YuvMatrix Matrix, bool FullRange) _colorFor = (YuvMatrix.Bt709, false);
    float[] _color = YuvToRgb.Matrix(YuvMatrix.Bt709, false);

    public GlVideoView(VideoSurface owner)
    {
        _owner = owner;
        IsHitTestVisible = false; // pointer input goes to the tile, as without OpenGL
    }

    /// <summary>True while OpenGL works here; until then (and after a failure) the processor paints.</summary>
    public bool Ready { get; private set; }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        if (GpuVideo.State == GpuVideo.Mode.Off) return;
        try
        {
            var header = GlShaders.Header(GlVersion.Type, GlVersion.Major, GlVersion.Minor)
                ?? throw new NotSupportedException($"OpenGL {GlVersion.Type} {GlVersion.Major}.{GlVersion.Minor}");
            if (GpuVideo.IsSoftwareRenderer(gl.Renderer)) throw new NotSupportedException(gl.Renderer);
            _texSubImage2D = (delegate* unmanaged<int, int, int, int, int, int, int, int, IntPtr, void>)Proc(gl, "glTexSubImage2D");
            _pixelStorei = (delegate* unmanaged<int, int, void>)Proc(gl, "glPixelStorei");
            _uniform1i = (delegate* unmanaged<int, int, void>)Proc(gl, "glUniform1i");
            _nv12 = Link(gl, header, GlShaders.Nv12);
            _bgra = Link(gl, header, GlShaders.Bgra);
            _luma = NewTexture(gl);
            _chroma = NewTexture(gl);
            _buffer = gl.GenBuffer();
            if (gl.IsGenVertexArraysAvailable) _vertexArray = gl.GenVertexArray();
            Ready = true;
            GpuVideo.TurnOn();
            _owner.OnGlReady();
        }
        catch (Exception)
        {
            Fail();
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        try
        {
            gl.BindFramebuffer(GL_FRAMEBUFFER, fb);
            var scaling = VisualRoot?.RenderScaling ?? 1;
            gl.Viewport(0, 0, Math.Max(1, (int)(Bounds.Width * scaling)), Math.Max(1, (int)(Bounds.Height * scaling)));
            gl.Disable(GL_BLEND);
            gl.Disable(GL_DEPTH_TEST);
            gl.Disable(GL_SCISSOR_TEST);
            gl.Disable(GL_CULL_FACE);
            // Transparent: before the first frame (or after a failed init, until the child is dropped) the processor's
            // image shows through; around the image, the tile.
            gl.ClearColor(0, 0, 0, 0);
            gl.Clear(GL_COLOR_BUFFER_BIT);
            if (!Ready || _nv12 is null || _bgra is null) return;
            _owner.TryReadFrame(frame => Upload(gl, frame));
            if (_layout is not { } layout) return;
            Draw(gl, layout == FrameLayout.Nv12 ? _nv12 : _bgra, layout);
        }
        catch (Exception)
        {
            Fail();
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_nv12 is not null) gl.DeleteProgram(_nv12.Program);
        if (_bgra is not null) gl.DeleteProgram(_bgra.Program);
        if (_luma != 0) gl.DeleteTexture(_luma);
        if (_chroma != 0) gl.DeleteTexture(_chroma);
        if (_buffer != 0) gl.DeleteBuffer(_buffer);
        if (_vertexArray != 0) gl.DeleteVertexArray(_vertexArray);
        Forget();
    }

    protected override void OnOpenGlLost() => Forget();

    /// <summary>The context is gone: everything is created again on the next OpenGL init.</summary>
    void Forget()
    {
        Ready = false;
        _nv12 = _bgra = null;
        _luma = _chroma = _buffer = _vertexArray = 0;
        _lumaShape = _chromaShape = default;
        _layout = null;
    }

    /// <summary>OpenGL does not work here: the whole app goes back to the processor, after Avalonia's call returns.</summary>
    void Fail()
    {
        Ready = false;
        _layout = null;
        UiThread.Post(GpuVideo.TurnOff);
    }

    void Upload(GlInterface gl, VideoFrame frame)
    {
        _pixelStorei(GL_UNPACK_ALIGNMENT, 1); // rows are packed tight (odd widths)
        fixed (byte* data = frame.Data)
        {
            if (frame.Layout == FrameLayout.Nv12)
            {
                Upload(gl, GL_TEXTURE0, _luma, GL_R8, GL_RED, frame.Width, frame.Height, data, ref _lumaShape);
                Upload(gl, GL_TEXTURE1, _chroma, GL_RG8, GL_RG, frame.ChromaWidth, frame.ChromaHeight,
                    data + frame.ChromaOffset, ref _chromaShape);
                if (_colorFor != (frame.Matrix, frame.FullRange))
                {
                    _colorFor = (frame.Matrix, frame.FullRange);
                    _color = YuvToRgb.Matrix(frame.Matrix, frame.FullRange);
                }
            }
            else
                Upload(gl, GL_TEXTURE0, _luma, GL_RGBA8, GL_RGBA, frame.Width, frame.Height, data, ref _lumaShape);
        }
        _layout = frame.Layout;
    }

    void Upload(GlInterface gl, int unit, int texture, int internalFormat, int format, int width, int height, byte* data,
        ref (int Format, int Width, int Height) shape)
    {
        gl.ActiveTexture(unit);
        gl.BindTexture(GL_TEXTURE_2D, texture);
        if (shape == (internalFormat, width, height))
            _texSubImage2D(GL_TEXTURE_2D, 0, 0, 0, width, height, format, GL_UNSIGNED_BYTE, (IntPtr)data);
        else
        {
            gl.TexImage2D(GL_TEXTURE_2D, 0, internalFormat, width, height, 0, format, GL_UNSIGNED_BYTE, (IntPtr)data);
            shape = (internalFormat, width, height);
        }
    }

    void Draw(GlInterface gl, Shader shader, FrameLayout layout)
    {
        gl.UseProgram(shader.Program);
        if (layout == FrameLayout.Nv12)
            fixed (float* color = _color) gl.UniformMatrix4fv(shader.Color, 1, false, color);
        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, _luma);
        gl.ActiveTexture(GL_TEXTURE1);
        gl.BindTexture(GL_TEXTURE_2D, _chroma);
        var vertices = VideoQuad.Vertices(_owner.DrawRect, Bounds.Size);
        if (_vertexArray != 0) gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(GL_ARRAY_BUFFER, _buffer);
        fixed (float* v = vertices)
            gl.BufferData(GL_ARRAY_BUFFER, new IntPtr(vertices.Length * sizeof(float)), (IntPtr)v, GL_STREAM_DRAW);
        gl.VertexAttribPointer(0, 2, GL_FLOAT, 0, 4 * sizeof(float), IntPtr.Zero);
        gl.VertexAttribPointer(1, 2, GL_FLOAT, 0, 4 * sizeof(float), new IntPtr(2 * sizeof(float)));
        gl.EnableVertexAttribArray(0);
        gl.EnableVertexAttribArray(1);
        gl.DrawArrays(GL_TRIANGLE_STRIP, 0, new IntPtr(4));
    }

    Shader Link(GlInterface gl, string header, string fragment)
    {
        var vertexShader = Compile(gl, GL_VERTEX_SHADER, header + GlShaders.Vertex);
        var fragmentShader = Compile(gl, GL_FRAGMENT_SHADER, header + fragment);
        var program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        gl.BindAttribLocationString(program, 0, "aPosition");
        gl.BindAttribLocationString(program, 1, "aTexCoord");
        var error = gl.LinkProgramAndGetError(program);
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);
        if (error is not null)
        {
            gl.DeleteProgram(program);
            throw new InvalidOperationException(error);
        }
        gl.UseProgram(program);
        _uniform1i(gl.GetUniformLocationString(program, "uLuma"), 0);
        _uniform1i(gl.GetUniformLocationString(program, "uChroma"), 1); // -1 in the BGRA program: ignored
        return new Shader(program, gl.GetUniformLocationString(program, "uColor"));
    }

    static int Compile(GlInterface gl, int type, string source)
    {
        var shader = gl.CreateShader(type);
        var error = gl.CompileShaderAndGetError(shader, source);
        if (error is null) return shader;
        gl.DeleteShader(shader);
        throw new InvalidOperationException(error);
    }

    static int NewTexture(GlInterface gl)
    {
        var texture = gl.GenTexture();
        gl.BindTexture(GL_TEXTURE_2D, texture);
        // Without mipmaps the default minification filter leaves the texture incomplete (it samples black).
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        return texture;
    }

    static IntPtr Proc(GlInterface gl, string name)
    {
        var proc = gl.GetProcAddress(name);
        return proc != IntPtr.Zero ? proc : throw new EntryPointNotFoundException(name);
    }
}
