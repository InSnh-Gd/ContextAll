#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_PATH="$REPO_ROOT/tests/ContextAll.Tests.E2E/ContextAll.Tests.E2E.csproj"

CONFIG="Release"
FILTER=""
EXTRA_ARGS=()

show_help() {
  cat << 'EOF'
Usage: ./scripts/test-e2e.sh [options]

Options:
  --all                     Run all E2E tests (default)
  --tier <1-4>              Run tests for a specific tier (1, 2, 3, or 4)
  --feature <ID>            Run tests for a feature (e.g., 1..23, F01..F23)
  -c, --configuration <CFG> Build configuration (default: Release)
  -h, --help                Show this help message
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --all)
      FILTER=""
      shift
      ;;
    --tier)
      if [[ $# -lt 2 ]]; then
        echo "Error: --tier requires an argument (1-4)." >&2
        exit 1
      fi
      TIER="$2"
      if [[ ! "$TIER" =~ ^[1-4]$ ]]; then
        echo "Error: Invalid tier '$TIER'. Must be 1, 2, 3, or 4." >&2
        exit 1
      fi
      FILTER="Category=Tier${TIER}"
      shift 2
      ;;
    --feature)
      if [[ $# -lt 2 ]]; then
        echo "Error: --feature requires a feature ID argument (F01..F23 or 1..23)." >&2
        exit 1
      fi
      FEAT="$2"
      NUM=""
      if [[ "$FEAT" =~ ^[0-9]+$ ]]; then
        NUM="$FEAT"
      elif [[ "$FEAT" =~ ^[Ff]([0-9]+)$ ]]; then
        NUM="${BASH_REMATCH[1]}"
      fi
      if [[ -z "$NUM" ]] || [[ ${#NUM} -gt 2 ]] || (( 10#$NUM < 1 || 10#$NUM > 23 )); then
        echo "Error: Invalid feature ID '$FEAT'. Valid features are F01 through F23 (or 1..23)." >&2
        exit 1
      fi
      printf -v FEAT_PADDED "%02d" "$((10#$NUM))"
      FILTER="Feature=F${FEAT_PADDED}"
      shift 2
      ;;
    --configuration|-c)
      if [[ $# -lt 2 ]]; then
        echo "Error: $1 requires an argument." >&2
        exit 1
      fi
      CONFIG="$2"
      shift 2
      ;;
    -h|--help)
      show_help
      exit 0
      ;;
    *)
      EXTRA_ARGS+=("$1")
      shift
      ;;
  esac
done

CMD=(dotnet test "$PROJECT_PATH" -c "$CONFIG")

if [[ -n "$FILTER" ]]; then
  CMD+=(--filter "$FILTER")
fi

if [[ ${#EXTRA_ARGS[@]} -gt 0 ]]; then
  CMD+=("${EXTRA_ARGS[@]}")
fi

echo "Running: ${CMD[*]}"
exec "${CMD[@]}"
