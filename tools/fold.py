#!/usr/bin/env python3
"""Hex fold script for TortalPortal.

Packages the local HexiumDist release archive. Fold only - this never publishes anything;
`hex web` is a separate, explicit step that nobody should reach by accident.

Adheres to the HexiumPublishing conventions used by every previous fold in this repo:
- forward slashes only in zip entry names (Compress-Archive emits backslashes; we never use it)
- deterministic 1980-01-01 timestamps, so the same inputs give a byte-identical archive
- version consistency checked across every place the number lives, before anything is written
- the packed DLL's SHA256 re-verified out of the finished archive against bin/Release

WHY THIS WALKS THE DIST TREE INSTEAD OF A TYPED FILE LIST
--------------------------------------------------------
The sibling mod's fold script carries a hand-written PACKAGE_FILES list. That is exactly how the
1.4.0 packages shipped: someone typed the list, `plugins/TortalPortalPins/` was not on it, and both
archives went out with five entries and none of the eight map-pin icons. Nothing caught it, because
a typed list cannot notice what it forgot.

So this walks HexiumDist and packages what is actually there. The inverse risk is that a tree walk
silently packages something it should not - `HexiumDist/Screenies/` holds README screenshots and is
empty today, so a naive walk happens to be correct right now and would quietly start shipping a few
hundred KB of PNGs the day somebody drops one in. Both failure modes are silent, so neither is
tolerated here: every file found is classified, and anything that is neither required, nor a
recognised pin icon, nor explicitly excluded is a hard error that names the file and stops. Adding a
new file to the package is then a deliberate edit to this script, and forgetting one is impossible.
"""

import hashlib
import json
import pathlib
import re
import sys
import zipfile

FIXED_DATE = (1980, 1, 1, 0, 0, 0)

# Required, and packaged in this order (matches every prior fold's entry order).
REQUIRED = [
    "manifest.json",
    "README.md",
    "CHANGELOG.md",
    "LICENSE.md",
    "icon.png",
    "plugins/TortalPortal.dll",
]

# Map-pin icons: packaged as a set rather than named individually, but the COUNT is asserted, so a
# lost folder fails the fold instead of shipping. Update EXPECTED_PINS when icons are added/removed.
PINS_DIR = "plugins/TortalPortalPins/"
EXPECTED_PINS = 8

# Present in HexiumDist but deliberately NOT in the package. A directory prefix or a suffix.
EXCLUDED_PREFIXES = ["Screenies/"]   # README screenshots - documentation, not payload
EXCLUDED_SUFFIXES = [".zip"]         # previous folds


def fail(message: str) -> int:
    print(f"ERROR: {message}", file=sys.stderr)
    return 1


