#!/bin/bash
set -e

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
OUTPUT_DIR="$DIR/publish_output"

echo "=========================================================="
echo " Building Agilico MSP Toolkit for macOS"
echo "=========================================================="

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

echo "[1/2] Publishing Apple Silicon (osx-arm64)..."
dotnet publish -c Release -r osx-arm64 --self-contained -o "$OUTPUT_DIR/osx-arm64" "$DIR"

APP_ARM="$OUTPUT_DIR/Agilico_MSP_Toolkit_arm64.app"
mkdir -p "$APP_ARM/Contents/MacOS"
mkdir -p "$APP_ARM/Contents/Resources"
cp -r "$OUTPUT_DIR/osx-arm64/"* "$APP_ARM/Contents/MacOS/"
cp "$DIR/Assets/Info.plist" "$APP_ARM/Contents/"
cp "$DIR/Assets/logo.png" "$APP_ARM/Contents/Resources/"
chmod +x "$APP_ARM/Contents/MacOS/AgilicoDiagMac"

echo "[2/2] Publishing Intel Mac (osx-x64)..."
dotnet publish -c Release -r osx-x64 --self-contained -o "$OUTPUT_DIR/osx-x64" "$DIR"

APP_X64="$OUTPUT_DIR/Agilico_MSP_Toolkit_x64.app"
mkdir -p "$APP_X64/Contents/MacOS"
mkdir -p "$APP_X64/Contents/Resources"
cp -r "$OUTPUT_DIR/osx-x64/"* "$APP_X64/Contents/MacOS/"
cp "$DIR/Assets/Info.plist" "$APP_X64/Contents/"
cp "$DIR/Assets/logo.png" "$APP_X64/Contents/Resources/"
chmod +x "$APP_X64/Contents/MacOS/AgilicoDiagMac"

echo "=========================================================="
echo " macOS Build Complete!"
echo " Apps available in: $OUTPUT_DIR"
echo "=========================================================="
