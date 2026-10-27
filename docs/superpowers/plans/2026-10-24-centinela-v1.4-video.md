# Centinela v1.4 — Mejoras de vídeo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mejor imagen y menos trabajo del procesador en el vídeo en directo: colores correctos (BT.709 en HD), conversión en varios núcleos, filtro bicúbico en imágenes grandes, búfer UDP mayor, y pintado con la tarjeta gráfica (OpenGL) con vuelta automática al pintado actual si OpenGL no funciona.

**Architecture:** `Centinela.Media` convierte cada imagen descodificada con la API nueva de swscale (`sws_scale_frame`, FFmpeg 9), que respeta la matriz de color y el rango de la imagen; cuando la cámara no los indica se deducen por la altura (HD → BT.709), como hacen los reproductores. El buzón de imágenes (`FrameMailbox`) pasa a llevar imágenes BGRA (como hoy) o NV12 (Y + U/V a media resolución) según `FrameOutput.Layout`, global para la aplicación. En `Centinela.Desktop`, `VideoSurface` lleva dentro un `GlVideoView` (`OpenGlControlBase` de Avalonia) que sube las imágenes como texturas y las convierte a RGB con un sombreador; la primera vista cuyo OpenGL arranca enciende el modo NV12 (`GpuVideo`), y cualquier fallo lo apaga para el resto de la ejecución, volviendo al mapa de bits de siempre. El zoom digital, cuando pinta OpenGL, lo dibuja el propio sombreador (nítido) en lugar de una transformación de Avalonia (que ampliaría la imagen ya rasterizada).

**Tech Stack:** C# / .NET 10, Avalonia 11.3.22 (`Avalonia.OpenGL`, ya incluido en el paquete `Avalonia`), FFmpeg 9.0 (FFmpeg.AutoGen 9.0.1.1), xUnit.

**Spec:** no hay documento de diseño: el diseño se acordó en el chat el 24 de octubre (propuesta en cinco puntos; el usuario respondió «hazlos todos y luego probaré de nuevo la app»). Este plan es la referencia; las decisiones que el chat no fijó son *rulings* del controlador y quedan en el registro.

**Comprobado antes de escribir el plan (prueba aparte, descartada):** con FFmpeg 9 (`n9.0.2`), `sws_scale_frame` con un búfer gestionado como destino (`data[0]` puesto a mano, sin `buf`) funciona; respeta `colorspace`/`color_range` del origen (rojo BT.709 limitado Y=63 U=102 V=240 → B=0 G=0 R=255 con la etiqueta BT.709; R=233 si la etiqueta falta, porque FFmpeg supone BT.601); `threads = 4` funciona; NV12 → NV12 con escalado conserva los valores (63/102/240).

**Fuera de este plan:** copia directa de texturas de la descodificación por hardware a OpenGL (D3D11/VideoToolbox/VAAPI interop), sombreador bicúbico, opción visible en los menús para elegir el modo de pintado, más hilos de descodificación, ritmo de presentación por marcas de tiempo.

## Global Constraints

- .NET 10; Avalonia 11.3.22; FFmpeg.AutoGen 9.0.1.1. Ningún paquete NuGet nuevo.
- Si OpenGL no está o falla, la aplicación se comporta como hoy (mapa de bits BGRA, mismos textos salvo la línea de estadísticas).
- Textos visibles en español; los nuevos los fija este plan.
- Un archivo, una responsabilidad; ningún archivo de más de ~300 líneas. La API pública de `Centinela.Media` que usa la interfaz (`VideoFrame`, `YuvMatrix`, `YuvToRgb`, `FrameOutput`) no expone tipos de FFmpeg.
- Las pruebas no usan la red real, cámaras reales ni OpenGL; las de FFmpeg usan imágenes sintéticas o el servidor RTSP de pruebas (`[Collection("rtsp")]`).
- `FrameOutput.Layout` es global: una prueba que lo cambie debe estar en la colección `rtsp` (las pruebas de sesiones no corren en paralelo entre sí) y devolverlo a `FrameLayout.Bgra` en un `finally`.
- Compilar y probar Desktop/solución con `-p:BaseOutputPath=C:/Users/PCgaming/AppData/Local/Temp/claude/X--project1-camara-win/30c3d4fb-5b9f-42d9-8a10-21b243cdd2ca/scratchpad/bin/` (Core/Media/Platform sin ese parámetro). No ejecutar la aplicación.
- `git add` con rutas explícitas (nunca los `README.sync-conflict-*.md`); commits con línea en blanco + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Cero advertencias de compilación.

---

### Task 1: Colores correctos, varios núcleos, bicúbico y búfer UDP

**Files:**
- Create: `src/Centinela.Media/YuvMatrix.cs`
- Create: `src/Centinela.Media/ColorTags.cs`
- Create: `src/Centinela.Media/FrameConverter.cs`
- Modify: `src/Centinela.Media/StreamSession.cs` (campo `_sws`, `Run` finally, `PlayOnce` opciones y reinicio de estadísticas, `PresentFrame`, `UpdateStats`)
- Modify: `src/Centinela.Media/StreamStats.cs`
- Modify: `src/Centinela.Media/SnapshotWriter.cs`
- Test: `tests/Centinela.Media.Tests/ColorTagsTests.cs`
- Test: `tests/Centinela.Media.Tests/FrameConverterTests.cs`

**Interfaces:**
- Produces: `public enum YuvMatrix { Bt601, Bt709, Bt2020 }`; `public static class ColorTags` con `(YuvMatrix Matrix, bool FullRange) Infer(AVColorSpace, AVColorRange, AVPixelFormat, int height)` y `unsafe (YuvMatrix Matrix, bool FullRange) Apply(AVFrame*)`; `sealed unsafe class FrameConverter : IDisposable` (internal) con `void Convert(AVFrame* src, VideoFrame target)`, `internal static SwsFlags FilterFor(int width)`, `internal const int BicubicFromWidth = 960`, `internal static int Threads`; `StreamStats.ConvertMs` (double, último parámetro opcional, por defecto 0). La tarea 2 amplía `FrameConverter.Convert` para NV12; la tarea 4 muestra `ConvertMs`.

- [ ] **Step 1: Write the failing tests**

`tests/Centinela.Media.Tests/ColorTagsTests.cs`:

```csharp
using FFmpeg.AutoGen;

namespace Centinela.Media.Tests;

public class ColorTagsTests
{
    const AVColorSpace UnsetSpace = AVColorSpace.AVCOL_SPC_UNSPECIFIED;
    const AVColorRange UnsetRange = AVColorRange.AVCOL_RANGE_UNSPECIFIED;

    [Theory]
    [InlineData(2160, YuvMatrix.Bt709)]
    [InlineData(1080, YuvMatrix.Bt709)]
    [InlineData(720, YuvMatrix.Bt709)]
    [InlineData(576, YuvMatrix.Bt601)]
    [InlineData(360, YuvMatrix.Bt601)]
    public void Unset_matrix_follows_the_height(int height, YuvMatrix expected) =>
        Assert.Equal(expected, ColorTags.Infer(UnsetSpace, UnsetRange, AVPixelFormat.AV_PIX_FMT_YUV420P, height).Matrix);

    [Theory]
    [InlineData(AVColorSpace.AVCOL_SPC_BT470BG, 1080, YuvMatrix.Bt601)]
    [InlineData(AVColorSpace.AVCOL_SPC_SMPTE170M, 1080, YuvMatrix.Bt601)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT709, 360, YuvMatrix.Bt709)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT2020_NCL, 2160, YuvMatrix.Bt2020)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT2020_CL, 2160, YuvMatrix.Bt2020)]
    public void Tagged_matrix_wins_over_the_height(AVColorSpace space, int height, YuvMatrix expected) =>
        Assert.Equal(expected, ColorTags.Infer(space, UnsetRange, AVPixelFormat.AV_PIX_FMT_YUV420P, height).Matrix);

    [Theory]
    [InlineData(AVColorRange.AVCOL_RANGE_UNSPECIFIED, AVPixelFormat.AV_PIX_FMT_YUV420P, false)]
    [InlineData(AVColorRange.AVCOL_RANGE_UNSPECIFIED, AVPixelFormat.AV_PIX_FMT_NV12, false)]
    [InlineData(AVColorRange.AVCOL_RANGE_UNSPECIFIED, AVPixelFormat.AV_PIX_FMT_YUVJ420P, true)]
    [InlineData(AVColorRange.AVCOL_RANGE_UNSPECIFIED, AVPixelFormat.AV_PIX_FMT_YUVJ422P, true)]
    [InlineData(AVColorRange.AVCOL_RANGE_JPEG, AVPixelFormat.AV_PIX_FMT_YUV420P, true)]
    [InlineData(AVColorRange.AVCOL_RANGE_MPEG, AVPixelFormat.AV_PIX_FMT_YUVJ420P, false)]
    public void Range_is_the_tag_or_full_for_jpeg_formats(AVColorRange range, AVPixelFormat format, bool full) =>
        Assert.Equal(full, ColorTags.Infer(UnsetSpace, range, format, 1080).FullRange);
}
```