def main() -> int:
    repo = pathlib.Path(__file__).resolve().parent.parent
    dist = repo / "HexiumDist"
    staged_dll = dist / "plugins" / "TortalPortal.dll"
    built_dll = repo / "bin" / "Release" / "net48" / "TortalPortal.dll"

    print("=== Hex Fold: TortalPortal ===")

    manifest_path = dist / "manifest.json"
    if not manifest_path.is_file():
        return fail(f"missing manifest at {manifest_path}")
    version = json.loads(manifest_path.read_text(encoding="utf-8")).get("version_number", "").strip()
    if not version:
        return fail("manifest.json has no version_number")

    # ---- 1. every place the version lives must agree -------------------------------------------
    print(f"[1/5] Target version: v{version}")

    def site(path: pathlib.Path, pattern: str, label: str):
        if not path.is_file():
            return fail(f"missing {label} at {path}")
        found = re.search(pattern, path.read_text(encoding="utf-8"))
        if not found:
            return fail(f"could not read a version out of {label}")
        if found.group(1) != version:
            return fail(f"{label} says {found.group(1)}, manifest says {version}")
        return None

    checks = [
        (repo / "Plugin.cs", r'PluginVersion = "([^"]+)"', "Plugin.cs PluginVersion"),
        (repo / "TortalPortal.csproj", r"<Version>([^<]+)</Version>", "csproj Version"),
        (repo / "Configuration.cs", r'CurrentVersion = "([^"]+)"', "Configuration.cs ConfigSync CurrentVersion"),
        (dist / "README.md", r"Version-([0-9.]+)-lightgrey", "README.md version badge"),
    ]
    for path, pattern, label in checks:
        bad = site(path, pattern, label)
        if bad is not None:
            return bad
    print(f"      Consistent across {len(checks) + 1} sites (manifest, Plugin.cs, csproj, ConfigSync, README badge).")

    # ---- 2. stage the built DLL and prove it is the one we think it is -------------------------
    print("[2/5] Staging Release DLL...")
    if not built_dll.is_file():
        return fail(f"built DLL not found at {built_dll} - run `~/.dotnet/dotnet build -c Release` first")
    dll_bytes = built_dll.read_bytes()
    staged_dll.parent.mkdir(parents=True, exist_ok=True)
    if not staged_dll.is_file() or staged_dll.read_bytes() != dll_bytes:
        staged_dll.write_bytes(dll_bytes)
        print("      Copied built DLL into HexiumDist/plugins/.")
    else:
        # The csproj PostBuild target already copies it, so this is the usual path.
        print("      HexiumDist/plugins/TortalPortal.dll already matches the build.")
    dll_sha = hashlib.sha256(dll_bytes).hexdigest()

    # A version-stamped assembly that does not contain the version string is a stale DLL.
    if version.encode() not in dll_bytes and version.encode("utf-16-le") not in dll_bytes:
        return fail(f"the built DLL does not embed version {version} - it is stale, rebuild it")
    print(f"      DLL SHA256: {dll_sha}")
    print(f"      DLL embeds v{version}: yes ({len(dll_bytes):,} bytes)")

    # ---- 3. classify everything in the dist tree ------------------------------------------------
    print("[3/5] Reading the dist tree...")
    entries, pins, unexpected = [], [], []
    for path in sorted(dist.rglob("*")):
        if not path.is_file():
            continue
        rel = path.relative_to(dist).as_posix()          # forward slashes, always
        if any(rel.startswith(p) for p in EXCLUDED_PREFIXES) or any(rel.endswith(s) for s in EXCLUDED_SUFFIXES):
            continue
        if rel in REQUIRED:
            continue                                      # added below, in REQUIRED's order
        if rel.startswith(PINS_DIR) and rel.endswith(".png"):
            pins.append(rel)
            continue
        unexpected.append(rel)

    missing = [r for r in REQUIRED if not (dist / r).is_file()]
    if missing:
        return fail(f"dist tree is missing required files: {missing}")
    if unexpected:
        return fail(
            "unclassified file(s) in HexiumDist - decide deliberately whether they belong in the "
            f"package, then update REQUIRED, PINS_DIR or EXCLUDED_* in this script: {unexpected}"
        )
    if len(pins) != EXPECTED_PINS:
        return fail(
            f"expected {EXPECTED_PINS} map-pin icons in {PINS_DIR}, found {len(pins)}. "
            "This is the 1.4.0 regression guard - if the icon set really changed, update EXPECTED_PINS."
        )

    entries = REQUIRED + sorted(pins)
    print(f"      {len(entries)} entries ({len(pins)} pin icons), nothing unclassified.")

    # ---- 4. write it, deterministically ---------------------------------------------------------
    zip_path = dist / f"TortalPortal-v{version}-hexium.zip"
    print(f"[4/5] Packing {zip_path.name}...")
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for rel in entries:
            if "\\" in rel:
                return fail(f"backslash in entry name: {rel}")
            info = zipfile.ZipInfo(rel, date_time=FIXED_DATE)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            zf.writestr(info, (dist / rel).read_bytes())
    print(f"      Wrote {len(entries)} entries with fixed 1980 timestamps.")

    # ---- 5. read the finished archive back and prove it ----------------------------------------
    print(f"[5/5] Verifying {zip_path.name}...")
    with zipfile.ZipFile(zip_path, "r") as zf:
        if zf.testzip() is not None:
            return fail(f"CRC check failed on {zf.testzip()}")
        names = zf.namelist()
        if names != entries:
            return fail(f"entry list mismatch: {sorted(set(names) ^ set(entries))}")
        if any("\\" in n for n in names):
            return fail("a backslash survived into the archive")
        packed_sha = hashlib.sha256(zf.read("plugins/TortalPortal.dll")).hexdigest()
        if packed_sha != dll_sha:
            return fail(f"packed DLL SHA256 {packed_sha} != built {dll_sha}")
        if json.loads(zf.read("manifest.json")).get("version_number") != version:
            return fail("the packed manifest does not carry the version we folded")
    print(f"      Verified {len(names)} entries, {zip_path.stat().st_size:,} bytes.")
    print(f"      Packed DLL hash matches bin/Release ({packed_sha[:16]}...).")

    # ---- retention: HexiumDist keeps ONE zip, the newest fold, and nothing else -----------------
    superseded = [p for p in dist.glob("TortalPortal-v*.zip") if p.name != zip_path.name]
    for old in superseded:
        old.unlink()
        print(f"      Removed superseded archive: {old.name}")

    print(f"\nHex fold complete: {zip_path}")
    print("Nothing was published. hex web is a separate, explicit step.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
