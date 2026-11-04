"""Builds a Thunderstore-format package zip.

Usage: python3 thunderstore/package.py <Package> <out-dir> <plugin.dll> [<plugin.dll> ...]

Packs thunderstore/<Package>/{manifest.json,README.md,icon.png} and the given DLLs into
<out-dir>/<Package>-<version>.zip. The version is $PACKAGE_VERSION when set (CI sets it so every
release gets a higher number automatically), else the manifest's own version_number. The BepInExPack dependency is set from
$BEPINEX_VERSION when present. Everything is checked against Thunderstore's rules before
zipping.

The zip installs through Thunderstore Mod Manager / r2modman's "Import local mod", or can be
uploaded to thunderstore.io as-is.
"""

import json
import os
import re
import struct
import sys
import zipfile
from pathlib import Path


def fail(message: str) -> None:
    print(f"::error::{message}")
    sys.exit(1)


def main() -> None:
    if len(sys.argv) < 4:
        fail("usage: package.py <Package> <out-dir> <plugin.dll> [<plugin.dll> ...]")

    package_name, out_dir, dlls = sys.argv[1], Path(sys.argv[2]), [Path(p) for p in sys.argv[3:]]
    package_dir = Path(__file__).resolve().parent / package_name

    manifest = json.loads((package_dir / "manifest.json").read_text(encoding="utf-8"))

    package_version = os.environ.get("PACKAGE_VERSION")
    if package_version:
        manifest["version_number"] = package_version

    bepinex_version = os.environ.get("BEPINEX_VERSION")
    if bepinex_version:
        manifest["dependencies"] = [
            f"denikson-BepInExPack_Valheim-{bepinex_version}" if d.startswith("denikson-BepInExPack_Valheim-") else d
            for d in manifest.get("dependencies", [])
        ]

    if manifest.get("name") != package_name or not re.fullmatch(r"[A-Za-z0-9_]+", package_name):
        fail("manifest name must match the folder name and contain only letters, digits and underscores")
    if not re.fullmatch(r"\d+\.\d+\.\d+", manifest.get("version_number", "")):
        fail("manifest version_number must be MAJOR.MINOR.PATCH")
    # Not part of Thunderstore's own manifest format (thunderstore.io takes the author from the
    # uploading team), but r2modman / Thunderstore Mod Manager read it on "Import local mod" -
    # without it the import dialog shows the author as "Unknown".
    if not isinstance(manifest.get("author", ""), str):
        fail("manifest author must be a string")
    if len(manifest.get("description", "")) > 250:
        fail("manifest description must be at most 250 characters")
    if not isinstance(manifest.get("website_url"), str) or not isinstance(manifest.get("dependencies"), list):
        fail("manifest needs a website_url string and a dependencies list")

    icon = (package_dir / "icon.png").read_bytes()
    if icon[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", icon[16:24]) != (256, 256):
        fail("icon.png must be a 256x256 PNG")

    for dll in dlls:
        if not dll.is_file():
            fail(f"{dll} not found")

    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / f"{package_name}-{manifest['version_number']}.zip"
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as package:
        package.writestr("manifest.json", json.dumps(manifest, indent=2) + "\n")
        package.writestr("README.md", (package_dir / "README.md").read_text(encoding="utf-8"))
        package.writestr("icon.png", icon)
        for dll in dlls:
            package.write(dll, dll.name)

    print(f"Wrote {out}: {', '.join(d.name for d in dlls)}")


if __name__ == "__main__":
    main()
