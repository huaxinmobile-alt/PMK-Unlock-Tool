#!/usr/bin/env python3
"""
update/latest.json (update manifest) ကို လက်ဖြင်း ပြင်ရန် helper.

သုံးပုံ:
  python tools/update_version.py 4.0.22                      # version + minimum_required = 4.0.22
  python tools/update_version.py 4.0.22 4.0.20 "Bug fixes"    # minimum_required ကို သီးသန့် သတ်မှတ်
  python tools/update_version.py --min 4.0.20                 # minimum_required ကိုပဲ ပြောင်း (version မထိ)
  python tools/update_version.py --show                       # လက်ရှိ manifest ပြ

မှတ်ချက်:
  · ဒီ script က version/minimum_required/changelog/release_date/download_url ကို ရေးတယ်။
  · url / sha256 / size / notes (in-app self-update အတွက် လိုတာ) ကို ရှိပြီးသားအတိုင်း ဆက်ထားတယ် —
    အဲဒါတွေကို tools/make_release.py က release တင်တိုင်း အလိုအလျောက် ရေးပေးတယ်။
  · minimum_required ကို မြှင့်လိုက်ရင် အဲဒီ version အောက် tool တွေက login မဝင်နိုင်တော့ဘူး
    (UpdateGate က ဖိတ်ပြီး update လုပ်ခိုင်း) — သတိထားပြီး မြှင့်ပါ။
"""
import argparse
import json
import os
import sys
from datetime import datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MANIFEST = os.path.join(ROOT, "update", "latest.json")
RELEASES_URL = "https://github.com/huaxinmobile-alt/PMK-Unlock-Tool/releases/latest"


def load() -> dict:
    if not os.path.isfile(MANIFEST):
        return {}
    with open(MANIFEST, "r", encoding="utf-8") as f:
        return json.load(f)


def save(data: dict) -> None:
    os.makedirs(os.path.dirname(MANIFEST), exist_ok=True)
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")


def main():
    ap = argparse.ArgumentParser(description="update/latest.json ကို ပြင်")
    ap.add_argument("version", nargs="?", help="version အသစ် (ဥပမာ 4.0.22)")
    ap.add_argument("minimum_required", nargs="?", help="အနည်းဆုံး လိုအပ်တဲ့ version (မပေးရင် version အတိုင်း)")
    ap.add_argument("changelog", nargs="?", default="", help="အတိုချုပ် ပြောင်းလဲမှု မှတ်တမ်း")
    ap.add_argument("--min", dest="min_only", help="minimum_required ကိုပဲ ပြောင်း (version မထိ)")
    ap.add_argument("--changelog", dest="changelog_opt", default=None, help="changelog သီးသန့်")
    ap.add_argument("--show", action="store_true", help="လက်ရှိ manifest ပြ")
    args = ap.parse_args()

    data = load()

    if args.show:
        print(json.dumps(data, indent=2, ensure_ascii=False))
        return

    if args.min_only:
        if not data.get("version"):
            sys.exit("[ERROR] manifest မရှိသေးပါ — version ကို အရင် သတ်မှတ်ပါ")
        data["minimum_required"] = args.min_only.strip()
        save(data)
        print(f"minimum_required = {data['minimum_required']}  (version {data['version']} မထိ)")
        print("⚠️  အဲဒီ version အောက် tool တွေ login မဝင်နိုင်တော့ပါ — update လုပ်ခိုင်းမယ်")
        return

    if not args.version:
        ap.print_help()
        sys.exit(1)

    version = args.version.strip()
    data["version"] = version
    data["minimum_required"] = (args.minimum_required or version).strip()
    data["download_url"] = data.get("download_url") or RELEASES_URL
    data["changelog"] = (args.changelog_opt if args.changelog_opt is not None
                         else (args.changelog or data.get("changelog") or ""))
    data["release_date"] = datetime.now().strftime("%Y-%m-%d")
    save(data)

    print(f"Updated to version {version}")
    print(f"  minimum_required : {data['minimum_required']}")
    print(f"  download_url     : {data['download_url']}")
    if data.get("sha256"):
        print(f"  sha256 (keep)    : {data['sha256'][:16]}…")
    if data["minimum_required"] != version:
        print("  note: minimum_required < version — force update မဖြစ်သေး (ဟောင်းတွေ ဆက်ဝင်လို့ရ)")


if __name__ == "__main__":
    main()
