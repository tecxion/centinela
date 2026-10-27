#!/usr/bin/env bash
# Builds dist/centinela_<version>_amd64.deb (self-contained .NET publish + FFmpeg).
# Usage: tools/build-deb.sh [version]   (default: <Version> of Directory.Build.props)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

for tool in dotnet dpkg-deb; do
    command -v "$tool" >/dev/null 2>&1 || { echo "Falta '$tool'. Instálalo y vuelve a intentarlo." >&2; exit 1; }
done

if ! compgen -G "ffmpeg/lib/libavcodec.so.*" >/dev/null; then
    echo "Faltan las librerías de FFmpeg en ffmpeg/lib. Ejecuta antes: tools/get-ffmpeg.sh" >&2
    exit 1
fi

version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1)}"
[ -n "$version" ] || { echo "No se encuentra la versión en Directory.Build.props." >&2; exit 1; }

staging="$root/dist/deb-staging"
app="$staging/opt/centinela"
deb="$root/dist/centinela_${version}_amd64.deb"

rm -rf "$staging"
trap 'rm -rf "$staging"' EXIT
mkdir -p "$app"

dotnet publish src/Centinela.Desktop -c Release -r linux-x64 --self-contained true -o "$app"

# Directory.Build.targets only copies the Windows DLLs for win-* builds; remove any just in case.
rm -f "$app"/ffmpeg/*.dll

# FFmpeg: only the soname files (libavcodec.so.N ...) are what FFmpegLoader opens.
# cp -L copies the real library under the soname, so the package holds no symlinks
# and no duplicate copies under the full version or the bare .so development name.
# This intentionally overlaps with the copy that publish already does through
# Directory.Build.targets (same files): the package must not depend on it.
mkdir -p "$app/ffmpeg"
for so in ffmpeg/lib/lib*.so.*; do
    name="$(basename "$so")"
    if [[ "$name" =~ ^lib[a-z0-9_]+\.so\.[0-9]+$ ]]; then
        cp -L "$so" "$app/ffmpeg/$name"
    fi
done
if [ -f ffmpeg/LICENSE.txt ]; then
    cp ffmpeg/LICENSE.txt "$app/ffmpeg/LICENSE.txt"
fi

mkdir -p "$staging/usr/bin"
ln -s /opt/centinela/Centinela "$staging/usr/bin/centinela"

mkdir -p "$staging/usr/share/applications"
cp packaging/linux/centinela.desktop "$staging/usr/share/applications/centinela.desktop"

icons="$staging/usr/share/icons/hicolor/256x256/apps"
mkdir -p "$icons"
icon_src=src/Centinela.Desktop/Assets/centinela.png
# The source PNG is 300x300; scale it to the folder size when ImageMagick is available.
if command -v magick >/dev/null 2>&1; then
    magick "$icon_src" -resize 256x256 "$icons/centinela.png"
elif command -v convert >/dev/null 2>&1; then
    convert "$icon_src" -resize 256x256 "$icons/centinela.png"
else
    cp "$icon_src" "$icons/centinela.png"
fi

mkdir -p "$staging/DEBIAN"
sed "s/@VERSION@/$version/" packaging/linux/control.in > "$staging/DEBIAN/control"
installed_size="$(du -sk --exclude=DEBIAN "$staging" | cut -f1)"
sed -i "/^Description:/i Installed-Size: $installed_size" "$staging/DEBIAN/control"

# Permissions: directories 755, files 644, executables 755.
find "$staging" -type d -exec chmod 755 {} +
find "$staging" -type f -exec chmod 644 {} +
chmod 755 "$app/Centinela"
if [ -f "$app/createdump" ]; then
    chmod 755 "$app/createdump"
fi

rm -f "$deb"
dpkg-deb --build --root-owner-group "$staging" "$deb"

echo
echo "Paquete creado: dist/centinela_${version}_amd64.deb"
echo "Para instalarlo: sudo apt install ./dist/centinela_${version}_amd64.deb"