`tests/Centinela.Media.Tests/FrameConverterTests.cs`:

```csharp
using FFmpeg.AutoGen;

namespace Centinela.Media.Tests;

public unsafe class FrameConverterTests
{
    // Pure red in BT.709, limited range.
    const byte RedY = 63, RedU = 102, RedV = 240;

    /// <summary>A flat NV12 image of one colour with the given tags; the caller frees it.</summary>
    internal static AVFrame* Nv12(int width, int height, byte y, byte u, byte v,
        AVColorSpace space = AVColorSpace.AVCOL_SPC_UNSPECIFIED, AVColorRange range = AVColorRange.AVCOL_RANGE_UNSPECIFIED)
    {
        FFmpegLoader.Initialize();
        var frame = ffmpeg.av_frame_alloc();
        frame->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        frame->width = width;
        frame->height = height;
        frame->colorspace = space;
        frame->color_range = range;
        FFmpegException.ThrowIfError(ffmpeg.av_frame_get_buffer(frame, 0), "alloc");
        for (var row = 0; row < height; row++)
            new Span<byte>(frame->data[0] + row * frame->linesize[0], width).Fill(y);
        for (var row = 0; row < (height + 1) / 2; row++)
        {
            var uv = new Span<byte>(frame->data[1] + row * frame->linesize[1], (width + 1) / 2 * 2);
            for (var i = 0; i < uv.Length; i += 2) (uv[i], uv[i + 1]) = (u, v);
        }
        return frame;
    }

    static VideoFrame ToBgra(AVFrame* src, int width, int height)
    {
        var target = new VideoFrame(width, height);
        using var converter = new FrameConverter();
        converter.Convert(src, target);
        return target;
    }

    static void AssertPureRed(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            Assert.InRange(bgra[i], 0, 5);         // B
            Assert.InRange(bgra[i + 1], 0, 5);     // G
            Assert.InRange(bgra[i + 2], 250, 255); // R
        }
    }

    [Fact]
    public void Bt709_red_becomes_pure_red_in_every_pixel()
    {
        var src = Nv12(64, 64, RedY, RedU, RedV, AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG);
        try { AssertPureRed(ToBgra(src, 32, 32).Data); }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Fact]
    public void Untagged_hd_image_is_read_as_bt709()
    {
        var src = Nv12(1280, 720, RedY, RedU, RedV);
        try { AssertPureRed(ToBgra(src, 640, 360).Data); }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Fact]
    public void Untagged_sd_image_is_read_as_bt601()
    {
        var src = Nv12(640, 480, RedY, RedU, RedV);
        try
        {
            var bgra = ToBgra(src, 320, 240).Data;
            Assert.InRange(bgra[2], 225, 240); // BT.601 reads this sample as a darker red (≈233)
        }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Fact]
    public void Conversion_writes_the_inferred_tags_into_the_source()
    {
        var src = Nv12(1280, 720, RedY, RedU, RedV);
        try
        {
            ToBgra(src, 640, 360);
            Assert.Equal(AVColorSpace.AVCOL_SPC_BT709, src->colorspace);
            Assert.Equal(AVColorRange.AVCOL_RANGE_MPEG, src->color_range);
        }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Fact]
    public void Keeps_converting_when_the_filter_changes()
    {
        var src = Nv12(1920, 1080, RedY, RedU, RedV);
        try
        {
            using var converter = new FrameConverter();
            var big = new VideoFrame(1280, 720);
            converter.Convert(src, big);   // bicubic
            var small = new VideoFrame(320, 180);
            converter.Convert(src, small); // bilinear: a new context
            AssertPureRed(big.Data);
            AssertPureRed(small.Data);
        }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Theory]
    [InlineData(1920, SwsFlags.SWS_BICUBIC)]
    [InlineData(960, SwsFlags.SWS_BICUBIC)]
    [InlineData(959, SwsFlags.SWS_BILINEAR)]
    [InlineData(320, SwsFlags.SWS_BILINEAR)]
    public void Large_images_use_bicubic(int width, SwsFlags expected) =>
        Assert.Equal(expected, FrameConverter.FilterFor(width));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Centinela.Media.Tests --filter "FullyQualifiedName~ColorTagsTests|FullyQualifiedName~FrameConverterTests"`
Expected: FAIL to compile (`YuvMatrix`, `ColorTags`, `FrameConverter` do not exist).

- [ ] **Step 3: Write the implementation**

`src/Centinela.Media/YuvMatrix.cs`:

```csharp
namespace Centinela.Media;

/// <summary>The coefficients a camera used to turn RGB into YUV (and that turn it back).</summary>
public enum YuvMatrix { Bt601, Bt709, Bt2020 }
```

`src/Centinela.Media/ColorTags.cs`:

```csharp
using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>
/// The colour matrix and range a decoded frame really uses. Cameras often leave them unset; FFmpeg then assumes
/// BT.601, which tints HD images, so an unset matrix follows the height as players do (720 lines or more: BT.709).
/// </summary>
public static class ColorTags
{
    public static (YuvMatrix Matrix, bool FullRange) Infer(AVColorSpace space, AVColorRange range, AVPixelFormat format, int height)
    {
        var matrix = space switch
        {
            AVColorSpace.AVCOL_SPC_BT709 => YuvMatrix.Bt709,
            AVColorSpace.AVCOL_SPC_BT2020_NCL or AVColorSpace.AVCOL_SPC_BT2020_CL => YuvMatrix.Bt2020,
            AVColorSpace.AVCOL_SPC_BT470BG or AVColorSpace.AVCOL_SPC_SMPTE170M or AVColorSpace.AVCOL_SPC_FCC => YuvMatrix.Bt601,
            _ => height >= 720 ? YuvMatrix.Bt709 : YuvMatrix.Bt601,
        };
        var fullRange = range switch
        {
            AVColorRange.AVCOL_RANGE_JPEG => true,
            AVColorRange.AVCOL_RANGE_MPEG => false,
            // The deprecated "J" formats (MJPEG) are full range by definition.
            _ => format is AVPixelFormat.AV_PIX_FMT_YUVJ420P or AVPixelFormat.AV_PIX_FMT_YUVJ422P
                or AVPixelFormat.AV_PIX_FMT_YUVJ444P,
        };
        return (matrix, fullRange);
    }

    /// <summary>Writes the inferred matrix and range into the frame, so swscale converts with them.</summary>
    public static unsafe (YuvMatrix Matrix, bool FullRange) Apply(AVFrame* frame)
    {
        var tags = Infer(frame->colorspace, frame->color_range, (AVPixelFormat)frame->format, frame->height);
        frame->colorspace = tags.Matrix switch
        {
            YuvMatrix.Bt709 => AVColorSpace.AVCOL_SPC_BT709,
            YuvMatrix.Bt2020 => AVColorSpace.AVCOL_SPC_BT2020_NCL,
            _ => AVColorSpace.AVCOL_SPC_BT470BG,
        };
        frame->color_range = tags.FullRange ? AVColorRange.AVCOL_RANGE_JPEG : AVColorRange.AVCOL_RANGE_MPEG;
        return tags;
    }
}
```

`src/Centinela.Media/FrameConverter.cs`:

