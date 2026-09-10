#!/usr/bin/env python3
"""
bkerler/Loaders (https://github.com/bkerler/Loaders) က firehose loaders တွေကို
PMK Unlock Tool ရဲ့ LoaderDB/Loaders/<Brand>/<Model>.ext စံနှုန်းအတိုင်း ထည့်တယ်.

မူဝါဒ (အရေးကြီး):
  bkerler ရဲ့ ဖိုင်နာမည် ပုံစံက  <hwid>_<pkhash>_fhprg[_hint].bin  ဖြစ်တယ် —
  ဖိုင်အများစု (၉၃%) မှာ model အချက်အလက် လုံးဝ မပါဘူး။ သင့် tool က brand folder ထဲက
  ဖိုင်နာမည်တိုင်းကို "Model" စာရင်းအဖြစ် ပြတာမို့ hash နာမည်တွေ ထည့်ရင် dropdown
  အမှိုက်တွေ ဖြစ်မယ်။ ဒါကြောင့် **model hint ပါတဲ့ ဖိုင်တွေကိုပဲ** ထည့်တယ်၊
  နာမည်ကိုလည်း <Model>.bin (သင့်စံနှုန်း) ပြောင်းပြီး မူရင်းနာမည်ကို log မှာ သိမ်းထားတယ်.

သုံးပုံ:
  python tools/import_bkerler_loaders.py              # dry-run (ဘာတွေ ထည့်မလဲ ပြရုံ)
  python tools/import_bkerler_loaders.py --apply      # တကယ် download + copy
  python tools/import_bkerler_loaders.py --apply --reindex   # ပြီးရင် index.json ပါ refresh
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOADER_ROOT = os.path.join(ROOT, "LoaderDB", "Loaders")
LOG_PATH = os.path.join(ROOT, "LoaderDB", "bkerler_import_log.txt")
RAW = "https://raw.githubusercontent.com/bkerler/Loaders/main/"

# bkerler brand folder → သင့် tool ရဲ့ brand folder နာမည်
BRAND_MAP = {
    "xiaomi": "Xiaomi", "samsung": "Samsung", "oppo": "Oppo", "vivo": "Vivo",
    "oneplus": "Oneplus", "huawei": "Huawei", "LG": "LG", "zte": "ZTE",
    "meizu": "Meizu", "lenovo_motorola": "Motorola", "asus_wingtech": "Asus",
    "nothing": "Nothing", "sharp": "Sharp", "sony": "Sony",
    "nokia_foxconn": "Nokia", "TCL": "TCL", "micromax": "Micromax",
    "blackshark": "BlackShark", "gionee": "Gionee", "amazon": "Amazon",
    "realme": "Realme", "letv": "Letv", "smartisan": "Smartisan",
    "meitu": "Meitu", "haier": "Haier", "hisense_agm": "Hisense",
}

HINT_RE = re.compile(r'_(fhprg|fhprgl|fhloader|firehose)_([A-Za-z0-9][A-Za-z0-9_\-\.]{1,40})\.(bin|mbn|elf)$', re.I)
# model မဟုတ်တဲ့ hint တွေ
NOT_MODEL = {"peek", "edlauth", "auth", "firehose", "loader", "prog", "test", "unknown", "empty", "ddr"}


def fetch_tree() -> list:
    """bkerler/Loaders repo tree (gh CLI ရှိရင် သုံး၊ မရရှင် API တိုက်ရိုက်)"""
    try:
        out = subprocess.run(["gh", "api", "repos/bkerler/Loaders/git/trees/main?recursive=1"],
                             capture_output=True, text=True, check=True).stdout
        return json.loads(out)["tree"]
    except Exception:
        req = urllib.request.Request("https://api.github.com/repos/bkerler/Loaders/git/trees/main?recursive=1",
                                     headers={"User-Agent": "pmk-import"})
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.loads(r.read().decode("utf-8"))["tree"]


def clean_model(hint: str) -> str:
    """hint ကနေ model နာမည် သန့်စင် (peek_ / _peek ဖယ်)"""
    h = hint.strip("_-. ")
    h = re.sub(r'^peek[_\-.]+', '', h, flags=re.I)
    h = re.sub(r'[_\-.]+peek$', '', h, flags=re.I)
    return h.strip("_-. ")


def plan(tree: list) -> list:
    """ထည့်ရမယ့် ဖိုင်စာရင်း: (brand_folder, model, ext, bkerler_path, size)"""
    items = []
    for x in tree:
        if x.get("type") != "blob":
            continue
        p = x["path"]
        parts = p.split("/")
        if len(parts) != 2:
            continue
        brand_src, fname = parts
        if brand_src not in BRAND_MAP:
            continue
        m = HINT_RE.search(fname)
        if not m:
            continue
        model = clean_model(m.group(2))
        if not model or model.lower() in NOT_MODEL:
            continue
        if model.isdigit() or re.fullmatch(r'[0-9a-f]{16}', model, re.I):
            continue
        ext = "." + m.group(3).lower()
        items.append((BRAND_MAP[brand_src], model, ext, p, int(x.get("size", 0))))
    # model နာမည် ထပ်နေရင် ကြီးတဲ့ဖိုင် (နောက်ဆုံး version) ကို ယူ
    best = {}
    for it in items:
        key = (it[0].lower(), it[1].lower())
        if key not in best or it[4] > best[key][4]:
            best[key] = it
    return sorted(best.values(), key=lambda t: (t[0].lower(), t[1].lower()))


def download(url: str, dest: str) -> bool:
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "pmk-import"})
        with urllib.request.urlopen(req, timeout=120) as r, open(dest, "wb") as f:
            data = r.read()
            f.write(data)
        return len(data) > 0
    except Exception as ex:
        print(f"    ❌ download error: {ex}")
        return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="တကယ် download + copy လုပ်")
    ap.add_argument("--reindex", action="store_true", help="ပြီးရင် loaders/index.json refresh")
    ap.add_argument("--tree", default=os.path.join(ROOT, ".zcode", "tmp", "bkerler_tree.json"),
                    help="tree json cache (မရှိရင် fetch)")
    args = ap.parse_args()

    if os.path.isfile(args.tree):
        tree = json.loads(open(args.tree, encoding="utf-8").read())["tree"]
        print(f"tree cache: {args.tree}")
    else:
        print("fetching bkerler/Loaders tree…")
        tree = fetch_tree()

    items = plan(tree)
    total_mb = sum(i[4] for i in items) / 1048576
    print(f"\nထည့်ရန် ဖိုင်: {len(items)}  ·  {total_mb:.1f} MB  (model hint ပါတဲ့ဖိုင်တွေပဲ)")
    brands = {}
    for brand, model, ext, p, size in items:
        brands.setdefault(brand, []).append(model)
    for b in sorted(brands):
        print(f"  {b:10} {len(brands[b]):3} ဖိုင်  →  {', '.join(sorted(brands[b])[:6])}{'…' if len(brands[b])>6 else ''}")

    if not args.apply:
        print("\n(dry-run) တကယ်ထည့်ရန် --apply ထည့်ပါ")
        return

    os.makedirs(LOADER_ROOT, exist_ok=True)
    log_lines, added, skipped, failed = [], 0, 0, 0
    for brand, model, ext, path, size in items:
        dest_dir = os.path.join(LOADER_ROOT, brand)
        os.makedirs(dest_dir, exist_ok=True)
        dest = os.path.join(dest_dir, model + ext)
        if os.path.isfile(dest) and os.path.getsize(dest) == size:
            skipped += 1
            continue
        print(f"  ⬇ {path}  →  {brand}/{model}{ext}")
        if not download(RAW + urllib.parse.quote(path), dest):
            failed += 1
            continue
        sha = hashlib.sha256(open(dest, "rb").read()).hexdigest()
        log_lines.append(f"{brand}/{model}{ext}\tsha256={sha}\tsize={size}\tsource={path}")
        added += 1

    if log_lines:
        header = f"# bkerler/Loaders import — {__import__('datetime').datetime.now():%Y-%m-%d %H:%M}\n"
        old = ""
        if os.path.isfile(LOG_PATH):
            old = open(LOG_PATH, encoding="utf-8").read()
        with open(LOG_PATH, "w", encoding="utf-8") as f:
            f.write(header + "\n".join(log_lines) + "\n" + old)

    print(f"\n✅ ထည့်ပြီး: {added}  ·  ကျော် (ရှိပြီးသား): {skipped}  ·  မအောင်: {failed}")
    print(f"   log: {LOG_PATH}")

    if args.reindex:
        print("\nindex.json refresh…")
        rc = subprocess.call([sys.executable, os.path.join(ROOT, "tools", "make_loader_index.py")], cwd=ROOT)
        print("reindex rc =", rc)
    else:
        print("index.json refresh လုပ်ရန်: python tools/make_loader_index.py")


if __name__ == "__main__":
    import urllib.parse  # noqa: E402  (download loop မှာ သုံး)
    main()
