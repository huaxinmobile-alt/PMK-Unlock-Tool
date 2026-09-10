#!/usr/bin/env python3
"""
PMK License Maker — GUI ကို self-contained EXE အဖြစ် ဆောက်တယ် (PMK ရဲ့ PC အတွက်သာ).

  python tools/make_license_maker.py            # build + publish
  python tools/make_license_maker.py --run      # build ပြီး ဖွင့်ပြပါ

ထွက်တဲ့ ဖိုင်: LicenseMaker\\Publish\\PMK License Maker.exe
  · private key (tools\\private\\activation_key.pem) ကို runtime မှာ ရှာတယ် — EXE ထဲ မထည့်ဘူး
  · ⚠️ ဒီ exe ကို release zip/installer ထဲ (ဝယ်သူတွေဆီ) ဘယ်တော့မှ မထည့်ရ
"""
import argparse
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJ = os.path.join(ROOT, "LicenseMaker", "PMKLicenseMaker.csproj")
OUT = os.path.join(ROOT, "LicenseMaker", "Publish")
EXE = os.path.join(OUT, "PMK License Maker.exe")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--run", action="store_true", help="build ပြီး ချက်ချင်း ဖွင့်")
    ap.add_argument("--framework-dependent", action="store_true",
                    help="self-contained မလုပ်ဘဲ .NET runtime ရှိရင် သုံး (ဖိုင် သေး)")
    args = ap.parse_args()

    cmd = ["dotnet", "publish", PROJ, "-c", "Release", "-r", "win-x64", "-o", OUT]
    if not args.framework_dependent:
        cmd += ["--self-contained", "true"]
        cmd += ["-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true"]
    else:
        cmd += ["--self-contained", "false"]

    print(">>", " ".join(cmd))
    rc = subprocess.call(cmd, cwd=ROOT)
    if rc != 0:
        sys.exit("[ERROR] build မအောင်ဘူး")

    if not os.path.isfile(EXE):
        sys.exit(f"[ERROR] exe မတွေ့ဘူး: {EXE}")
    print(f"\n✅ License Maker: {EXE}  ({os.path.getsize(EXE) / 1048576:.1f} MB)")
    print("   private key ကို tools\\private\\activation_key.pem ကနေ ရှာတယ် (exe ထဲ မပါ)")
    print("   ⚠️ ဒီ exe ကို ဝယ်သူတွေဆီ ဘယ်တော့မှ မဖြန့်ရ")

    if args.run:
        subprocess.Popen([EXE], cwd=OUT)


if __name__ == "__main__":
    main()
