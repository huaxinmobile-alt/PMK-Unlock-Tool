#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
make_release.py — PMK Unlock Tool release packager
===================================================
Steps (run from repo root, after `dotnet publish` or via release.bat):
  1. Zip Publish/win-x64 -> Releases/PMK-Unlock-Tool-v<ver>.zip
     (excludes runtime junk: updates/, device_database/, *.log)
  2. Compute SHA-256 -> write update/latest.json  (this file is committed)
  3. Optional: --git      -> git add/commit/push update/latest.json
  4. Optional: --release  -> also create GitHub release v<ver> + upload zip

Usage:
  python tools/make_release.py [--notes "text"] [--git] [--release]

Version is read automatically from WinFormsApp1/WinFormsApp1.csproj <Version>.
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PUBLISH = os.path.join(ROOT, "Publish", "win-x64")
RELEASES = os.path.join(ROOT, "Releases")
UPDATE_DIR = os.path.join(ROOT, "update")
EXCLUDE_DIRS = {"updates", "device_database", "Publish"}
EXCLUDE_FILES = {"users.dat", "users.dat.corrupt", "users.dat.tmp"}  # shop-local login list — update မှာ မပျက်အောင် zip ထဲ မထည့်ဘူး
REPO = "huaxinmobile-alt/PMK-Unlock-Tool"
EXE = "WinFormsApp1.exe"


def run(cmd, check=True):
    print(">>", " ".join(cmd))
    r = subprocess.run(cmd, cwd=ROOT, shell=False)
    if check and r.returncode != 0:
        sys.exit(f"Command failed: {' '.join(cmd)}")
    return r


def read_version():
    csproj = os.path.join(ROOT, "WinFormsApp1", "WinFormsApp1.csproj")
    with open(csproj, encoding="utf-8") as f:
        m = re.search(r"<Version>(\d+\.\d+(?:\.\d+)*)</Version>", f.read())
    if not m:
        sys.exit("Version not found in csproj")
    return m.group(1)


def build_zip(version):
    os.makedirs(RELEASES, exist_ok=True)
    out = os.path.join(RELEASES, f"PMK-Unlock-Tool-v{version}.zip")
    if not os.path.isfile(os.path.join(PUBLISH, EXE)):
        sys.exit(f"{EXE} not found in {PUBLISH} — run dotnet publish first")
    if os.path.exists(out):
        os.remove(out)
    n = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=1) as z:
        for dirpath, dirnames, filenames in os.walk(PUBLISH):
            dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS and not d.startswith(".")]
            for f in sorted(filenames):
                if f.endswith(".log") or f in EXCLUDE_FILES:
                    continue
                full = os.path.join(dirpath, f)
                z.write(full, os.path.relpath(full, PUBLISH))
                n += 1
    size = os.path.getsize(out)
    sha = hashlib.sha256()
    with open(out, "rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            sha.update(chunk)
    return out, size, sha.hexdigest(), n


def write_manifest(version, notes, size, sha, asset_name):
    os.makedirs(UPDATE_DIR, exist_ok=True)
    m = {
        "version": version,
        "notes": notes or "",
        "url": f"https://github.com/{REPO}/releases/download/v{version}/{asset_name}",
        "sha256": sha,
        "size": size,
    }
    path = os.path.join(UPDATE_DIR, "latest.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(m, f, indent=2)
    print(f"Manifest: {path} -> v{version}, {size / 1048576:.0f} MB")
    return path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--notes", default="")
    ap.add_argument("--git", action="store_true", help="commit + push update/latest.json")
    ap.add_argument("--release", action="store_true", help="--git + create GitHub release + upload")
    args = ap.parse_args()

    version = read_version()
    print(f"Version: {version}")
    asset_name = f"PMK-Unlock-Tool-v{version}.zip"
    zip_path, size, sha, nfiles = build_zip(version)
    print(f"ZIP: {zip_path} ({nfiles} files, {size / 1048576:.0f} MB)")
    print(f"SHA256: {sha}")
    write_manifest(version, args.notes, size, sha, asset_name)

    if args.git or args.release:
        run(["git", "add", "update/latest.json"])
        run(["git", "commit", "-m", f"chore(update): manifest v{version}"])
        run(["git", "push"])

    if args.release:
        notes = args.notes or f"PMK Unlock Tool v{version} — portable package (self-contained, no .NET runtime needed)."
        run(["gh", "release", "create", f"v{version}", zip_path,
             "--title", f"PMK Unlock Tool v{version}", "--notes", notes, "--repo", REPO])
        print(f"Release v{version} published: https://github.com/{REPO}/releases/tag/v{version}")


if __name__ == "__main__":
    main()
