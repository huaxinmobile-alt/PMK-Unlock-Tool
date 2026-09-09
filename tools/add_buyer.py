#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
add_buyer.py — ဝယ်ယူသူရဲ့ Gmail ကို ခွင့်ပြုစာရင်း ထည့်တဲ့ tool
==================================================
Usage:
    python tools/add_buyer.py buyer1@gmail.com [buyer2@gmail.com ...]
    python tools/add_buyer.py buyer@gmail.com --git   # commit + push ပါ လုပ်

ဘာတွေ လုပ်ပေးလဲ:
    update/allowed_emails.json ထဲ email တွေကို sha256 hash နဲ့ ထည့်တယ်
    (public repo မှာ ဘယ်သူတွေ ဝယ်ထားလဲ မပေါ်အောင် — email အရှင် မသိမ်းဘူး)

ထည့်ပြီးရင် အဲဒီသူက ကိုယ့် PC မှာ tool ဖွင့်ပြီး
"Gmail နဲ့ register" ကို နှိပ်ရင် သူ့ Gmail နဲ့ account ဖွင့်လို့ရပြီ။

⚠️ Salt က WinFormsApp1/GoogleAuth.cs ထဲက EmailSalt နဲ့ တူရမယ် (pmk-allow-v1:)
"""
import argparse
import hashlib
import json
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LIST_PATH = os.path.join(ROOT, "update", "allowed_emails.json")
SALT = "pmk-allow-v1:"
REPO = "huaxinmobile-alt/PMK-Unlock-Tool"


def email_hash(email: str) -> str:
    return hashlib.sha256((SALT + email.strip().lower()).encode("utf-8")).hexdigest()


def load() -> list:
    if os.path.exists(LIST_PATH):
        with open(LIST_PATH, encoding="utf-8") as f:
            data = json.load(f)
        return list(data.get("hashes", []))
    return []


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("emails", nargs="+", help="Gmail လိပ်စာတွေ (space ခြားပြီး အများကြီးထည့်လို့ရ)")
    ap.add_argument("--git", action="store_true", help="commit + push ပါ လုပ်")
    args = ap.parse_args()

    hashes = set(load())
    for raw in args.emails:
        email = raw.strip().lower()
        if "@" not in email:
            sys.exit(f"'{raw}' က email ပုံစံ မဟုတ်ဘူး")
        hashes.add(email_hash(email))
        print(f"+ {email}")

    os.makedirs(os.path.dirname(LIST_PATH), exist_ok=True)
    with open(LIST_PATH, "w", encoding="utf-8") as f:
        json.dump({"hashes": sorted(hashes)}, f, indent=2)
    print(f"Saved: {LIST_PATH} ({len(hashes)} entries)")

    if args.git:
        subprocess.run(["git", "add", "update/allowed_emails.json"], cwd=ROOT, check=True)
        subprocess.run(["git", "commit", "-m", "chore(update): allow buyer gmail(s)"], cwd=ROOT, check=True)
        subprocess.run(["git", "push"], cwd=ROOT, check=True)
        print("Pushed to GitHub — buyer က အခု register လုပ်လို့ရပြီ")


if __name__ == "__main__":
    main()
