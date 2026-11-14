#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ABIS="${1:-x86_64,arm64-v8a}"
OUT_DIR="$ROOT_DIR/app/build/nativeJniLibs"
CONFIGURATION="${CONFIG:-Release}"

find_ndk() {
  for candidate in "${ANDROID_NDK_HOME:-}" "${ANDROID_NDK_ROOT:-}" "${ANDROID_NDK_LATEST_HOME:-}"; do
    if [[ -n "$candidate" && -d "$candidate/toolchains/llvm/prebuilt" ]]; then
      echo "$candidate"
      return 0
    fi
  done

  if [[ -n "${ANDROID_HOME:-}" && -d "$ANDROID_HOME/ndk" ]]; then
    local latest
    latest="$(find "$ANDROID_HOME/ndk" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
    if [[ -n "$latest" && -d "$latest/toolchains/llvm/prebuilt" ]]; then
      echo "$latest"
      return 0
    fi
  fi

  if [[ -n "${ANDROID_HOME:-}" && -d "$ANDROID_HOME/ndk-bundle/toolchains/llvm/prebuilt" ]]; then
    echo "$ANDROID_HOME/ndk-bundle"
    return 0
  fi

  local nix_ndk
  for nix_ndk in /nix/store/*-android-sdk-ndk-*/libexec/android-sdk/ndk-bundle; do
    if [[ -d "$nix_ndk/toolchains/llvm/prebuilt" ]]; then
      echo "$nix_ndk"
      return 0
    fi
  done

  return 1
}

if ! command -v dotnet >/dev/null 2>&1; then
  for extra_bin in "/etc/profiles/per-user/${USER:-}/bin" "$HOME/.dotnet"; do
    if [[ -x "$extra_bin/dotnet" ]]; then
      export PATH="$extra_bin:$PATH"
      break
    fi
  done
fi

NDK_DIR="$(find_ndk)" || {
  echo "error: Android NDK not found. Set ANDROID_NDK_HOME or install NDK under \$ANDROID_HOME/ndk." >&2
  exit 1
}

LLVM_BIN="$(find "$NDK_DIR/toolchains/llvm/prebuilt" -mindepth 2 -maxdepth 2 -name bin -type d | head -n 1)"
if [[ -z "$LLVM_BIN" || ! -x "$LLVM_BIN/llvm-objcopy" ]]; then
  echo "error: NDK LLVM bin directory not found under $NDK_DIR" >&2
  exit 1
fi

export PATH="$LLVM_BIN:$PATH"

find_msquic_aar() {
  find "${NUGET_PACKAGES:-$HOME/.nuget/packages}/vpnhood.net.quic.msquic.androidnative" -name "VpnHood.Net.Quic.MsQuic.AndroidNative.aar" 2>/dev/null | sort -V | tail -n 1 || true
}

AAR_PATH="$(find_msquic_aar)"
if [[ -z "$AAR_PATH" ]]; then
  dotnet restore "$ROOT_DIR/src/ContextAll.App.Android/ContextAll.App.Android.csproj"
  AAR_PATH="$(find_msquic_aar)"
fi
if [[ -z "$AAR_PATH" ]]; then
  echo "error: VpnHood.Net.Quic.MsQuic.AndroidNative.aar not found in NuGet cache" >&2
  exit 1
fi

JAR_TOOL="${JAVA_HOME:+$JAVA_HOME/bin/}jar"
if ! command -v "$JAR_TOOL" >/dev/null 2>&1; then
  JAR_TOOL="jar"
fi

TMP_AAR_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_AAR_DIR"' EXIT
(cd "$TMP_AAR_DIR" && "$JAR_TOOL" -xf "$AAR_PATH" jni)

IFS=',' read -ra TARGET_ABIS <<<"$ABIS"
for abi in "${TARGET_ABIS[@]}"; do
  abi="${abi//[[:space:]]/}"
  case "$abi" in
    x86_64)
      rid="linux-bionic-x64"
      cc="$LLVM_BIN/x86_64-linux-android26-clang"
      ;;
    arm64-v8a)
      rid="linux-bionic-arm64"
      cc="$LLVM_BIN/aarch64-linux-android26-clang"
      ;;
    *)
      echo "error: Unsupported ABI '$abi' (expected x86_64 or arm64-v8a)" >&2
      exit 1
      ;;
  esac

  if [[ ! -x "$cc" ]]; then
    echo "error: NDK clang compiler not found: $cc" >&2
    exit 1
  fi

  mkdir -p "$OUT_DIR/$abi"
  msquic_src="$TMP_AAR_DIR/jni/$abi/libmsquic.so"
  if [[ ! -f "$msquic_src" ]]; then
    echo "error: Missing $msquic_src in $AAR_PATH" >&2
    exit 1
  fi
  cp -f "$msquic_src" "$OUT_DIR/$abi/libmsquic.so"

  echo "Publishing ContextAll.Native ($rid -> $abi)..."
  dotnet publish "$ROOT_DIR/src/ContextAll.Native/ContextAll.Native.csproj" \
    -r "$rid" \
    -c "$CONFIGURATION" \
    -p:CppCompilerAndLinker="$cc" \
    -p:ObjCopyName="$LLVM_BIN/llvm-objcopy" \
    -p:StripSymbols=true \
    --nologo

  native_so="$ROOT_DIR/src/ContextAll.Native/bin/$CONFIGURATION/net10.0/$rid/publish/ContextAll.Native.so"
  if [[ ! -f "$native_so" ]]; then
    echo "error: Expected NativeAOT output not found: $native_so" >&2
    exit 1
  fi
  cp -f "$native_so" "$OUT_DIR/$abi/libcontextall_native.so"
done

echo "Prepared native JNI libraries under $OUT_DIR"

