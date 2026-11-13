#!/bin/sh
set -eu
here=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
root=$(CDPATH= cd -- "$here/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
app=$work/AppDir
mkdir -p "$app" "$root/artifacts"

dotnet publish "$root/src/ContextAll.Host/ContextAll.Host.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -o "$work/host"
cp "$work/host/ContextAll.Host" "$app/contextall-host"

dotnet publish "$root/src/ContextAll.App.Desktop/ContextAll.App.Desktop.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishAot=false -p:PublishSingleFile=false \
  -o "$app"

cp "$here/AppRun" "$here/contextall-shell" "$here/contextall.desktop" "$here/contextall.png" "$app/"
chmod 755 "$app/AppRun" "$app/contextall-shell" "$app/contextall-host"

quic=$(readlink -f /usr/lib/x86_64-linux-gnu/libmsquic.so.2)
cp "$quic" "$app/$(basename "$quic")"
ln -s "$(basename "$quic")" "$app/libmsquic.so.2"

tool=$work/appimagetool
curl -fsSL -o "$tool" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod 755 "$tool"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" "$app" "$root/artifacts/ContextAll.AppImage"
chmod 755 "$root/artifacts/ContextAll.AppImage"
echo "$root/artifacts/ContextAll.AppImage"
