#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
make_license.py — ဝယ်သူရဲ့ PC အတွက် license ထုတ်ပေးတဲ့ tool (PMK သုံး)
=======================================================================
Usage:
    python tools/make_license.py <Installation-ID> "ဆိုင်နာမည်" [days]
    python tools/make_license.py ABCD-EFGH-... "Mandalay Mobile Shop"
    python tools/make_license.py ABCD-EFGH-... "Shop" 30        # 30 ရက် သက်တမ်း

အဆင့်တွေ:
    1. ဝယ်သူက tool ထဲက Installation ID ပို့မယ်
    2. ဒီ script run → license string တစ်ကြောင်း ထွက်မယ် (copy လုပ်ပြီး ပြန်ပို့)
    3. ဝယ်သူက tool ထဲ paste လုပ်ရင် အဲဒီ PC မှာ activate ဖြစ်မယ်

License က ဒီ PC နဲ့ပဲ အလုပ်လုပ်တယ် (တစ်ခြား PC ကော်ပီကူးသွားရင် မရဘူး)
Private key က tools/private/activation_key.pem — ဒီဖိုင် မပျောက်အောင် backup ထားပါ!
"""
import argparse
import datetime
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KEY = os.path.join(ROOT, "tools", "private", "activation_key.pem")
PREFIX = "pmklic1"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("machine_id", help="ဝယ်သူ tool ထဲက Installation ID (ABCD-... 8 group)")
    ap.add_argument("shop", help="ဆိုင်နာမည် (license ထဲ မှတ်တမ်းအနေနဲ့ ပါမယ်)")
    ap.add_argument("days", nargs="?", type=int, default=None, help="သက်တမ်း (ရက်) — မထည့်ရင် အမြဲ")
    args = ap.parse_args()

    if not os.path.exists(KEY):
        sys.exit(f"Private key မတွေ့ဘူး: {KEY}\nပထမဆုံး keypair ထုတ်ဖို့ README ကြည့်ပါ (tools/private/)")

    mid = args.machine_id.strip().replace("-", "").lower()
    if len(mid) != 32 or any(c not in "0123456789abcdef" for c in mid):
        sys.exit("Installation ID ပုံစံ မမှန်ဘူး (hex 32 လုံး ရှိရမယ်)")

    shop = args.shop.strip().replace("|", "_").replace("\n", " ")
    if not shop:
        sys.exit("ဆိုင်နာမည် ထည့်ပါ")

    expiry = "none"
    if args.days is not None:
        expiry = (datetime.date.today() + datetime.timedelta(days=args.days)).isoformat()

    payload = f"{PREFIX}|{mid}|{shop}|{expiry}"
    sig_file = os.path.join(ROOT, "tools", "private", "_sig.tmp")
    payload_file = os.path.join(ROOT, "tools", "private", "_payload.tmp")
    try:
        with open(payload_file, "w", encoding="utf-8") as f:
            f.write(payload)
        subprocess.run(
            ["openssl", "dgst", "-sha256", "-sign", KEY, "-out", sig_file, payload_file],
            check=True, capture_output=True)
        with open(sig_file, "rb") as f:
            import base64
            sig_b64 = base64.b64encode(f.read()).decode("ascii")
        print()
        print("=" * 78)
        print("LICENSE (ဒီစာကြောင်း တစ်ကြောင်းလုံး ကော်ပီလုပ်ပြီး ဝယ်သူကို ပို့ပါ):")
        print("=" * 78)
        print(payload + "|" + sig_b64)
        print("=" * 78)
        print(f"Shop: {shop}   Expiry: {expiry}   Machine: {mid[:8]}...")
    finally:
        for p in (sig_file, payload_file):
            try:
                os.remove(p)
            except OSError:
                pass


if __name__ == "__main__":
    main()
