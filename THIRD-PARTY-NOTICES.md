# Third-party software

Centinela's packages (the Windows installer, the macOS `.dmg` and the Linux `.deb`) are self-contained: they bundle
the .NET runtime and the components below, each under its own license. This file and Centinela's own license
(`LICENSE.txt`, MIT) are installed next to the program.

## FFmpeg (LGPL)

Centinela loads FFmpeg (https://ffmpeg.org) as separate shared libraries from the `ffmpeg` folder next to the
program: `ffmpeg\*.dll` on Windows, `/opt/centinela/ffmpeg/*.so.*` on Linux and
`Centinela.app/Contents/MacOS/ffmpeg/*.dylib` on macOS. The full license text is in `ffmpeg/LICENSE.txt`.

- **Windows and Linux:** the LGPL "shared" builds of FFmpeg 9.0 from https://github.com/BtbN/FFmpeg-Builds
  (downloaded by `tools/get-ffmpeg.ps1` and `tools/get-ffmpeg.sh`), under the **LGPL v3**. That project publishes
  their exact sources and build scripts.
- **macOS:** built from the FFmpeg `n9.0.2` source (https://github.com/FFmpeg/FFmpeg/tree/n9.0.2) by
  `tools/get-ffmpeg-mac.sh`, which holds the exact configuration (no GPL or nonfree parts), under the
  **LGPL v2.1 or later**.

FFmpeg's source code is available at https://ffmpeg.org/download.html. You may replace the libraries with your own
compatible build of FFmpeg 9.0 (libavcodec 63, libavformat 63, libavutil 61, libavfilter 12, libavdevice 63,
libswscale 10, libswresample 7).

## OpenAL Soft (LGPL v2 or later)

Sound is played with OpenAL Soft 1.23.1 (https://openal-soft.org), a separate shared library: `soft_oal.dll` on
Windows, `libopenal.so` on Linux and `libopenal.dylib` on macOS. It comes from the `Silk.NET.OpenAL.Soft.Native`
package (https://github.com/dotnet/Silk.NET) under the **LGPL-2.0-or-later**. Source code:
https://github.com/kcat/openal-soft/tree/1.23.1; full license text:
https://github.com/kcat/openal-soft/blob/1.23.1/COPYING. You may replace the library with your own compatible build.

## Components under the MIT License

| Component | Version | Source |
|---|---|---|
| .NET runtime | 10.0 | https://github.com/dotnet/runtime (its own notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT) |
| Avalonia | 11.3.22 | https://github.com/AvaloniaUI/Avalonia |
| CommunityToolkit.Mvvm | 8.4.2 | https://github.com/CommunityToolkit/dotnet |
| FFmpeg.AutoGen | 9.0.1.1 | https://github.com/Ruslan-B/FFmpeg.AutoGen |
| Silk.NET (Silk.NET.OpenAL, Silk.NET.Core) | 2.23.0 | https://github.com/dotnet/Silk.NET |
| SkiaSharp | 2.88.9 | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp | 8.3.1.1 | https://github.com/mono/SkiaSharp |
| MicroCom.Runtime | 0.11.0 | https://github.com/kekekeks/MicroCom |
| Tmds.DBus.Protocol | 0.21.3 | https://github.com/tmds/Tmds.DBus |
| System.Security.Cryptography.ProtectedData | 10.0.12 | https://github.com/dotnet/runtime |

Each is copyright its authors, as stated in its repository, and distributed under these terms:

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and
to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions
of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO
THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

## Skia and HarfBuzz

SkiaSharp and HarfBuzzSharp bundle the native Skia (BSD-3-Clause, https://skia.org) and HarfBuzz (MIT,
https://github.com/harfbuzz/harfbuzz) libraries (`libSkiaSharp` and `libHarfBuzzSharp`), which in turn include other
open-source code. Their complete notices:
https://github.com/mono/SkiaSharp/blob/v2.88.9/THIRD-PARTY-NOTICES.txt

## ANGLE (Windows only)

Avalonia draws through ANGLE on Windows (`av_libglesv2.dll`, from the `Avalonia.Angle.Windows.Natives` package):

```
Copyright 2018 The ANGLE Project Authors.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

    Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

    Redistributions in binary form must reproduce the above
    copyright notice, this list of conditions and the following
    disclaimer in the documentation and/or other materials provided
    with the distribution.

    Neither the name of TransGaming Inc., Google Inc., 3DLabs Inc.
    Ltd., nor the names of their contributors may be used to endorse
    or promote products derived from this software without specific
    prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

## mediamtx (tests only, not distributed)

MIT License: https://github.com/bluenviron/mediamtx