```csharp
using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>
/// Turns a decoded frame into a mailbox frame with swscale: with the frame's real colours (<see cref="ColorTags"/>),
/// over several cores, bicubic for large images. One per session; only its decoding thread calls it.
/// </summary>
sealed unsafe class FrameConverter : IDisposable
{
    /// <summary>Images at least this wide are scaled bicubic (sharper); smaller ones bilinear (cheaper).</summary>
    internal const int BicubicFromWidth = 960;

    /// <summary>Cores per conversion: enough for a 4 MP image without one camera taking the whole processor.</summary>
    internal static int Threads { get; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    SwsContext* _sws;
    SwsFlags _filter;
    AVFrame* _dst;

    internal static SwsFlags FilterFor(int width) =>
        width >= BicubicFromWidth ? SwsFlags.SWS_BICUBIC : SwsFlags.SWS_BILINEAR;

    /// <summary>Converts <paramref name="src"/> into <paramref name="target"/>, scaled to its size; tags <paramref name="src"/> with its inferred colours.</summary>
    public void Convert(AVFrame* src, VideoFrame target)
    {
        ColorTags.Apply(src);
        var filter = FilterFor(target.Width);
        if (_sws is null || filter != _filter)
        {
            FreeContext();
            _sws = ffmpeg.sws_alloc_context();
            if (_sws is null) throw new InvalidOperationException("sws_alloc_context failed");
            _sws->threads = Threads;
            _sws->flags = (uint)filter;
            _filter = filter;
        }
        if (_dst is null) _dst = ffmpeg.av_frame_alloc();
        _dst->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        _dst->width = target.Width;
        _dst->height = target.Height;
        _dst->colorspace = AVColorSpace.AVCOL_SPC_RGB;
        _dst->color_range = AVColorRange.AVCOL_RANGE_JPEG;
        // Same primaries and transfer as the source: only the matrix is converted, never the gamut.
        _dst->color_primaries = src->color_primaries;
        _dst->color_trc = src->color_trc;
        int err;
        fixed (byte* data = target.Data)
        {
            _dst->data[0] = data;
            _dst->linesize[0] = target.Stride;
            err = ffmpeg.sws_scale_frame(_sws, _dst, src);
            _dst->data[0] = null; // managed buffer: never left behind in the frame
        }
        FFmpegException.ThrowIfError(err, "scale");
    }

    public void Dispose()
    {
        FreeContext();
        if (_dst is null) return;
        var dst = _dst;
        ffmpeg.av_frame_free(&dst);
        _dst = null;
    }

    void FreeContext()
    {
        if (_sws is null) return;
        var sws = _sws;
        ffmpeg.sws_free_context(&sws);
        _sws = null;
    }
}
```

`src/Centinela.Media/StreamStats.cs` (whole file):

```csharp
namespace Centinela.Media;

/// <summary>
/// Live-view health: frames per second, packet-received→frame-published latency, decoder, freshness, for a
/// smoothed view its delay in seconds (0 = frames shown as they arrive) and how often it ran dry, and how long the
/// processor takes to convert and scale each frame (milliseconds, averaged).
/// </summary>
public sealed record StreamStats(double Fps, double LatencyMs, bool HardwareDecoding, TimeSpan SinceLastFrame,
    double SmoothingSeconds = 0, int Rebuffers = 0, double ConvertMs = 0)
{
    public static StreamStats Empty { get; } = new(0, 0, false, TimeSpan.Zero);
}
```

`src/Centinela.Media/StreamSession.cs`:

1. Replace the field `SwsContext* _sws;` with:

```csharp
    readonly FrameConverter _converter = new();
```

and next to `double _latencyEma = -1;` add:

```csharp
    double _convertEma = -1;
```

2. In `Run`'s `finally`, replace

```csharp
            ffmpeg.sws_freeContext(_sws);
            _sws = null;
```

with

```csharp
            _converter.Dispose();
```

3. In `PlayOnce`, after `_latencyEma = -1;` add `_convertEma = -1;`, and after the `rtsp_transport` option add:

```csharp
            // Keyframes of big images arrive as bursts of datagrams; a small socket buffer drops some and smears the image.
            if (_useUdp) ffmpeg.av_dict_set(&options, "buffer_size", "4194304", 0);
```

4. In `PresentFrame`, after `src = sw;` (inside the hardware branch, after the transfer succeeded) add:

```csharp
            // The transfer copies pixels only; the colour tags are needed to convert them right.
            FFmpegException.ThrowIfError(ffmpeg.av_frame_copy_props(sw, frame), "frame props");
```

and replace everything from `var target = Mailbox.Rent(width, height);` to the end of the method with:

```csharp
        var target = Mailbox.Rent(width, height);
        var started = Stopwatch.GetTimestamp();
        _converter.Convert(src, target);
        var convertMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Mailbox.Publish(target);
        UpdateStats(hardware, convertMs);
    }
```

5. `UpdateStats`:

```csharp
    void UpdateStats(bool hardware, double convertMs)
    {
        var now = Stopwatch.GetTimestamp();
        var latency = Stopwatch.GetElapsedTime(_packetTimestamp, now).TotalMilliseconds;
        _latencyEma = _latencyEma < 0 ? latency : _latencyEma * 0.9 + latency * 0.1;
        _convertEma = _convertEma < 0 ? convertMs : _convertEma * 0.9 + convertMs * 0.1;
        ... (unchanged up to the Volatile.Write)
        Volatile.Write(ref _stats, new StreamStats(_fps, _latencyEma, hardware, TimeSpan.Zero, SmoothingSeconds,
            Volatile.Read(ref _rebuffers), _convertEma));
    }
```

Also update the class summary ("push the newest frame (BGRA, scaled to the target size)") only if it becomes untrue — it stays true in this task.

`src/Centinela.Media/SnapshotWriter.cs`: tag the source and convert with `sws_scale_frame` so the PNG has the same colours as the live view. Replace the RGB frame setup and the scaling block:

```csharp
            ColorTags.Apply(source);
            rgb->format = (int)AVPixelFormat.AV_PIX_FMT_RGB24;
            rgb->width = source->width;
            rgb->height = source->height;
            rgb->colorspace = AVColorSpace.AVCOL_SPC_RGB;
            rgb->color_range = AVColorRange.AVCOL_RANGE_JPEG;
            rgb->color_primaries = source->color_primaries;
            rgb->color_trc = source->color_trc;
            FFmpegException.ThrowIfError(ffmpeg.av_frame_get_buffer(rgb, 0), "alloc rgb frame");

            sws = ffmpeg.sws_alloc_context();
            if (sws is null) throw new InvalidOperationException("sws_alloc_context failed");
            sws->flags = (uint)SwsFlags.SWS_BICUBIC;
            FFmpegException.ThrowIfError(ffmpeg.sws_scale_frame(sws, rgb, source), "scale png");
```

and in `finally` replace `ffmpeg.sws_freeContext(sws);` with `ffmpeg.sws_free_context(&sws);`.

- [ ] **Step 4: Run the Media tests**

