#!/usr/bin/env python3
"""
Agilico MSP Toolkit - macOS DMG Package Generator
Cross-platform disk image builder using Rock Ridge ISO with POSIX permissions and /Applications symlink.
"""

import os
import sys
import shutil

try:
    import pycdlib
except ImportError:
    print("[ERROR] pycdlib is not installed. Run with: uv run --with pycdlib python build_dmg.py")
    sys.exit(1)


def build_dmg(app_source_dir, output_dmg_path, volume_name="Agilico MSP"):
    """
    Packages a macOS .app directory into a mountable .dmg disk image.
    Preserves POSIX executable permissions and includes a symlink to /Applications.
    """
    if not os.path.exists(app_source_dir):
        raise FileNotFoundError(f"Source app bundle not found: {app_source_dir}")

    output_dir = os.path.dirname(output_dmg_path)
    if output_dir and not os.path.exists(output_dir):
        os.makedirs(output_dir, exist_ok=True)

    if os.path.exists(output_dmg_path):
        os.remove(output_dmg_path)

    print(f"[*] Packaging {app_source_dir} -> {output_dmg_path}")
    print(f"    Volume Name: {volume_name}")

    iso = pycdlib.PyCdlib()
    # Rock Ridge 1.09 provides POSIX permissions, symlinks, and long filenames
    iso.new(rock_ridge="1.09", vol_ident=volume_name[:16].upper().replace(" ", "_"))

    # Map directories to ISO 8.3 unique paths
    dir_iso_map = {"": "/"}
    
    # Root level app folder inside the DMG (named "Agilico MSP Toolkit.app")
    app_folder_name = "Agilico MSP Toolkit.app"
    iso.add_directory("/APP", rr_name=app_folder_name)
    dir_iso_map["."] = "/APP"
    
    dir_counter = 0
    file_counter = 0

    # Add Applications symlink at the root of the disk image
    iso.add_symlink(symlink_path="/APPS;1", rr_symlink_name="Applications", rr_path="/Applications")

    # Walk directory structure
    for root, dirs, files in os.walk(app_source_dir):
        rel_root = os.path.relpath(root, app_source_dir)
        parent_iso = dir_iso_map["." if rel_root == "." else rel_root]

        for d in sorted(dirs):
            dir_counter += 1
            rel_dir = os.path.normpath(os.path.join(rel_root, d))
            iso_dir_name = f"D{dir_counter:04d}"
            iso_dir_path = f"{parent_iso}/{iso_dir_name}"
            dir_iso_map[rel_dir] = iso_dir_path
            iso.add_directory(iso_dir_path, rr_name=d)

        for f in sorted(files):
            file_counter += 1
            full_path = os.path.join(root, f)
            iso_file_name = f"F{file_counter:05d}.DAT;1"
            iso_file_path = f"{parent_iso}/{iso_file_name}"

            # Executable mode (0755) for Mach-O binaries and dylibs; standard (0644) for others
            is_executable = (
                f == "AgilicoDiagMac" or
                f.endswith(".dylib") or
                f == "createdump" or
                f.endswith(".sh")
            )
            mode = 0o100755 if is_executable else 0o100644
            iso.add_file(full_path, iso_path=iso_file_path, rr_name=f, file_mode=mode)

    print(f"    Writing {dir_counter} directories and {file_counter} files to disk image...")
    iso.write(output_dmg_path)
    iso.close()

    size_mb = os.path.getsize(output_dmg_path) / (1024 * 1024)
    print(f"[+] Successfully generated DMG: {output_dmg_path} ({size_mb:.2f} MB)")


def main():
    base_dir = os.path.dirname(os.path.abspath(__file__))
    publish_dir = os.path.join(base_dir, "publish_output")

    targets = [
        ("Agilico_MSP_Toolkit_arm64.app", "Agilico_MSP_Toolkit_arm64.dmg", "Agilico MSP ARM64"),
        ("Agilico_MSP_Toolkit_x64.app", "Agilico_MSP_Toolkit_x64.dmg", "Agilico MSP x64"),
    ]

    built_any = False
    for app_name, dmg_name, vol_name in targets:
        app_path = os.path.join(publish_dir, app_name)
        dmg_path = os.path.join(publish_dir, dmg_name)

        if os.path.isdir(app_path):
            build_dmg(app_path, dmg_path, vol_name)
            built_any = True
        else:
            print(f"[!] App bundle not found: {app_path} (run build_macos script first)")

    if not built_any:
        print("[ERROR] No app bundles found in publish_output to package into DMG.")
        sys.exit(1)


if __name__ == "__main__":
    main()
