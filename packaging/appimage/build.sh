#!/bin/sh
set -eu
here=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
root=$(CDPATH= cd -- "$here/../.." && pwd)

stage_only=0
stage_dir=
while [ "$#" -gt 0 ]; do
  case "$1" in
    --stage-only)
      if [ "$#" -lt 2 ] || [ -z "$2" ]; then
        echo "error: --stage-only requires a target directory" >&2
        exit 2
      fi
      stage_only=1
      stage_dir=$2
      shift 2
      ;;
    *)
      echo "error: unknown option: $1" >&2
      exit 2
      ;;
  esac
done

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
if [ "$stage_only" -eq 1 ]; then
  mkdir -p "$stage_dir"
  app=$(CDPATH= cd -- "$stage_dir" && pwd)
else
  app=$work/AppDir
  mkdir -p "$app" "$root/artifacts"
fi

set -- -c Release -r linux-x64 --self-contained true -p:NuGetAudit=false
if [ -n "${NUGET_SOURCE:-}" ]; then
  set -- "$@" --source "$NUGET_SOURCE"
fi

dotnet publish "$root/src/ContextAll.Host/ContextAll.Host.csproj" \
  "$@" \
  -o "$work/host"
cp "$work/host/ContextAll.Host" "$app/contextall-host"

dotnet publish "$root/src/ContextAll.App.Desktop/ContextAll.App.Desktop.csproj" \
  "$@" \
  -p:PublishAot=false -p:PublishSingleFile=false \
  -o "$app"

cp "$here/AppRun" "$here/contextall-shell" "$here/contextall.desktop" "$here/contextall.png" "$app/"
chmod 755 "$app/AppRun" "$app/contextall-shell" "$app/contextall-host"

quic_candidate=
for candidate in \
  "${LIBMSQUIC_PATH:-}" \
  /usr/lib/x86_64-linux-gnu/libmsquic.so.2 \
  /usr/lib64/libmsquic.so.2 \
  /usr/lib/libmsquic.so.2; do
  if [ -n "$candidate" ] && [ -e "$candidate" ]; then
    quic_candidate=$candidate
    break
  fi
done

if [ -n "$quic_candidate" ]; then
  quic=$(readlink -f "$quic_candidate")
  quic_base=$(basename "$quic")
  cp "$quic" "$app/$quic_base"
  if [ "$quic_base" != "libmsquic.so.2" ]; then
    ln -sf "$quic_base" "$app/libmsquic.so.2"
  fi
elif [ "$stage_only" -eq 1 ]; then
  echo "warning: libmsquic.so.2 not found; continuing for --stage-only" >&2
else
  echo "error: libmsquic.so.2 not found" >&2
  exit 1
fi

if [ "$stage_only" -eq 1 ]; then
  echo "$app"
  exit 0
fi

if command -v appimagetool >/dev/null 2>&1; then
  tool=$(command -v appimagetool)
else
  tool=$work/appimagetool
  curl -fsSL -o "$tool" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod 755 "$tool"
fi
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" "$app" "$root/artifacts/ContextAll.AppImage"
chmod 755 "$root/artifacts/ContextAll.AppImage"
echo "$root/artifacts/ContextAll.AppImage"