Run: `dotnet test tests/Centinela.Media.Tests`
Expected: PASS (the new tests and all existing ones: sessions, snapshots, sharing, motion-style gray reads).

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Media/YuvMatrix.cs src/Centinela.Media/ColorTags.cs src/Centinela.Media/FrameConverter.cs src/Centinela.Media/StreamSession.cs src/Centinela.Media/StreamStats.cs src/Centinela.Media/SnapshotWriter.cs tests/Centinela.Media.Tests/ColorTagsTests.cs tests/Centinela.Media.Tests/FrameConverterTests.cs
git commit -m "feat: colores BT.709 en HD, conversión en varios núcleos y bicúbico en imágenes grandes"
```

---

### Task 2: Imágenes NV12 para la tarjeta gráfica

**Files:**
- Create: `src/Centinela.Media/VideoFrame.cs` (move `VideoFrame` out of `FrameMailbox.cs`, add `FrameLayout`)
- Create: `src/Centinela.Media/FrameOutput.cs`
- Create: `src/Centinela.Media/YuvToRgb.cs`
- Modify: `src/Centinela.Media/FrameMailbox.cs` (`Rent` takes the layout)
- Modify: `src/Centinela.Media/FrameConverter.cs` (NV12 output)
- Modify: `src/Centinela.Media/StreamSession.cs` (`PresentFrame` rents in `FrameOutput.Layout`; stats)
- Modify: `src/Centinela.Media/StreamStats.cs` (`GpuPainting`)
- Modify: `src/Centinela.Core/Motion/GrayFrame.cs` (`GrayScaler.FromLuma`)
- Modify: `src/Centinela.Desktop/Services/Motion/MotionService.cs:230-231`
- Test: `tests/Centinela.Media.Tests/FrameMailboxTests.cs`, `tests/Centinela.Media.Tests/FrameConverterTests.cs`, `tests/Centinela.Media.Tests/YuvToRgbTests.cs` (new), `tests/Centinela.Media.Tests/StreamSessionTests.cs`, `tests/Centinela.Core.Tests/GrayScalerTests.cs`

**Interfaces:**
- Consumes (Task 1): `FrameConverter.Convert`, `ColorTags.Apply` (returns the tags), `YuvMatrix`, `StreamStats.ConvertMs`, `UpdateStats(bool hardware, double convertMs)`.
- Produces: `public enum FrameLayout { Bgra, Nv12 }`; `VideoFrame(int width, int height, FrameLayout layout = FrameLayout.Bgra)` with `Layout`, `Stride` (BGRA row bytes, or the Y row bytes = `Width` for NV12), `ChromaWidth`, `ChromaHeight`, `ChromaOffset`, `Matrix` (`YuvMatrix`, settable), `FullRange` (bool, settable); `FrameMailbox.Rent(int width, int height, FrameLayout layout = FrameLayout.Bgra)`; `public static class FrameOutput { public static FrameLayout Layout { get; set; } }` (default `Bgra`); `public static class YuvToRgb { public static float[] Matrix(YuvMatrix matrix, bool fullRange) }` — column-major 4×4, (Y, U, V, 1) normalised 0–1 → (R, G, B, 1); `StreamStats.GpuPainting` (bool, last optional parameter); `GrayScaler.FromLuma(ReadOnlySpan<byte> luma, int width, int height, int stride, bool fullRange, GrayFrame target)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Centinela.Media.Tests/FrameMailboxTests.cs`:

```csharp
    [Fact]
    public void Nv12_frame_has_a_y_plane_and_a_half_size_chroma_plane()
    {
        var f = new VideoFrame(6, 4, FrameLayout.Nv12);
        Assert.Equal((6, 3, 2, 24), (f.Stride, f.ChromaWidth, f.ChromaHeight, f.ChromaOffset));
        Assert.Equal(6 * 4 + 3 * 2 * 2, f.Data.Length);
    }

    [Fact]
    public void Odd_nv12_sizes_round_the_chroma_plane_up()
    {
        var f = new VideoFrame(5, 3, FrameLayout.Nv12);
        Assert.Equal((3, 2), (f.ChromaWidth, f.ChromaHeight));
        Assert.Equal(15 + 3 * 2 * 2, f.Data.Length);
    }

    [Fact]
    public void Rent_does_not_reuse_a_frame_of_another_layout()
    {
        var mailbox = new FrameMailbox();
        mailbox.Publish(mailbox.Rent(4, 2));
        mailbox.Publish(mailbox.Rent(4, 2)); // the first one is now the spare
        var frame = mailbox.Rent(4, 2, FrameLayout.Nv12);
        Assert.Equal(FrameLayout.Nv12, frame.Layout);
    }
```

Append to `tests/Centinela.Media.Tests/FrameConverterTests.cs` (inside the class):

```csharp
    [Fact]
    public void Nv12_output_keeps_the_samples_and_says_how_to_read_them()
    {
        var src = Nv12(1280, 720, RedY, RedU, RedV);
        try
        {
            var target = new VideoFrame(640, 360, FrameLayout.Nv12);
            using var converter = new FrameConverter();
            converter.Convert(src, target);
            Assert.Equal(RedY, target.Data[0]);
            Assert.Equal(RedY, target.Data[target.ChromaOffset - 1]);
            Assert.Equal(RedU, target.Data[target.ChromaOffset]);
            Assert.Equal(RedV, target.Data[target.ChromaOffset + 1]);
            Assert.Equal(RedV, target.Data[^1]);
            Assert.Equal((YuvMatrix.Bt709, false), (target.Matrix, target.FullRange));
        }
        finally { ffmpeg.av_frame_free(&src); }
    }
```

`tests/Centinela.Media.Tests/YuvToRgbTests.cs`:

```csharp
using FFmpeg.AutoGen;

namespace Centinela.Media.Tests;

/// <summary>The graphics card's colour matrix must give the same colours as swscale on the processor.</summary>
public unsafe class YuvToRgbTests
{
    static readonly (byte Y, byte U, byte V)[] Samples =
        [(63, 102, 240), (235, 128, 128), (16, 128, 128), (128, 60, 200), (180, 200, 50), (100, 128, 128)];

    /// <summary>The matrix applied to one sample (0–255 each), clamped like the shader; 0–255 out.</summary>
    static (double R, double G, double B) Apply(float[] m, byte y, byte u, byte v)
    {
        double Y = y / 255.0, U = u / 255.0, V = v / 255.0;
        double Row(int r) => Math.Clamp(m[r] * Y + m[4 + r] * U + m[8 + r] * V + m[12 + r], 0, 1) * 255;
        return (Row(0), Row(1), Row(2));
    }

    [Theory]
    [InlineData(YuvMatrix.Bt601, false)]
    [InlineData(YuvMatrix.Bt601, true)]
    [InlineData(YuvMatrix.Bt709, false)]
    [InlineData(YuvMatrix.Bt709, true)]
    [InlineData(YuvMatrix.Bt2020, false)]
    [InlineData(YuvMatrix.Bt2020, true)]
    public void Matrix_matches_swscale(YuvMatrix matrix, bool fullRange)
    {
        var space = matrix switch
        {
            YuvMatrix.Bt709 => AVColorSpace.AVCOL_SPC_BT709,
            YuvMatrix.Bt2020 => AVColorSpace.AVCOL_SPC_BT2020_NCL,
            _ => AVColorSpace.AVCOL_SPC_BT470BG,
        };
        var range = fullRange ? AVColorRange.AVCOL_RANGE_JPEG : AVColorRange.AVCOL_RANGE_MPEG;
        var m = YuvToRgb.Matrix(matrix, fullRange);
        foreach (var (y, u, v) in Samples)
        {
            var src = FrameConverterTests.Nv12(64, 64, y, u, v, space, range);
            try
            {
                var bgra = new VideoFrame(32, 32);
                using var converter = new FrameConverter();
                converter.Convert(src, bgra);
                var (r, g, b) = Apply(m, y, u, v);
                var sample = $"{matrix} full={fullRange} yuv=({y},{u},{v})";
                Assert.True(Math.Abs(bgra.Data[2] - r) <= 3, $"R {bgra.Data[2]} vs {r:0.0} {sample}");
                Assert.True(Math.Abs(bgra.Data[1] - g) <= 3, $"G {bgra.Data[1]} vs {g:0.0} {sample}");
                Assert.True(Math.Abs(bgra.Data[0] - b) <= 3, $"B {bgra.Data[0]} vs {b:0.0} {sample}");
            }
            finally { ffmpeg.av_frame_free(&src); }
        }
    }

    [Fact]
    public void Limited_range_white_and_black_reach_the_ends()
    {
        var m = YuvToRgb.Matrix(YuvMatrix.Bt709, fullRange: false);
        Assert.Equal((255.0, 255.0, 255.0), Round(Apply(m, 235, 128, 128)));
        Assert.Equal((0.0, 0.0, 0.0), Round(Apply(m, 16, 128, 128)));
    }

    static (double, double, double) Round((double R, double G, double B) c) =>
        (Math.Round(c.R), Math.Round(c.G), Math.Round(c.B));
}
```

Append to `tests/Centinela.Media.Tests/StreamSessionTests.cs` (inside the class; it is in the `rtsp` collection):

```csharp
    [SkippableFact]
    public void Publishes_nv12_frames_while_the_graphics_card_paints()
    {
        FrameOutput.Layout = FrameLayout.Nv12;
        try
        {
            using var session = Open(server.Url("open"));
            session.SetTargetSize(320, 180);
            session.Start();
            Assert.True(TestUtil.WaitFor(() => session.Mailbox.Sequence > 0, Ten), session.LastError);

            long seq = 0;
            (FrameLayout Layout, int Width) seen = default;
            session.Mailbox.TryRead(ref seq, f => seen = (f.Layout, f.Width));
            Assert.Equal((FrameLayout.Nv12, 320), seen);
            Assert.True(session.Stats.GpuPainting);
        }
        finally
        {
            FrameOutput.Layout = FrameLayout.Bgra;
        }
    }
