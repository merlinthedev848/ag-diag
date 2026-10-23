#!/bin/bash
set -e

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
OUTPUT_DIR="$DIR/publish_output"

echo "=========================================================="
echo " Building Agilico MSP Toolkit for macOS"
echo "=========================================================="

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

echo "[1/3] Publishing Apple Silicon (osx-arm64)..."
dotnet publish -c Release -r osx-arm64 --self-contained -o "$OUTPUT_DIR/osx-arm64" "$DIR"

APP_ARM="$OUTPUT_DIR/Agilico_MSP_Toolkit_arm64.app"
mkdir -p "$APP_ARM/Contents/MacOS"
mkdir -p "$APP_ARM/Contents/Resources"
cp -r "$OUTPUT_DIR/osx-arm64/"* "$APP_ARM/Contents/MacOS/"
cp "$DIR/Assets/Info.plist" "$APP_ARM/Contents/"
cp "$DIR/Assets/logo.png" "$APP_ARM/Contents/Resources/"
chmod +x "$APP_ARM/Contents/MacOS/AgilicoDiagMac"

echo "[2/3] Publishing Intel Mac (osx-x64)..."
dotnet publish -c Release -r osx-x64 --self-contained -o "$OUTPUT_DIR/osx-x64" "$DIR"

APP_X64="$OUTPUT_DIR/Agilico_MSP_Toolkit_x64.app"
mkdir -p "$APP_X64/Contents/MacOS"
mkdir -p "$APP_X64/Contents/Resources"
cp -r "$OUTPUT_DIR/osx-x64/"* "$APP_X64/Contents/MacOS/"
cp "$DIR/Assets/Info.plist" "$APP_X64/Contents/"
cp "$DIR/Assets/logo.png" "$APP_X64/Contents/Resources/"
chmod +x "$APP_X64/Contents/MacOS/AgilicoDiagMac"

echo "[3/3] Generating macOS .dmg Disk Images..."
if command -v hdiutil >/dev/null 2>&1; then
    echo "Using native Apple hdiutil..."

    # ARM64 DMG
    STAGING_ARM="$OUTPUT_DIR/dmg_arm64"
    mkdir -p "$STAGING_ARM"
    cp -r "$APP_ARM" "$STAGING_ARM/Agilico MSP Toolkit.app"
    ln -s /Applications "$STAGING_ARM/Applications"
    hdiutil create -volname "Agilico MSP Toolkit" -srcfolder "$STAGING_ARM" -ov -format UDZO "$OUTPUT_DIR/Agilico_MSP_Toolkit_arm64.dmg"
    rm -rf "$STAGING_ARM"

    # x64 DMG
    STAGING_X64="$OUTPUT_DIR/dmg_x64"
    mkdir -p "$STAGING_X64"
    cp -r "$APP_X64" "$STAGING_X64/Agilico MSP Toolkit.app"
    ln -s /Applications "$STAGING_X64/Applications"
    hdiutil create -volname "Agilico MSP Toolkit" -srcfolder "$STAGING_X64" -ov -format UDZO "$OUTPUT_DIR/Agilico_MSP_Toolkit_x64.dmg"
    rm -rf "$STAGING_X64"
else
    echo "hdiutil not found; falling back to build_dmg.py..."
    python3 "$DIR/build_dmg.py"
fi

echo "=========================================================="
echo " macOS Build & DMG Packaging Complete!"
echo " Artifacts available in: $OUTPUT_DIR"
echo " - Apple Silicon App: $APP_ARM"
echo " - Intel Mac App:     $APP_X64"
echo " - Apple Silicon DMG: $OUTPUT_DIR/Agilico_MSP_Toolkit_arm64.dmg"
echo " - Intel Mac DMG:     $OUTPUT_DIR/Agilico_MSP_Toolkit_x64.dmg"
echo "=========================================================="
