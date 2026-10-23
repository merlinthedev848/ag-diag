#!/bin/bash
# Agilico MSP Toolkit - Standalone macOS DMG Creator
set -e

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
OUTPUT_DIR="$DIR/publish_output"

echo "=========================================================="
echo " Packaging Agilico MSP Toolkit into macOS DMG Installers"
echo "=========================================================="

create_one_dmg() {
    local APP_PATH="$1"
    local DMG_PATH="$2"
    local VOL_NAME="$3"

    if [ ! -d "$APP_PATH" ]; then
        echo "[!] App bundle not found: $APP_PATH"
        return
    fi

    echo "[*] Creating DMG for: $APP_PATH -> $DMG_PATH"
    if command -v hdiutil >/dev/null 2>&1; then
        local STAGING="$OUTPUT_DIR/temp_dmg_staging_$$"
        rm -rf "$STAGING"
        mkdir -p "$STAGING"
        cp -r "$APP_PATH" "$STAGING/Agilico MSP Toolkit.app"
        ln -s /Applications "$STAGING/Applications"
        hdiutil create -volname "$VOL_NAME" -srcfolder "$STAGING" -ov -format UDZO "$DMG_PATH"
        rm -rf "$STAGING"
        echo "[+] Successfully generated: $DMG_PATH"
    else
        echo "[*] Running python build_dmg.py..."
        python3 "$DIR/build_dmg.py"
    fi
}

create_one_dmg "$OUTPUT_DIR/Agilico_MSP_Toolkit_arm64.app" "$OUTPUT_DIR/Agilico_MSP_Toolkit_arm64.dmg" "Agilico MSP ARM64"
create_one_dmg "$OUTPUT_DIR/Agilico_MSP_Toolkit_x64.app" "$OUTPUT_DIR/Agilico_MSP_Toolkit_x64.dmg" "Agilico MSP x64"

echo "=========================================================="
echo " DMG Creation Process Complete!"
echo "=========================================================="
