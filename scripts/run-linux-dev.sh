#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

BUILD=1
if [[ "${1:-}" == "--no-build" ]]; then
  BUILD=0
  shift
fi

MODE="${1:-gui}"
if [[ "$#" -gt 0 ]]; then
  shift
fi

if [[ "$BUILD" -eq 1 ]]; then
  dotnet build "$REPO_ROOT/ContextAll.Linux.slnf" -c Release -p:NuGetAudit=false --nologo
fi

resolve_dotnet_root() {
  if [[ -n "${DOTNET_ROOT:-}" && -d "$DOTNET_ROOT/shared" ]]; then
    return 0
  fi

  local dotnet_cmd dotnet_real dir candidate
  dotnet_cmd="$(command -v dotnet 2>/dev/null || true)"
  if [[ -n "$dotnet_cmd" ]]; then
    dotnet_real="$(readlink -f "$dotnet_cmd" 2>/dev/null || true)"
    if [[ -n "$dotnet_real" ]]; then
      dir="$(dirname "$dotnet_real")"
      for candidate in "$dir" "$dir/../share/dotnet"; do
        if [[ -d "$candidate/shared" ]]; then
          export DOTNET_ROOT="$(cd "$candidate" && pwd)"
          return 0
        fi
      done
    fi
  fi
}

resolve_nix_ld_library_path() {
  if [[ -e /usr/lib/x86_64-linux-gnu/libfontconfig.so.1 || -e /usr/lib/libfontconfig.so.1 || ! -d /nix/store ]]; then
    return 0
  fi

  local host_glibc=""
  if [[ -L /run/current-system/sw/share/nix-ld/lib/ld.so ]]; then
    local ld_real
    ld_real="$(readlink -f /run/current-system/sw/share/nix-ld/lib/ld.so 2>/dev/null || true)"
    host_glibc="$(basename "$(dirname "$(dirname "$ld_real")")")"
  fi

  local -a libs=(
    libfontconfig.so.1
    libwayland-client.so.0
    libwayland-egl.so.1
    libwayland-cursor.so.0
    libfreetype.so.6
    libexpat.so.1
    libxkbcommon.so.0
    libX11.so.6
    libXfixes.so.3
    libXcursor.so.1
    libXi.so.6
    libXrandr.so.2
    libICE.so.6
    libSM.so.6
    libGL.so.1
    libEGL.so.1
    libbz2.so.1
    libz.so.1
    libpng16.so.16
    libbrotlidec.so.1
  )

  local -A seen_dirs=()
  local -a dirs=()
  local so cand cls ldd_out fallback_dir

  for so in "${libs[@]}"; do
    fallback_dir=""
    for cand in /nix/store/*/lib/"$so"; do
      case "$cand" in
        *fhsenv*|*-dev/*) continue ;;
      esac
      [[ -e "$cand" ]] || continue

      cls="$(od -An -tx1 -j4 -N1 "$cand" 2>/dev/null | tr -d ' \n' || true)"
      [[ "$cls" == "02" ]] || continue

      ldd_out="$(ldd "$cand" 2>/dev/null || true)"
      if [[ -z "$fallback_dir" ]] && ! grep -q "not found" <<< "$ldd_out"; then
        fallback_dir="$(dirname "$cand")"
      fi

      if [[ -n "$host_glibc" ]] && ! grep -q "$host_glibc" <<< "$ldd_out"; then
        continue
      fi

      local chosen_dir
      chosen_dir="$(dirname "$cand")"
      if [[ -z "${seen_dirs[$chosen_dir]:-}" ]]; then
        seen_dirs["$chosen_dir"]=1
        dirs+=("$chosen_dir")
      fi
      fallback_dir=""
      break
    done

    if [[ -n "$fallback_dir" && -z "${seen_dirs[$fallback_dir]:-}" ]]; then
      seen_dirs["$fallback_dir"]=1
      dirs+=("$fallback_dir")
    fi
  done

  if [[ ${#dirs[@]} -gt 0 ]]; then
    local joined
    joined="$(IFS=:; echo "${dirs[*]}")"
    export LD_LIBRARY_PATH="$joined${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  fi
}

resolve_cursor_env() {
  if [[ -z "${XCURSOR_THEME:-}" ]] && command -v gsettings >/dev/null 2>&1; then
    local gs_theme
    gs_theme="$(gsettings get org.gnome.desktop.interface cursor-theme 2>/dev/null || true)"
    gs_theme="${gs_theme//\'/}"
    if [[ -n "$gs_theme" ]]; then
      export XCURSOR_THEME="$gs_theme"
    fi
  fi

  if [[ -z "${XCURSOR_SIZE:-}" ]] && command -v gsettings >/dev/null 2>&1; then
    local gs_size
    gs_size="$(gsettings get org.gnome.desktop.interface cursor-size 2>/dev/null || true)"
    if [[ "$gs_size" =~ ^[0-9]+$ ]]; then
      export XCURSOR_SIZE="$gs_size"
    fi
  fi

  if [[ -z "${XCURSOR_THEME:-}" && -d /run/current-system/sw/share/icons/Adwaita/cursors ]]; then
    export XCURSOR_THEME="Adwaita"
  fi
  if [[ -z "${XCURSOR_SIZE:-}" ]]; then
    export XCURSOR_SIZE="24"
  fi

  local extra_paths=""
  local cand
  for cand in "$HOME/.local/share/icons" "$HOME/.icons" "/etc/profiles/per-user/${USER:-}/share/icons" "/run/current-system/sw/share/icons"; do
    [[ -d "$cand" ]] && extra_paths="${extra_paths:+$extra_paths:}$cand"
  done
  if [[ -n "$extra_paths" ]]; then
    export XCURSOR_PATH="${XCURSOR_PATH:+$XCURSOR_PATH:}$extra_paths"
  fi
}

resolve_dotnet_root
resolve_nix_ld_library_path
resolve_cursor_env

DESKTOP_BIN="$REPO_ROOT/src/ContextAll.App.Desktop/bin/Release/net10.0/ContextAll.App.Desktop"
HOST_BIN="$REPO_ROOT/src/ContextAll.Host/bin/Release/net10.0/ContextAll.Host"

case "$MODE" in
  gui)
    exec "$DESKTOP_BIN" "$@"
    ;;
  serve)
    exec "$HOST_BIN" serve "$@"
    ;;
  cli)
    exec "$HOST_BIN" "$@"
    ;;
  -h|--help)
    cat << 'EOF'
Usage: ./scripts/run-linux-dev.sh [--no-build] [gui|serve|cli] [args...]
EOF
    ;;
  *)
    echo "Unknown mode: $MODE (expected gui, serve, or cli)" >&2
    exit 2
    ;;
esac
