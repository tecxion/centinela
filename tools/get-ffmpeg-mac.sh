#!/usr/bin/env bash
# Builds FFmpeg n9.0.2 (LGPL, shared, VideoToolbox) for macOS into <repo>/ffmpeg/osx-<arm64|x64>/lib.
# Usage: tools/get-ffmpeg-mac.sh arm64|x64   (on a Mac with the Xcode command line tools; x64 also needs nasm)
# Counterpart of tools/get-ffmpeg.sh (same FFmpeg version); the libraries load from @loader_path.
set -euo pipefail

case "${1:-}" in
    arm64) arch=arm64; cpu=arm64 ;;
    x64) arch=x64; cpu=x86_64 ;;
    *) echo "Uso: tools/get-ffmpeg-mac.sh arm64|x64" >&2; exit 1 ;;
esac

for tool in git make clang; do
    command -v "$tool" >/dev/null 2>&1 || { echo "Falta '$tool'. Instálala y vuelve a intentarlo." >&2; exit 1; }
done
if [ "$arch" = x64 ]; then
    command -v nasm >/dev/null 2>&1 || { echo "Falta 'nasm'. Instálala (brew install nasm) y vuelve a intentarlo." >&2; exit 1; }
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dest="$root/ffmpeg/osx-$arch"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

git -c advice.detachedHead=false clone --depth 1 --branch n9.0.2 https://github.com/FFmpeg/FFmpeg.git "$tmp/src"
if [ "$(git -C "$tmp/src" rev-parse HEAD)" != 946fcce07b6dcd0331c8cc609192aeff5e1924f8 ]; then
    echo "El código de FFmpeg descargado no coincide con n9.0.2." >&2
    exit 1
fi

# Building for the other CPU (x64 on Apple Silicon) is a cross build.
cross=()
if [ "$cpu" != "$(uname -m)" ]; then
    cross=(--enable-cross-compile --arch="$cpu" --target-os=darwin)
fi
flags="-arch $cpu -mmacosx-version-min=14.0"

cd "$tmp/src"
# --disable-autodetect keeps out Homebrew libraries the user's Mac does not have; it also drops threads,
# hence --enable-pthreads. No iconv: on macOS it is a separate libiconv that libavcodec fails to link, and it only
# converts subtitle charsets, which Centinela never decodes.
./configure \
    --prefix="$tmp/out" --enable-shared --disable-static --disable-programs --disable-doc --disable-debug \
    --disable-autodetect --enable-pthreads --enable-videotoolbox --enable-audiotoolbox \
    --enable-zlib --enable-bzlib \
    --install-name-dir=@loader_path \
    --extra-cflags="$flags" --extra-ldflags="$flags" \
    ${cross[@]+"${cross[@]}"}
make -j"$(sysctl -n hw.ncpu)"
make install

# Each installed library (not the symlinks) may only depend on FFmpeg itself or on the system.
for lib in "$tmp"/out/lib/lib*.dylib; do
    if [ -L "$lib" ]; then
        continue
    fi
    deps="$(otool -L "$lib" | tail -n +2)"
    while read -r dep _; do
        case "$dep" in
            @loader_path/* | /usr/lib/* | /System/*) ;;
            *) echo "Dependencia no permitida en $(basename "$lib"): $dep" >&2; exit 1 ;;
        esac
    done <<< "$deps"
done

# Only the names FFmpegLoader opens (libavcodec.N.dylib ...); cp -L copies the real library under that name.
name_re='^lib[a-z0-9_]+\.[0-9]+\.dylib$'
rm -rf "$dest/lib"
mkdir -p "$dest/lib"
for lib in "$tmp"/out/lib/lib*.dylib; do
    name="$(basename "$lib")"
    if [[ "$name" =~ $name_re ]]; then
        cp -L "$lib" "$dest/lib/$name"
    fi
done
cp "$tmp/src/COPYING.LGPLv2.1" "$dest/LICENSE.txt"

echo "Librerías de FFmpeg en $dest/lib"
