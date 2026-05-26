#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID="${1:-win-x64}"

case "$RID" in
  win-x64|win-arm64)
    ;;
  *)
    echo "Usage: bash scripts/publish-windows.sh [win-x64|win-arm64]" >&2
    exit 2
    ;;
esac

PROJECT="$ROOT_DIR/src/QuotaMonitor.App.Avalonia/QuotaMonitor.App.Avalonia.csproj"
PUBLISH_DIR="$ROOT_DIR/src/QuotaMonitor.App.Avalonia/bin/Release/net10.0/$RID/publish"
DIST_DIR="$ROOT_DIR/dist/windows-$RID"

dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true

mkdir -p "$DIST_DIR"
cp -R "$PUBLISH_DIR/." "$DIST_DIR/"

echo "Published:"
echo "$PUBLISH_DIR"
echo "Windows app:"
echo "$DIST_DIR/QuotaMonitor.exe"
