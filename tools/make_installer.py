#!/usr/bin/env python3
"""
PMK Unlock Tool — EXE Installer builder (Inno Setup)

လုပ်တာ:
  1) csproj ကနေ version ဖတ်
  2) Publish\\win-x64 ထဲ dotnet publish (--skip-publish မပေးရင်)
  3) Inno Setup (ISCC) နဲ့ Releases\\PMK-Unlock-Tool-Setup-v<ver>.exe ဆောက်
  4) --upload ပေးရင် GitHub release မှာ installer ကိုပါ တင်ပေး

သုံးပုံ:
  python tools/make_installer.py                    # publish + installer
  python tools/make_installer.py --skip-publish     # publish ကျော် (အမြန်)
  python tools/make_installer.py --upload           # installer ကို GH release မှာ တင်
"""
import argparse
import pathlib
import re
import shutil
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
CSPROJ = ROOT / "WinFormsApp1" / "WinFormsApp1.csproj"
PUBLISH_DIR = ROOT / "Publish" / "win-x64"
ISS = ROOT / "tools" / "installer" / "PMK-Unlock-Tool.iss"
RELEASES = ROOT / "Releases"
ISCC_CANDIDATES = [
    r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    r"C:\Program Files\Inno Setup 6\ISCC.exe",
    str(pathlib.Path.home() / "AppData" / "Local" / "Programs" / "Inno Setup 6" / "ISCC.exe"),
]


def read_version() -> str:
    m = re.search(r"<Version>([^<]+)</Version>", CSPROJ.read_text(encoding="utf-8"))
    if not m:
        sys.exit("[ERROR] csproj ထဲမှာ <Version> မတွေ့ပါ")
    return m.group(1).strip()


def find_iscc() -> str:
    for c in ISCC_CANDIDATES:
        if pathlib.Path(c).exists():
            return c
    found = shutil.which("ISCC") or shutil.which("iscc")
    if found:
        return found
    sys.exit("[ERROR] Inno Setup (ISCC.exe) မတွေ့ပါ — https://jrsoftware.org/isdl.php ကနေ install လုပ်ပါ")


def run(cmd, **kw) -> int:
    print(">>", " ".join(cmd), flush=True)
    return subprocess.call(cmd, **kw)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--skip-publish", action="store_true", help="dotnet publish ကို ကျော်")
    ap.add_argument("--upload", action="store_true", help="installer ကို GitHub release မှာ တင်")
    ap.add_argument("--repo", default="huaxinmobile-alt/PMK-Unlock-Tool")
    args = ap.parse_args()

    version = read_version()
    print(f"[1/3] Version: {version}")

    if not args.skip_publish:
        print("[2/3] dotnet publish (win-x64, self-contained)...")
        rc = run([
            "dotnet", "publish", str(ROOT / "WinFormsApp1" / "WinFormsApp1.csproj"),
            "-c", "Release", "-r", "win-x64", "--self-contained", "true",
            "-o", str(PUBLISH_DIR),
        ])
        if rc != 0:
            sys.exit("[ERROR] publish မအောင်မြင်ပါ")
    else:
        print("[2/3] publish ကျော်")

    if not PUBLISH_DIR.exists() or not any(PUBLISH_DIR.iterdir()):
        sys.exit(f"[ERROR] Publish folder မရှိပါ: {PUBLISH_DIR}")

    iscc = find_iscc()
    print(f"[3/3] Inno Setup: {iscc}")
    RELEASES.mkdir(exist_ok=True)
    rc = run([iscc, f"/DMyAppVersion={version}", str(ISS)])
    if rc != 0:
        sys.exit("[ERROR] installer build မအောင်မြင်ပါ")

    out = RELEASES / f"PMK-Unlock-Tool-Setup-v{version}.exe"
    if not out.exists():
        sys.exit(f"[ERROR] installer ဖိုင် မတွေ့ပါ: {out}")
    size_mb = out.stat().st_size / 1024 / 1024
    print(f"\n✅ Installer: {out}  ({size_mb:.1f} MB)")

    if args.upload:
        print(f">> gh release upload v{version} ...")
        rc = run(["gh", "release", "upload", f"v{version}", str(out),
                  "--repo", args.repo, "--clobber"])
        if rc != 0:
            print("[WARN] upload မအောင်မြင်ပါ — release ရှိမရှိ စစ်ပါ (gh release view)")
        else:
            print("✅ GitHub release မှာ installer တင်ပြီး")


if __name__ == "__main__":
    main()
