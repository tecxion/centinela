#!/usr/bin/env bash
# Builds dist/Centinela-<version>-macos-<arm64|intel>.dmg (Centinela.app: self-contained .NET publish + FFmpeg).
# Usage: tools/build-dmg.sh arm64|x64 [version]   (default: <Version> of Directory.Build.props)
# Signed ad hoc unless CODESIGN_IDENTITY names a signing identity (the Developer ID of phase 5).
set -euo pipefail

case "${1:-}" in
    arm64) arch=arm64; suffix=arm64 ;;
    x64) arch=x64; suffix=intel ;;
    *) echo "Uso: tools/build-dmg.sh arm64|x64 [versión]" >&2; exit 1 ;;
esac

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

for tool in dotnet codesign hdiutil sips iconutil; do
    command -v "$tool" >/dev/null 2>&1 || { echo "Falta '$tool'. Instálalo y vuelve a intentarlo." >&2; exit 1; }
done

if ! compgen -G "ffmpeg/osx-$arch/lib/libavcodec.*.dylib" >/dev/null; then
    echo "Faltan las librerías de FFmpeg en ffmpeg/osx-$arch/lib. Ejecuta antes: tools/get-ffmpeg-mac.sh $arch" >&2
    exit 1
fi

version="${2:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1)}"
[ -n "$version" ] || { echo "No se encuentra la versión en Directory.Build.props." >&2; exit 1; }

work="$root/dist/macos-$arch"
app="$work/dmg/Centinela.app"
exe_dir="$app/Contents/MacOS"
dmg="$root/dist/Centinela-$version-macos-$suffix.dmg"

rm -rf "$work"
trap 'rm -rf "$work"' EXIT
mkdir -p "$exe_dir" "$app/Contents/Resources"
ln -s /Applications "$work/dmg/Applications"

dotnet publish src/Centinela.Desktop -c Release -r "osx-$arch" --self-contained true -o "$exe_dir"

# FFmpeg: only lib<name>.<N>.dylib files, which are what FFmpegLoader opens. This intentionally overlaps with the
# copy that publish already does through Directory.Build.targets (same files): the package must not depend on it.
mkdir -p "$exe_dir/ffmpeg"
cp -L "ffmpeg/osx-$arch"/lib/*.dylib "$exe_dir/ffmpeg/"
cp "ffmpeg/osx-$arch/LICENSE.txt" "$exe_dir/ffmpeg/LICENSE.txt"

sed "s/@VERSION@/$version/g" packaging/macos/Info.plist > "$app/Contents/Info.plist"

# Icon: the source PNG is 300x300, so the larger sizes are scaled up.
icon_src=src/Centinela.Desktop/Assets/centinela.png
iconset="$work/centinela.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$icon_src" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    sips -z "$((size * 2))" "$((size * 2))" "$icon_src" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/centinela.icns"

# Signing: ad hoc ("-") by default; a real identity also gets a secure timestamp.
identity="${CODESIGN_IDENTITY:--}"
sign=(codesign --force --options runtime --entitlements "$root/packaging/macos/Centinela.entitlements" --sign "$identity")
if [ "$identity" != "-" ]; then
    sign+=(--timestamp)
fi
# Every file in Contents/MacOS first, then the bundle, which seals them. "-exec ... +" makes find fail if codesign does.
# The main executable is left to the bundle step: signing it is signing the bundle, which needs the rest signed first.
find "$exe_dir" -type f ! -path "$exe_dir/Centinela" -exec "${sign[@]}" {} +
"${sign[@]}" "$app"
codesign --verify --strict "$app"

# «Resource busy» is an intermittent hdiutil failure on hosted runners: up to three tries.
for try in 1 2 3; do
    hdiutil create -volname Centinela -srcfolder "$work/dmg" -ov -format UDZO "$dmg" && break
    [ "$try" -lt 3 ] || { echo "No se pudo crear el .dmg (hdiutil)." >&2; exit 1; }
    sleep 5
done
if [ "$identity" != "-" ]; then
    codesign --force --timestamp --sign "$identity" "$dmg"
fi

echo
echo "Imagen de disco creada: dist/Centinela-$version-macos-$suffix.dmg"
