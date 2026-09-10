#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
make_loader_index.py — Loaders folder ရဲ့ remote index ထုတ်တဲ့ tool (PMK သုံး)
================================================================================
Loaders တွေ zip ထဲ မပါတော့တဲ့အတွက် tool က brand/model စာရင်း မြင်ဖို့နဲ့
loader download လုပ်ဖို့ ဒီ index ကို သုံးတယ်။

Usage:
    python tools/make_loader_index.py            # loaders/index.json ပြန်ထုတ်
    python tools/make_loader_index.py --git      # commit + push ပါ လုပ်

Loader အသစ်ထည့်/ဖျက်တိုင်း ဒီ script run ပြီး push ပေးရင်
ဆိုင်တွေရဲ့ tool က စာရင်းအသစ် ရပြီး လိုအပ်တဲ့ loader ကို ကိုယ်တိုင်ယူပါတယ်။
"""
import argparse
import hashlib
import json
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOADERS = os.path.join(ROOT, "LoaderDB", "Loaders")
PROJECT = os.path.join(ROOT, "LoaderDB")  # p က "Loaders/..." နဲ့ စတယ် (app ဘက်က ဒီပုံစံပဲ မျှော်တယ်)
OUT = os.path.join(ROOT, "loaders", "index.json")
EXTS = {".elf", ".mbn", ".bin", ".melf"}
NO_EXT_MIN_SIZE = 100_000  # extension မပါတဲ့ firehose/programmer ဖိုင်တွေ (size ဒီထက် ကြီးမှ loader)


def is_loader(full: str, name: str) -> bool:
    ext = os.path.splitext(name)[1].lower()
    if ext in EXTS:
        return True
    # extension မပါဘဲ ကြီးတဲ့ ဖိုင်တွေ = extension မပါတဲ့ programmer တွေ (digest/sig/QLM လေးတွေ မပါအောင် size စစ်)
    if ext == "" and os.path.getsize(full) >= NO_EXT_MIN_SIZE:
        return True
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--git", action="store_true", help="commit + push ပါ လုပ်")
    args = ap.parse_args()

    if not os.path.isdir(LOADERS):
        sys.exit(f"Loaders folder မတွေ့ဘူး: {LOADERS}")

    files = []
    for dirpath, dirnames, filenames in os.walk(LOADERS):
        dirnames[:] = [d for d in dirnames if not d.startswith("_")]  # _backup လို folder တွေ ကျော်
        for f in sorted(filenames):
            full = os.path.join(dirpath, f)
            if not is_loader(full, f):
                continue
            rel = os.path.relpath(full, PROJECT).replace("\\", "/")  # "Loaders/..." (WinFormsApp1 အောက်ကနေ)
            h = hashlib.sha256()
            with open(full, "rb") as fh:
                for chunk in iter(lambda: fh.read(1024 * 1024), b""):
                    h.update(chunk)
            files.append({"p": rel, "s": os.path.getsize(full), "h": h.hexdigest()})

    files.sort(key=lambda e: e["p"].lower())
    data = {"version": 1, "count": len(files), "files": files}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, separators=(",", ":"))
    print(f"index.json: {len(files)} loaders, {os.path.getsize(OUT)/1024:.0f} KB")

    if args.git:
        subprocess.run(["git", "add", "loaders/index.json"], cwd=ROOT, check=True)
        subprocess.run(["git", "commit", "-m", f"chore(loaders): refresh index ({len(files)} files)"], cwd=ROOT, check=True)
        subprocess.run(["git", "push"], cwd=ROOT, check=True)
        print("Pushed — ဆိုင်တွေရဲ့ tool က index အသစ် ရပါပြီ")


if __name__ == "__main__":
    main()