```

Append to `tests/Centinela.Core.Tests/GrayScalerTests.cs` (inside the class):

```csharp
    [Fact]
    public void Limited_range_luma_is_stretched_to_full_range()
    {
        var frame = GrayScaler.Create();
        var luma = new byte[320 * 180];
        Array.Fill(luma, (byte)16);
        GrayScaler.FromLuma(luma, 320, 180, 320, fullRange: false, frame);
        Assert.All(frame.Pixels, p => Assert.Equal(0, p));
        Array.Fill(luma, (byte)235);
        GrayScaler.FromLuma(luma, 320, 180, 320, fullRange: false, frame);
        Assert.All(frame.Pixels, p => Assert.Equal(255, p));
    }

    [Fact]
    public void Full_range_luma_is_kept_and_halves_stay_apart()
    {
        var frame = GrayScaler.Create();
        var luma = new byte[640 * 360];
        for (var y = 0; y < 360; y++)
            for (var x = 320; x < 640; x++) luma[y * 640 + x] = 200;
        GrayScaler.FromLuma(luma, 640, 360, 640, fullRange: true, frame);
        Assert.Equal(0, frame.Pixels[10 * 160 + 10]);
        Assert.Equal(200, frame.Pixels[10 * 160 + 150]);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Centinela.Media.Tests` and `dotnet test tests/Centinela.Core.Tests --filter FullyQualifiedName~GrayScalerTests`
Expected: FAIL to compile (`FrameLayout`, `YuvToRgb`, `FrameOutput`, `GpuPainting`, `FromLuma` missing).

- [ ] **Step 3: Write the implementation**

`src/Centinela.Media/VideoFrame.cs` (the `VideoFrame` class is removed from `FrameMailbox.cs`):

```csharp
namespace Centinela.Media;

/// <summary>How a mailbox frame stores its pixels.</summary>
public enum FrameLayout
{
    /// <summary>BGRA, 4 bytes per pixel: painted by the processor as a bitmap.</summary>
    Bgra,
    /// <summary>NV12: the Y plane, then U/V pairs at half size; the graphics card turns it into RGB.</summary>
    Nv12,
}

/// <summary>A decoded image in a mailbox, rows packed tight. NV12 frames also say which colours they use.</summary>
public sealed class VideoFrame
{
    public VideoFrame(int width, int height, FrameLayout layout = FrameLayout.Bgra)
    {
        Width = width;
        Height = height;
        Layout = layout;
        Stride = layout == FrameLayout.Bgra ? width * 4 : width;
        ChromaWidth = (width + 1) / 2;
        ChromaHeight = (height + 1) / 2;
        Data = new byte[layout == FrameLayout.Bgra ? Stride * height : width * height + ChromaWidth * 2 * ChromaHeight];
    }

    public int Width { get; }
    public int Height { get; }
    public FrameLayout Layout { get; }
    /// <summary>Bytes per row of the BGRA image, or of the NV12 Y plane.</summary>
    public int Stride { get; }
    /// <summary>NV12: U/V pairs per row (each row is ChromaWidth·2 bytes) and rows of the U/V plane.</summary>
    public int ChromaWidth { get; }
    public int ChromaHeight { get; }
    /// <summary>NV12: where the U/V plane starts in <see cref="Data"/>.</summary>
    public int ChromaOffset => Width * Height;
    public byte[] Data { get; }
    /// <summary>NV12: the colours to turn it into RGB with; the producer sets them for every frame it publishes.</summary>
    public YuvMatrix Matrix { get; set; }
    public bool FullRange { get; set; }
}
```

`src/Centinela.Media/FrameMailbox.cs`: delete the `VideoFrame` class (and its summary) from the top of the file and change `Rent`:

```csharp
    public VideoFrame Rent(int width, int height, FrameLayout layout = FrameLayout.Bgra)
    {
        lock (_gate)
        {
            var frame = _spare;
            _spare = null;
            return frame is not null && frame.Width == width && frame.Height == height && frame.Layout == layout
                ? frame
                : new VideoFrame(width, height, layout);
        }
    }
```

`src/Centinela.Media/FrameOutput.cs`:

```csharp
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
```

`src/Centinela.Media/YuvToRgb.cs`:

```csharp
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
```

`src/Centinela.Media/FrameConverter.cs`: `Convert` writes BGRA or NV12 by `target.Layout`. Replace from `ColorTags.Apply(src);` … to the end of the `fixed` block with:

```csharp
        var tags = ColorTags.Apply(src);
        ... (context creation unchanged)
        if (_dst is null) _dst = ffmpeg.av_frame_alloc();
        _dst->width = target.Width;
        _dst->height = target.Height;
        // Same primaries and transfer as the source: only the matrix is converted, never the gamut.
        _dst->color_primaries = src->color_primaries;
        _dst->color_trc = src->color_trc;
        if (target.Layout == FrameLayout.Nv12)
        {
            // Scaled only, same matrix and range: the graphics card converts to RGB with them.
            _dst->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
            _dst->colorspace = src->colorspace;
            _dst->color_range = src->color_range;
            target.Matrix = tags.Matrix;
            target.FullRange = tags.FullRange;
        }
        else
        {
            _dst->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
            _dst->colorspace = AVColorSpace.AVCOL_SPC_RGB;
            _dst->color_range = AVColorRange.AVCOL_RANGE_JPEG;
        }
        int err;
        fixed (byte* data = target.Data)
        {
            _dst->data[0] = data;
            _dst->linesize[0] = target.Stride;
            if (target.Layout == FrameLayout.Nv12)
            {
                _dst->data[1] = data + target.ChromaOffset;
                _dst->linesize[1] = target.ChromaWidth * 2;
            }
            err = ffmpeg.sws_scale_frame(_sws, _dst, src);
            _dst->data[0] = null; // managed buffer: never left behind in the frame
            _dst->data[1] = null;
            _dst->linesize[1] = 0;
        }
        FFmpegException.ThrowIfError(err, "scale");
```

Update the class summary: "Turns a decoded frame into a mailbox frame (BGRA, or NV12 for the graphics card) with swscale: …".

`src/Centinela.Media/StreamStats.cs`: add `bool GpuPainting = false` after `ConvertMs`, and to the summary: "and whether its frames are NV12 for the graphics card to paint".

`src/Centinela.Media/StreamSession.cs`:
- `PresentFrame`: `var target = Mailbox.Rent(width, height, FrameOutput.Layout);` and `UpdateStats(hardware, convertMs, target.Layout == FrameLayout.Nv12);`
- `UpdateStats(bool hardware, double convertMs, bool gpuPainting)` passes `gpuPainting` as the last `StreamStats` argument.
- Class summary: "Decoding sessions push the newest frame (scaled to the target size, BGRA or NV12 by <see cref="FrameOutput"/>) into <see cref="Mailbox"/>; …".

`src/Centinela.Core/Motion/GrayFrame.cs`: add to `GrayScaler`:

```csharp
    /// <summary>
    /// Downscales the Y plane of a YUV image into <paramref name="target"/> (160×90) like <see cref="FromBgra"/>.
    /// Limited-range luma (16–235) is stretched to 0–255, so motion thresholds mean the same for both layouts.
    /// </summary>
    public static void FromLuma(ReadOnlySpan<byte> luma, int width, int height, int stride, bool fullRange, GrayFrame target)
    {
        var pixels = target.Pixels;
        for (var ty = 0; ty < target.Height; ty++)
        {
            var sy = Math.Min(height - 1, (int)((ty + 0.5) * height / target.Height));
            var row = sy * stride;
            for (var tx = 0; tx < target.Width; tx++)
            {
                var sx = Math.Min(width - 1, (int)((tx + 0.5) * width / target.Width));
                var y = luma[row + sx];
                pixels[ty * target.Width + tx] = fullRange ? y : (byte)Math.Clamp((y - 16) * 255 / 219, 0, 255);
            }
        }
    }
```

`src/Centinela.Desktop/Services/Motion/MotionService.cs` (the `TryRead` in `Process`):

```csharp
                var read = session.Mailbox.TryRead(ref monitor.Sequence, f =>
                {
                    if (f.Layout == FrameLayout.Nv12) GrayScaler.FromLuma(f.Data, f.Width, f.Height, f.Stride, f.FullRange, gray);
                    else GrayScaler.FromBgra(f.Data, f.Width, f.Height, f.Stride, gray);
                });
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Centinela.Media.Tests`, `dotnet test tests/Centinela.Core.Tests`, and `dotnet test tests/Centinela.Desktop.Tests -p:BaseOutputPath=<scratch bin>`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Media/VideoFrame.cs src/Centinela.Media/FrameOutput.cs src/Centinela.Media/YuvToRgb.cs src/Centinela.Media/FrameMailbox.cs src/Centinela.Media/FrameConverter.cs src/Centinela.Media/StreamSession.cs src/Centinela.Media/StreamStats.cs src/Centinela.Core/Motion/GrayFrame.cs src/Centinela.Desktop/Services/Motion/MotionService.cs tests/Centinela.Media.Tests/FrameMailboxTests.cs tests/Centinela.Media.Tests/FrameConverterTests.cs tests/Centinela.Media.Tests/YuvToRgbTests.cs tests/Centinela.Media.Tests/StreamSessionTests.cs tests/Centinela.Core.Tests/GrayScalerTests.cs
git commit -m "feat: imágenes NV12 para pintarlas con la tarjeta gráfica"
```

---

### Task 3: Pintor OpenGL

**Files:**
- Create: `src/Centinela.Desktop/Video/GpuVideo.cs`
- Create: `src/Centinela.Desktop/Video/GlShaders.cs`
- Create: `src/Centinela.Desktop/Video/VideoQuad.cs`
- Create: `src/Centinela.Desktop/Video/GlVideoView.cs`
- Modify: `src/Centinela.Desktop/Video/VideoSurface.cs` (only the two internal members `GlVideoView` needs, so this task compiles; Task 4 does the rest)
- Test: `tests/Centinela.Desktop.Tests/GpuVideoTests.cs`, `tests/Centinela.Desktop.Tests/GlShadersTests.cs`, `tests/Centinela.Desktop.Tests/VideoQuadTests.cs`

**Interfaces:**
- Consumes (Task 2): `VideoFrame` (`Layout`, `Width`, `Height`, `ChromaWidth`, `ChromaHeight`, `ChromaOffset`, `Data`, `Matrix`, `FullRange`), `FrameLayout`, `FrameOutput.Layout`, `YuvToRgb.Matrix`, `YuvMatrix`.
- Produces: `static class GpuVideo` with `enum Mode { Undecided, On, Off }`, `Mode State`, `event Action? TurnedOff`, `void TurnOn()`, `void TurnOff()`, `bool IsSoftwareRenderer(string? renderer)`; `static class GlShaders` with `string? Header(GlProfileType type, int major, int minor)`, `const string Vertex`, `Nv12`, `Bgra`; `static class VideoQuad` with `float[] Vertices(Rect content, Size view)`; `sealed class GlVideoView : OpenGlControlBase` with `GlVideoView(VideoSurface owner)` and `bool Ready`. `GlVideoView` calls on its owner: `internal bool TryReadFrame(Action<VideoFrame> upload)`, `internal Rect DrawRect`, `internal void OnGlReady()`. This task adds them to `VideoSurface` as stubs that compile (`TryReadFrame` returns `false`, `DrawRect` returns `ContentRect`, `OnGlReady` does nothing); Task 4 gives them their real bodies.

- [ ] **Step 1: Write the failing tests**

`tests/Centinela.Desktop.Tests/GpuVideoTests.cs`:

```csharp
using Centinela.Desktop.Video;

namespace Centinela.Desktop.Tests;

public class GpuVideoTests
{
    [Theory]
    [InlineData("llvmpipe (LLVM 15.0.7, 256 bits)")]
    [InlineData("ANGLE (Microsoft, Microsoft Basic Render Driver Direct3D11 vs_5_0 ps_5_0, D3D11)")]
    [InlineData("ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device (Subzero) (0x0000C0DE)), SwiftShader driver)")]
    [InlineData("Apple Software Renderer")]
    [InlineData("softpipe")]
    [InlineData(null)]
    public void Software_renderers_do_not_count(string? renderer) => Assert.True(GpuVideo.IsSoftwareRenderer(renderer));

    [Theory]
    [InlineData("ANGLE (NVIDIA, NVIDIA GeForce RTX 3070 Direct3D11 vs_5_0 ps_5_0, D3D11)")]
    [InlineData("ANGLE (Intel, Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0, D3D11)")]
    [InlineData("Apple M4 Pro")]
    [InlineData("AMD Radeon Graphics (radeonsi, renoir, LLVM 15.0.7, DRM 3.49, 6.1.0)")]
    [InlineData("Mesa Intel(R) UHD Graphics 620 (KBL GT2)")]
    [InlineData("SVGA3D; build: RELEASE; LLVM;")]
    public void Graphics_cards_count(string renderer) => Assert.False(GpuVideo.IsSoftwareRenderer(renderer));
}
```

`tests/Centinela.Desktop.Tests/GlShadersTests.cs`:

```csharp
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
```

`tests/Centinela.Desktop.Tests/VideoQuadTests.cs`:

```csharp
using Avalonia;
using Centinela.Desktop.Video;

namespace Centinela.Desktop.Tests;

public class VideoQuadTests
{
    [Fact]
    public void Image_filling_the_view_covers_clip_space_top_row_first()
    {
        Assert.Equal(
            new float[] { -1, 1, 0, 0, -1, -1, 0, 1, 1, 1, 1, 0, 1, -1, 1, 1 },
            VideoQuad.Vertices(new Rect(0, 0, 800, 450), new Size(800, 450)));
    }

    [Fact]
    public void Letterboxed_image_sits_in_the_middle()
    {
        var v = VideoQuad.Vertices(new Rect(100, 0, 600, 450), new Size(800, 450));
        Assert.Equal((-0.75f, 1f), (v[0], v[1]));    // top-left
        Assert.Equal((0.75f, -1f), (v[12], v[13]));  // bottom-right
    }

    [Fact]
    public void Zoomed_image_reaches_past_the_view()
    {
        var v = VideoQuad.Vertices(new Rect(-800, -450, 1600, 900), new Size(800, 450));
        Assert.Equal((-3f, 3f), (v[0], v[1]));
        Assert.Equal((1f, -1f), (v[12], v[13]));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Centinela.Desktop.Tests -p:BaseOutputPath=<scratch bin> --filter "FullyQualifiedName~GpuVideoTests|FullyQualifiedName~GlShadersTests|FullyQualifiedName~VideoQuadTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Write the implementation**

`src/Centinela.Desktop/Video/GpuVideo.cs`:

```csharp
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
```

`src/Centinela.Desktop/Video/GlShaders.cs`:

```csharp
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
```

`src/Centinela.Desktop/Video/VideoQuad.cs`:

```csharp
using Avalonia;

namespace Centinela.Desktop.Video;

/// <summary>Where the video is drawn: the 4 vertices (x, y, s, t) of a triangle strip in OpenGL clip space.</summary>
static class VideoQuad
{
    /// <param name="content">The image's rectangle in the view, in DIPs (letterboxed, then zoomed).</param>
    /// <param name="view">The view's size in DIPs.</param>
    public static float[] Vertices(Rect content, Size view)
    {
        float X(double x) => (float)(2 * x / view.Width - 1);
        float Y(double y) => (float)(1 - 2 * y / view.Height); // DIPs grow downwards, clip space upwards
        // Top-left, bottom-left, top-right, bottom-right; texture row 0 is the image's top row.
        return
        [
            X(content.Left), Y(content.Top), 0, 0,
            X(content.Left), Y(content.Bottom), 0, 1,
            X(content.Right), Y(content.Top), 1, 0,
            X(content.Right), Y(content.Bottom), 1, 1,
        ];
    }
}
```

`src/Centinela.Desktop/Video/GlVideoView.cs`:

```csharp
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
        if (!Ready || _nv12 is null || _bgra is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        try
        {
            gl.BindFramebuffer(GL_FRAMEBUFFER, fb);
            var scaling = VisualRoot?.RenderScaling ?? 1;
            gl.Viewport(0, 0, Math.Max(1, (int)(Bounds.Width * scaling)), Math.Max(1, (int)(Bounds.Height * scaling)));
            gl.Disable(GL_BLEND);
            gl.Disable(GL_DEPTH_TEST);
            gl.Disable(GL_SCISSOR_TEST);
            gl.Disable(GL_CULL_FACE);
            // Transparent: before the first frame the processor's image shows through; around the image, the tile.
            gl.ClearColor(0, 0, 0, 0);
            gl.Clear(GL_COLOR_BUFFER_BIT);
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
```

`src/Centinela.Desktop/Video/VideoSurface.cs`: add these stubs (Task 4 replaces them) so the project compiles:

```csharp
    /// <summary>Where the image is drawn (Task 4: moved by the zoom).</summary>
    internal Rect DrawRect => ContentRect;

    /// <summary>For <see cref="GlVideoView"/> (Task 4).</summary>
    internal bool TryReadFrame(Action<VideoFrame> upload) => false;

    /// <summary>Called by the OpenGL child once it can paint (Task 4).</summary>
    internal void OnGlReady() { }
```

- [ ] **Step 4: Build and run the tests**

Run: `dotnet build src/Centinela.Desktop -p:BaseOutputPath=<scratch bin>` (zero warnings) and `dotnet test tests/Centinela.Desktop.Tests -p:BaseOutputPath=<scratch bin>`
Expected: PASS. `GlVideoView` is not used yet: the app paints exactly as before.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Desktop/Video/GpuVideo.cs src/Centinela.Desktop/Video/GlShaders.cs src/Centinela.Desktop/Video/VideoQuad.cs src/Centinela.Desktop/Video/GlVideoView.cs src/Centinela.Desktop/Video/VideoSurface.cs tests/Centinela.Desktop.Tests/GpuVideoTests.cs tests/Centinela.Desktop.Tests/GlShadersTests.cs tests/Centinela.Desktop.Tests/VideoQuadTests.cs
git commit -m "feat: pintor de vídeo con OpenGL"
```

---

### Task 4: Pintar con la tarjeta gráfica

**Files:**
- Modify: `src/Centinela.Desktop/Video/VideoSurface.cs` (whole file below)
- Modify: `src/Centinela.Desktop/Video/ZoomPanBehavior.cs` (the zoom goes through `VideoSurface.SetZoom`)
- Modify: `src/Centinela.Desktop/ViewModels/TileViewModel.cs` (`RefreshStats`)
- Modify: `src/Centinela.Desktop/Services/Info/ManualDocument.cs` («Estadísticas»)
- Create: `docs/prueba-video-v1.4.md`
- Test: `tests/Centinela.Desktop.Tests/ZoomTests.cs` (existing, must still pass), `tests/Centinela.Desktop.Tests/InfoDocumentsTests.cs` (existing, must still pass)

**Interfaces:**
- Consumes (Tasks 1–3): `GlVideoView(VideoSurface)`, `GlVideoView.Ready`, `GlVideoView.RequestNextFrameRendering()`, `GpuVideo.State/TurnOff/TurnedOff/Mode`, `FrameLayout`, `VideoFrame`, `StreamStats.ConvertMs`, `StreamStats.GpuPainting`.
- Produces: `VideoSurface.SetZoom(Matrix? zoom)`; real `TryReadFrame`, `DrawRect`, `OnGlReady`.

- [ ] **Step 1: VideoSurface hosts the OpenGL child**

`src/Centinela.Desktop/Video/VideoSurface.cs` (whole file):

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Centinela.Media;

namespace Centinela.Desktop.Video;

/// <summary>
/// Paints the newest frame of a session's mailbox on every screen refresh: latest wins, nothing is queued, so no
/// latency accumulates (same rule as the Windows version). Letterboxed, centred. While OpenGL works its
/// <see cref="GlVideoView"/> child paints (and zooms); otherwise BGRA frames are copied into a bitmap
/// (<see cref="GpuVideo"/>).
/// </summary>
sealed class VideoSurface : Control
{
    // A view whose OpenGL never starts while NV12 frames arrive sends everybody back to the processor after this.
    static readonly TimeSpan GlStartTimeout = TimeSpan.FromSeconds(3);

    // Updated in place: panning must not allocate on every pointer move.
    readonly MatrixTransform _transform = new();
    WriteableBitmap? _bitmap;
    GlVideoView? _gl;
    PixelSize _frameSize;
    Matrix? _zoom;
    long _waitingForGl; // Stopwatch ticks of the first NV12 frame this view could not paint; 0 = none
    long _sequence;
    bool _attached;
    bool _firstFrame;
    // One animation-frame request at a time; a request made before the last detach is stale (its generation changed).
    bool _frameRequested;
    int _generation;

    StreamSession? _session;

    /// <summary>The session to paint; a new one starts reading its mailbox from the beginning.</summary>
    public StreamSession? Session
    {
        get => _session;
        set
        {
            if (value == _session) return;
            _session = value;
            _sequence = 0;
            RequestFrame();
        }
    }

    /// <summary>Raised once, after the first frame has been painted.</summary>
    public event Action? FirstFrameShown;

    /// <summary>Where the image sits inside the control (letterboxed, before the zoom); empty until the first frame.</summary>
    public Rect ContentRect { get; private set; }

    /// <summary>Raised when <see cref="ContentRect"/> changes (control resized or new frame size).</summary>
    public event Action? ContentRectChanged;

    public VideoSurface()
    {
        // The zoom matrix is built for the top-left corner; Avalonia centres render transforms by default.
        RenderTransformOrigin = RelativePoint.TopLeft;
        SizeChanged += (_, _) => UpdateContentRect();
        if (GpuVideo.State != GpuVideo.Mode.Off) AddGl();
    }

    /// <summary>Where OpenGL draws the image: <see cref="ContentRect"/> moved by the zoom.</summary>
    internal Rect DrawRect => _zoom is { } zoom ? ContentRect.TransformToAABB(zoom) : ContentRect;

    /// <summary>The digital zoom in surface coordinates (top-left origin), or null for the whole image.</summary>
    public void SetZoom(Matrix? zoom)
    {
        _zoom = zoom;
        ApplyZoom();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        GpuVideo.TurnedOff += RemoveGl;
        if (GpuVideo.State == GpuVideo.Mode.Off) RemoveGl();
        ApplyZoom();
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _frameRequested = false;
        _generation++;
        GpuVideo.TurnedOff -= RemoveGl;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Called by the OpenGL child once it can paint: the zoom moves into its drawing.</summary>
    internal void OnGlReady()
    {
        _waitingForGl = 0;
        ApplyZoom();
    }

    /// <summary>For <see cref="GlVideoView"/>: hands it the newest frame not yet shown; false when there is none.</summary>
    internal bool TryReadFrame(Action<VideoFrame> upload)
    {
        if (_session is not { } session) return false;
        var size = default(PixelSize);
        if (!session.Mailbox.TryRead(ref _sequence, frame =>
            {
                size = new PixelSize(frame.Width, frame.Height);
                upload(frame);
            }))
            return false;
        if (_bitmap is not null)
        {
            // OpenGL paints now: the processor's last image must not linger underneath.
            _bitmap.Dispose();
            _bitmap = null;
            InvalidateVisual();
        }
        Shown(size);
        return true;
    }

    void AddGl()
    {
        _gl = new GlVideoView(this);
        VisualChildren.Add(_gl);
        LogicalChildren.Add(_gl);
    }

    void RemoveGl()
    {
        if (_gl is not { } gl) return;
        _gl = null;
        VisualChildren.Remove(gl);
        LogicalChildren.Remove(gl);
        ApplyZoom();
    }

    /// <summary>
    /// A painting OpenGL child draws the zoom itself, at the decoded resolution; a render transform would enlarge its
    /// already-drawn pixels. The bitmap is zoomed with the render transform, as before.
    /// </summary>
    void ApplyZoom()
    {
        if (_zoom is { } zoom && _gl is not { Ready: true })
        {
            _transform.Matrix = zoom;
            if (!ReferenceEquals(RenderTransform, _transform)) RenderTransform = _transform;
        }
        else
        {
            RenderTransform = null;
        }
        _gl?.RequestNextFrameRendering();
    }

    /// <summary>Asks for the next screen refresh; not while detached, without a session or with a request pending.</summary>
    void RequestFrame()
    {
        if (!_attached || _session is null || _frameRequested) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        _frameRequested = true;
        var generation = _generation;
        topLevel.RequestAnimationFrame(_ => OnFrame(generation));
    }

    void OnFrame(int generation)
    {
        // Requested before a detach: the current placement (if any) has its own request.
        if (generation != _generation) return;
        _frameRequested = false;
        if (!_attached) return;
        if (_session is { } session)
        {
            if (_gl is { Ready: true } gl)
            {
                if (session.Mailbox.Sequence != _sequence) gl.RequestNextFrameRendering();
            }
            else
            {
                PaintOnProcessor(session);
            }
        }
        RequestFrame();
    }

    void PaintOnProcessor(StreamSession session)
    {
        var size = default(PixelSize);
        var needsGl = false;
        if (!session.Mailbox.TryRead(ref _sequence, frame =>
            {
                if (frame.Layout != FrameLayout.Bgra)
                {
                    needsGl = true;
                    return;
                }
                CopyFrame(frame);
                size = new PixelSize(frame.Width, frame.Height);
            }))
            return;
        if (needsGl)
        {
            // NV12 needs this view's OpenGL, which is still starting; if it never does, everybody uses the processor.
            if (_waitingForGl == 0) _waitingForGl = Stopwatch.GetTimestamp();
            else if (Stopwatch.GetElapsedTime(_waitingForGl) > GlStartTimeout) GpuVideo.TurnOff();
            return;
        }
        _waitingForGl = 0;
        InvalidateVisual();
        Shown(size);
    }

    void Shown(PixelSize size)
    {
        _frameSize = size;
        UpdateContentRect();
        if (_firstFrame) return;
        _firstFrame = true;
        FirstFrameShown?.Invoke();
    }

    void CopyFrame(VideoFrame frame)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using var target = _bitmap.Lock();
        if (target.RowBytes == frame.Stride)
        {
            Marshal.Copy(frame.Data, 0, target.Address, frame.Stride * frame.Height);
            return;
        }
        for (var row = 0; row < frame.Height; row++)
            Marshal.Copy(frame.Data, row * frame.Stride, target.Address + row * target.RowBytes, frame.Stride);
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is not { } bitmap) return;
        var source = new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        context.DrawImage(bitmap, source, Letterbox(source.Size, Bounds.Size));
    }

    void UpdateContentRect()
    {
        var rect = _frameSize.Width > 0
            ? Letterbox(new Size(_frameSize.Width, _frameSize.Height), Bounds.Size)
            : default;
        if (rect == ContentRect) return;
        ContentRect = rect;
        ContentRectChanged?.Invoke();
    }

    /// <summary>The largest rectangle with the image's aspect ratio that fits the control, centred.</summary>
    static Rect Letterbox(Size image, Size bounds)
    {
        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        var size = new Size(image.Width * scale, image.Height * scale);
        return new Rect(new Point((bounds.Width - size.Width) / 2, (bounds.Height - size.Height) / 2), size);
    }
}
```

- [ ] **Step 2: ZoomPanBehavior hands the zoom to the surface**

`src/Centinela.Desktop/Video/ZoomPanBehavior.cs`: delete the `_transform` field and the `RenderTransformOrigin` line (now in `VideoSurface`); `Apply` becomes:

```csharp
    /// <summary>Shows the zoom state: the surface's zoom and scaling quality, then raises <see cref="Changed"/>.</summary>
    public void Apply()
    {
        var o = Sync();
        _video.SetZoom(_zoom.IsZoomed ? ZoomMatrix(_zoom.Scale, _zoom.OffsetX, _zoom.OffsetY, o) : null);
        RenderOptions.SetBitmapInterpolationMode(_video,
            _zoom.IsZoomed ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.MediumQuality);
        Changed?.Invoke();
    }
```

Update `ZoomMatrix`'s summary from «The surface's render transform» to «The surface's zoom matrix». Remove `using Avalonia.Media.Imaging;` only if it becomes unused (it is still used by `BitmapInterpolationMode`).

- [ ] **Step 3: Statistics line and manual**

`src/Centinela.Desktop/ViewModels/TileViewModel.cs`, in `RefreshStats` (summary «fps · ms · GPU/CPU (and smoothing)» becomes «fps · ms · decoder · conversion · painter (and smoothing)»):

```csharp
        var s = Session.Stats;
        StatsText = $"{s.Fps:0} fps · {s.LatencyMs:0} ms · {(s.HardwareDecoding ? "GPU" : "CPU")} · conv. {s.ConvertMs:0.0} ms · pintado {(s.GpuPainting ? "GPU" : "CPU")}"
            + (s.SmoothingSeconds > 0
                ? string.Create(CultureInfo.InvariantCulture, $" · suav. {s.SmoothingSeconds:0.##} s · cortes {s.Rebuffers}")
                : "");
```

`src/Centinela.Desktop/Services/Info/ManualDocument.cs`, section «Estadísticas»: keep the fps, ms and camera-clock paragraphs; replace the GPU/CPU paragraph with these three, in this order, before the camera-clock paragraph:

```csharp
        doc.Add(new InfoParagraph("GPU/CPU: si el vídeo se descodifica con la tarjeta gráfica (GPU, consume menos) o con el procesador (CPU)."));
        doc.Add(new InfoParagraph("conv.: lo que tarda el procesador en preparar cada imagen (reducirla al tamaño del recuadro y, si pinta el procesador, pasarla a color RGB)."));
        doc.Add(new InfoParagraph("pintado GPU/CPU: si la imagen se pinta con la tarjeta gráfica (OpenGL, consume menos) o con el procesador. Si la tarjeta gráfica no puede, Centinela pinta con el procesador sin avisar."));
```

- [ ] **Step 4: Test guide for the user**

`docs/prueba-video-v1.4.md`:

```markdown
# Prueba de las mejoras de vídeo (v1.4)

Qué comprobar en Windows, macOS y Linux después de las mejoras de vídeo. Activa Ver › Mostrar estadísticas para ver la línea `fps · ms · GPU · conv. … ms · pintado …`.

## 1. Pintado con la tarjeta gráfica

- [ ] La línea de estadísticas dice `pintado GPU` en todas las cámaras.
- [ ] La imagen se ve derecha (no al revés ni en espejo), sin bandas, sin colores cambiados y sin inclinarse.
- [ ] Al cambiar de vista (cuadrícula, cámara grande, pantalla completa) las cámaras vuelven a verse en menos de un segundo.
- [ ] El zoom con la rueda y el arrastre funcionan como antes, y con zoom la imagen se ve **más nítida** que en la 1.3.
- [ ] Al pasar el ratón por encima siguen saliendo los botones de la cámara, y el clic, el doble clic y arrastrar para cambiar de sitio funcionan.
- [ ] En la ventana «Añadir cámara», la vista previa se ve bien.

Para comparar con el pintado de antes, arranca Centinela con la variable `CENTINELA_CPU_VIDEO=1` (la línea dirá `pintado CPU`):

- Windows (PowerShell): `$env:CENTINELA_CPU_VIDEO=1; & "C:\ruta\a\Centinela.exe"`
- macOS: `CENTINELA_CPU_VIDEO=1 /Applications/Centinela.app/Contents/MacOS/Centinela`
- Linux: `CENTINELA_CPU_VIDEO=1 centinela`

- [ ] Con las mismas cámaras abiertas, el uso del procesador (Administrador de tareas, Monitor de Actividad o `top`) es menor con `pintado GPU` que con `pintado CPU`.

## 2. Colores

- [ ] Los colores se parecen más a los de la web de la cámara (o a VLC) que en la 1.3: sobre todo rojos y verdes en las cámaras HD.
- [ ] Las capturas (📷) tienen los mismos colores que la imagen en directo.

## 3. Rendimiento

- [ ] El número de `conv.` baja respecto a la versión anterior en la cámara grande y en pantalla completa (se reparte en varios núcleos).
- [ ] La cámara grande y la pantalla completa se ven algo más nítidas (filtro bicúbico).

## 4. Cámaras por UDP

- [ ] En las cámaras configuradas con UDP aparecen menos manchas grises o imágenes rotas al llegar las imágenes completas.

## 5. Sin cambios

- [ ] La detección de movimiento avisa igual que antes (marco rojo y registro).
- [ ] Grabar, el sonido y «Suavizar imagen» funcionan igual.

Si algo falla con `pintado GPU`, apunta el sistema, la tarjeta gráfica y qué se ve; con `CENTINELA_CPU_VIDEO=1` Centinela funciona como antes mientras se corrige.
```

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build Centinela.slnx -p:BaseOutputPath=<scratch bin>` (zero warnings); `dotnet test tests/Centinela.Desktop.Tests -p:BaseOutputPath=<scratch bin>`; `dotnet test tests/Centinela.Core.Tests`; `dotnet test tests/Centinela.Media.Tests`; `dotnet test tests/Centinela.Platform.Tests`.
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Centinela.Desktop/Video/VideoSurface.cs src/Centinela.Desktop/Video/ZoomPanBehavior.cs src/Centinela.Desktop/ViewModels/TileViewModel.cs src/Centinela.Desktop/Services/Info/ManualDocument.cs docs/prueba-video-v1.4.md
git commit -m "feat: pintar el vídeo con la tarjeta gráfica y zoom nítido"
```
