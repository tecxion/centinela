#!/usr/bin/env bash
# Downloads FFmpeg 9.0 LGPL shared build for Linux (BtbN) into <repo>/ffmpeg/lib.
# Counterpart of tools/get-ffmpeg.ps1 (same release, same version).
set -euo pipefail

for tool in curl tar; do
    command -v "$tool" >/dev/null 2>&1 || { echo "Falta '$tool'. Instálalo y vuelve a intentarlo." >&2; exit 1; }
done

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dest="$root/ffmpeg"
url='https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n9.0-latest-linux64-lgpl-shared-9.0.tar.xz'

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

curl -fL --retry 3 -o "$tmp/ffmpeg.tar.xz" "$url"
tar -xJf "$tmp/ffmpeg.tar.xz" -C "$tmp"

lib="$(find "$tmp" -mindepth 2 -maxdepth 2 -type d -name lib | head -n 1)"
[ -n "$lib" ] || { echo "El archivo descargado no contiene la carpeta lib." >&2; exit 1; }

rm -rf "$dest/lib"
mkdir -p "$dest/lib"
# cp -P keeps the symlinks (libavcodec.so.62 -> libavcodec.so.62.x.y) as they are in the archive.
cp -P "$lib"/*.so* "$dest/lib/"

license="$(find "$tmp" -mindepth 2 -maxdepth 2 -type f -name 'LICENSE*' | head -n 1)"
if [ -n "$license" ]; then
    cp "$license" "$dest/LICENSE.txt"
fi

echo "FFmpeg libraries in $dest/lib"
