#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$ROOT_DIR/OrionBE.Launcher/OrionBE.Launcher.csproj"
RID="${1:-linux-x64}"
CONFIGURATION="${2:-Release}"
APP_NAME="OrionBE.Launcher"
APP_DIR="$ROOT_DIR/artifacts/AppDir/$RID"
PUBLISH_DIR="$ROOT_DIR/artifacts/publish/$RID"
OUTPUT_DIR="$ROOT_DIR/artifacts/appimage/$RID"
DESKTOP_TEMPLATE="$ROOT_DIR/packaging/linux/orionbe-launcher.desktop"
ICON_TEMPLATE="$ROOT_DIR/packaging/linux/orionbe-launcher.svg"
APPIMAGE_TOOL_BIN="${APPIMAGE_TOOL:-appimagetool}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Erro: dotnet CLI nao encontrado."
  exit 1
fi

if ! command -v "$APPIMAGE_TOOL_BIN" >/dev/null 2>&1; then
  echo "Erro: appimagetool nao encontrado. Defina APPIMAGE_TOOL ou instale appimagetool."
  exit 1
fi

if [[ ! -f "$PROJECT_PATH" ]]; then
  echo "Erro: projeto nao encontrado em $PROJECT_PATH"
  exit 1
fi

if [[ ! -f "$DESKTOP_TEMPLATE" || ! -f "$ICON_TEMPLATE" ]]; then
  echo "Erro: arquivos de packaging Linux ausentes em packaging/linux."
  exit 1
fi

echo "Publicando $APP_NAME para $RID ($CONFIGURATION)..."
dotnet publish "$PROJECT_PATH" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:PublishTrimmed=false \
  -o "$PUBLISH_DIR"

echo "Montando AppDir..."
rm -rf "$APP_DIR" "$OUTPUT_DIR"
mkdir -p "$APP_DIR/usr/bin" "$APP_DIR/usr/share/applications" "$APP_DIR/usr/share/icons/hicolor/scalable/apps" "$OUTPUT_DIR"

cp -a "$PUBLISH_DIR/." "$APP_DIR/usr/bin/"
cp "$DESKTOP_TEMPLATE" "$APP_DIR/orionbe-launcher.desktop"
cp "$DESKTOP_TEMPLATE" "$APP_DIR/usr/share/applications/orionbe-launcher.desktop"
cp "$ICON_TEMPLATE" "$APP_DIR/orionbe-launcher.svg"
cp "$ICON_TEMPLATE" "$APP_DIR/usr/share/icons/hicolor/scalable/apps/orionbe-launcher.svg"
ln -sf "orionbe-launcher.svg" "$APP_DIR/.DirIcon"

cat > "$APP_DIR/AppRun" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$HERE/usr/bin/OrionBE.Launcher" "$@"
EOF
chmod +x "$APP_DIR/AppRun"

VERSION="$(date +%Y.%m.%d)"
APPIMAGE_PATH="$OUTPUT_DIR/OrionBE-Launcher-${RID}-${VERSION}.AppImage"

case "$RID" in
  linux-x64) APPIMAGE_ARCH="x86_64" ;;
  linux-arm64) APPIMAGE_ARCH="aarch64" ;;
  *)
    echo "Erro: RID '$RID' nao suportado para AppImage (use linux-x64 ou linux-arm64)."
    exit 1
    ;;
esac

echo "Gerando AppImage..."
ARCH="$APPIMAGE_ARCH" "$APPIMAGE_TOOL_BIN" "$APP_DIR" "$APPIMAGE_PATH"

echo "AppImage pronta: $APPIMAGE_PATH"
