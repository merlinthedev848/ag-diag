#!/bin/bash
set -e

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
OUTPUT_DIR="$DIR/publish_output"
EDITION="${1:-all}"

echo "=========================================================="
echo " Building Agilico MSP Toolkit for macOS (Edition: $EDITION)"
echo "=========================================================="

mkdir -p "$OUTPUT_DIR"

build_dmg_helper() {
    local APP_PATH="$1"
    local DMG_PATH="$2"
    local VOL_NAME="$3"
    local APP_NAME="$4"

    if command -v hdiutil >/dev/null 2>&1; then
        echo "Creating DMG using Apple hdiutil: $DMG_PATH"
        local STAGING="$OUTPUT_DIR/temp_dmg_$$"
        rm -rf "$STAGING"
        mkdir -p "$STAGING"
        cp -r "$APP_PATH" "$STAGING/$APP_NAME"
        ln -s /Applications "$STAGING/Applications"
        hdiutil create -volname "$VOL_NAME" -srcfolder "$STAGING" -ov -format UDZO "$DMG_PATH"
        rm -rf "$STAGING"
    fi
}

# -------------------------------------------------------------
# FULL STANDALONE EDITIONS
# -------------------------------------------------------------
if [ "$EDITION" = "all" ] || [ "$EDITION" = "full" ]; then
    echo ""
    echo ">>> [1/2] Building Full Standalone Edition (Self-Contained)..."

    # Apple Silicon Full
    echo " -> Publishing osx-arm64 (Full)..."
    dotnet publish -c Release -r osx-arm64 --self-contained true -p:IsFullVersion=true -o "$OUTPUT_DIR/osx-arm64-full" "$DIR"
    APP_ARM="$OUTPUT_DIR/Agilico_MSP_Toolkit_arm64.app"
    mkdir -p "$APP_ARM/Contents/MacOS" "$APP_ARM/Contents/Resources"
    cp -r "$OUTPUT_DIR/osx-arm64-full/"* "$APP_ARM/Contents/MacOS/"
    cp "$DIR/Assets/Info.plist" "$APP_ARM/Contents/"
    cp "$DIR/Assets/logo.png" "$APP_ARM/Contents/Resources/"
    [ -f "$DIR/Assets/logo.icns" ] && cp "$DIR/Assets/logo.icns" "$APP_ARM/Contents/Resources/"
    chmod +x "$APP_ARM/Contents/MacOS/AgilicoDiagMac"
    build_dmg_helper "$APP_ARM" "$OUTPUT_DIR/Agilico_MSP_Toolkit_arm64.dmg" "Agilico MSP ARM64" "Agilico MSP Toolkit.app"

    # Intel Mac Full
    echo " -> Publishing osx-x64 (Full)..."
    dotnet publish -c Release -r osx-x64 --self-contained true -p:IsFullVersion=true -o "$OUTPUT_DIR/osx-x64-full" "$DIR"
    APP_X64="$OUTPUT_DIR/Agilico_MSP_Toolkit_x64.app"
    mkdir -p "$APP_X64/Contents/MacOS" "$APP_X64/Contents/Resources"
    cp -r "$OUTPUT_DIR/osx-x64-full/"* "$APP_X64/Contents/MacOS/"
    cp "$DIR/Assets/Info.plist" "$APP_X64/Contents/"
    cp "$DIR/Assets/logo.png" "$APP_X64/Contents/Resources/"
    [ -f "$DIR/Assets/logo.icns" ] && cp "$DIR/Assets/logo.icns" "$APP_X64/Contents/Resources/"
    chmod +x "$APP_X64/Contents/MacOS/AgilicoDiagMac"
    build_dmg_helper "$APP_X64" "$OUTPUT_DIR/Agilico_MSP_Toolkit_x64.dmg" "Agilico MSP x64" "Agilico MSP Toolkit.app"
fi

# -------------------------------------------------------------
# LITE EDITIONS
# -------------------------------------------------------------
if [ "$EDITION" = "all" ] || [ "$EDITION" = "lite" ]; then
    echo ""
    echo ">>> [2/2] Building Lite Edition (Framework-Dependent)..."

    # Apple Silicon Lite
    echo " -> Publishing osx-arm64 (Lite)..."
    dotnet publish -c Release -r osx-arm64 --self-contained false -p:IsFullVersion=false -o "$OUTPUT_DIR/osx-arm64-lite" "$DIR"
    APP_LITE_ARM="$OUTPUT_DIR/Agilico_MSP_Toolkit_Lite_arm64.app"
    mkdir -p "$APP_LITE_ARM/Contents/MacOS" "$APP_LITE_ARM/Contents/Resources"
    cp -r "$OUTPUT_DIR/osx-arm64-lite/"* "$APP_LITE_ARM/Contents/MacOS/"
    cp "$DIR/Assets/Info.Lite.plist" "$APP_LITE_ARM/Contents/Info.plist"
    cp "$DIR/Assets/logo.png" "$APP_LITE_ARM/Contents/Resources/"
    [ -f "$DIR/Assets/logo.icns" ] && cp "$DIR/Assets/logo.icns" "$APP_LITE_ARM/Contents/Resources/"
    chmod +x "$APP_LITE_ARM/Contents/MacOS/AgilicoDiagMac"
    build_dmg_helper "$APP_LITE_ARM" "$OUTPUT_DIR/Agilico_MSP_Toolkit_Lite_arm64.dmg" "Agilico Lite ARM" "Agilico MSP Toolkit Lite.app"

    # Intel Mac Lite
    echo " -> Publishing osx-x64 (Lite)..."
    dotnet publish -c Release -r osx-x64 --self-contained false -p:IsFullVersion=false -o "$OUTPUT_DIR/osx-x64-lite" "$DIR"
    APP_LITE_X64="$OUTPUT_DIR/Agilico_MSP_Toolkit_Lite_x64.app"
    mkdir -p "$APP_LITE_X64/Contents/MacOS" "$APP_LITE_X64/Contents/Resources"
    cp -r "$OUTPUT_DIR/osx-x64-lite/"* "$APP_LITE_X64/Contents/MacOS/"
    cp "$DIR/Assets/Info.Lite.plist" "$APP_LITE_X64/Contents/Info.plist"
    cp "$DIR/Assets/logo.png" "$APP_LITE_X64/Contents/Resources/"
    [ -f "$DIR/Assets/logo.icns" ] && cp "$DIR/Assets/logo.icns" "$APP_LITE_X64/Contents/Resources/"
    chmod +x "$APP_LITE_X64/Contents/MacOS/AgilicoDiagMac"
    build_dmg_helper "$APP_LITE_X64" "$OUTPUT_DIR/Agilico_MSP_Toolkit_Lite_x64.dmg" "Agilico Lite x64" "Agilico MSP Toolkit Lite.app"
fi

if ! command -v hdiutil >/dev/null 2>&1; then
    echo "hdiutil not found; generating disk images using build_dmg.py..."
    python3 "$DIR/build_dmg.py"
fi

echo "=========================================================="
echo " macOS Build & Packaging Complete!"
echo " Artifacts available in: $OUTPUT_DIR"
echo "=========================================================="
