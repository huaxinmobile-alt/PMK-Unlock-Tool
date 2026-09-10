# PMK Unlock Tool v4.0

Phone repair / flashing tool — Qualcomm EDL 9008 · MediaTek SP Flash · Samsung · Spreadtrum · Fastboot · ADB.
Windows 10/11 (64-bit) desktop app. **Offline tool** — flashing itself never needs internet; internet is only used
for the optional update check.

## 📥 Download (for shops / technicians)

Portable package (self-contained — no .NET runtime needed) is published on the **Releases** page:

**https://github.com/huaxinmobile-alt/PMK-Unlock-Tool/releases**

`PMK-Unlock-Tool-v4.0.0.zip` — extract to any writable folder (Desktop / `D:\` — **not** `C:\Program Files`, because
the built-in updater needs write access to its own folder) and run `WinFormsApp1.exe`.

> ☁️ **Loaders က zip ထဲ မပါတော့ပါ** (download သေးအောင် ~130MB) — tool က ဖုန်းတစ်လုံးလုပ်တဲ့အခါ လိုအပ်တဲ့
> loader ကို GitHub ကနေ အလိုအလျောက် download ပြီး သိမ်းတယ် (ပထမဆုံးအကြိမ်ပဲ internet လို — နောက်ပိုင်း offline)။
> ဒါမှမဟုတ် Browse နဲ့ programmer ကို ကိုယ်တိုင်ရွေးလည်း ရတယ်။

First-run requirements on each PC:

| Need | How |
|---|---|
| Python 3.8+ | python.org → installer → tick **Add python.exe to PATH** |
| Python modules | `pip install pyserial pyusb` |
| USB drivers | Qualcomm EDL 9008 (Zadig → WinUSB or QDLoader driver) · MediaTek VCOM · Samsung/ADB (Windows Update auto) |
| Windows SmartScreen | "More info → Run anyway" (unsigned app) |
| Run as Administrator | for driver / COM-port operations |

### 🔐 Login & Users

The tool is protected by a login gate — accounts live in a local **`users.dat`** (encrypted) next to the exe, no
internet needed.

- First run: the `admin` account exists with a temporary password (ask PMK) and **must be changed on first login**
- After login: **Settings → 👥 USERS & ACCESS** — add staff accounts, remove users, reset passwords, change your own
- **Settings → 🔒 Logout** returns to the login screen
- Shop-specific user lists are never shipped in the release zip, so self-updates keep your users intact
- Lost/corrupt `users.dat` (e.g. deleting the file) resets the list back to defaults — keep a copy if you customized it

### 🔑 Activation (per-PC license) — ဝယ်ယူသူ ဦးစွာ လုပ်ရမယ့်အရာ

Tool ပထမဆုံး ဖွင့်ရင် **activation screen** ပေါ်တယ် — ဒီ PC ရဲ့ **Installation ID** ပြပါတယ်။

- ဝယ်သူ: Installation ID ကို PMK ကို ပို့ → **license key** ပြန်ရ → tool ထဲ paste → Activate
- License က **ဒီ PC နဲ့ပဲ** အလုပ်လုပ်တယ် — ဖိုင် ကော်ပီကူးသွားရင် တစ်ခြား PC မှာ မရဘူး
- Activate ပြီးမှ login (admin/staff accounts) ဝင်လို့ရတယ်
- **PMK ဘက်က license ထုတ်ရန်**: `python tools/make_license.py <Installation-ID> "ဆိုင်နာမည်" [days]`
  - private key က `tools/private/activation_key.pem` — **backup သေချာယူပါ**၊ မပျောက်စေနဲ့ (ဒီဖိုင် ပျောက်ရင် အကုန်ပြန်ထုတ်ရတယ်)
  - `days` မထည့်ရင် သက်တမ်းမကုန် — ထည့်ရင် အဲဒီရက်ပြီး သက်တမ်းကုန်မယ် (ပြန် renew လို့ရ)
- Keypair ပြန်ထုတ်ချင်ရင် (အရေးပေါ်မှသာ): `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out tools/private/activation_key.pem` ပြီးရင် public key ကို `WinFormsApp1/License.cs` ထဲ update ပြီး rebuild

### 📧 Gmail register (ဆိုင်သစ် account ဖွင့်ခြင်း)

Login screen မှာ **"Gmail နဲ့ register"** button — ဝယ်ယူသူက သူ့ Gmail နဲ့ Google ဝင်ရုံပဲ
(password ကို tool က မမြင်ရဘူး — official Google OAuth, PKCE)။ သူ့ Gmail က ခွင့်ပြုစာရင်းထဲ
ပါမှ password ထည့်ပြီး account ဖွင့်ရတယ် — **တစ်ခါပဲ**၊ နောက်ပိုင်း login တွေက offline ပုံမှန်။

- **PMK ဘက်က**: `python tools/add_buyer.py buyer@gmail.com --git` → ဝယ်သူ register လုပ်လို့ရပြီ
  (စာရင်းက hash နဲ့ပဲ သိမ်း — email အရှင် public မဖြစ်ဘူး)
- **Google setup (တစ်ခါပဲ)**: console.cloud.google.com → project အသစ် → "APIs & Services" →
  OAuth consent screen (External, app name = PMK Unlock Tool, support email ထည့်) →
  Credentials → **Create OAuth client ID** → Application type = **Desktop app** →
  Client ID ကို ကော်ပီပြီး tool folder ထဲ `google_client.json` ဖန်တီး:
  ```json
  { "client_id": "xxxx.apps.googleusercontent.com" }
  ```
  (Client secret မလို — PKCE သုံးလို့)။ ဖိုင်ကို `Publish\win-x64\` ထဲ ထည့်ပြီးမှ
  `release.bat` run ရင် zip ထဲ ပါသွားမယ်။ Client ID မထည့်ရသေးရင် register button က
  အကြောင်းကြားစာပဲ ပြမယ်။
- မှတ်ချက်: consent screen က Testing mode ဆို user 100 ကန့်သတ်တယ် — အဆင်သင့်ဖြစ်ရင်
  console ထဲ "Publish app" လုပ်ပြီး Production ပြောင်းပါ (unverified warning ပေါ်နေဦးမှာ —
  openid/email scope မို့ ပုံမှန်အလုပ်ဖြစ်တယ်)

### 🔄 Built-in update

Once installed, the tool checks for new versions itself: **Settings → 🌐 ONLINE UPDATE → Check for Updates**
(startup auto-check, max once per day). New version = one click download + SHA-256 verification + self-restart —
no manual zip re-downloading on every PC.

## Folder map

| Path | Purpose |
|---|---|
| `WinFormsApp1/Form1.cs` | UI + handlers (code-built WinForms, no designer) |
| `WinFormsApp1/UpdateManager.cs` | Online update engine (manifest fetch / download / verify / self-update) |
|  `LoaderDB/Loaders/` | Qualcomm firehose loader DB by brand/model |
| `WinFormsApp1/edl/` | Python EDL client (bkerler-based) used for QC operations |
| `WinFormsApp1/mtkclient/` | MTK (MediaTek) client source |
| `update/latest.json` | Version manifest served to the in-app update checker |
| `release.bat` + `tools/make_release.py` | One-command release packager (zip → manifest → GitHub release) |
| `WinFormsApp1/bin/.../device_database/auto_loader_map.txt` | Learned HWID→loader map (runtime) |

## Build

```
dotnet build WinFormsApp1/WinFormsApp1.csproj -c Debug
```

Requires .NET 9 SDK (`net9.0-windows`) and Python on PATH at runtime.
Portable publish: `publish_portable.bat` (→ `Publish\win-x64`). Public release: `release.bat --release`.

## Features (v4.0)

- Qualcomm EDL: auto-detect on operation / Read GPT / full partition backup /
  Safe Format /
  Factory Reset / FRP reset / loader auto-detect (USB & serial transports)
- Qualcomm fast flash engine (QFil XML protocol, per-file progress, MB/s speed)
- Xiaomi helpers (shown when Brand = Xiaomi/Redmi/POCO): Mi Account bypass
  (modem CARDAPP→PMKDAPP patch, file or direct phone modes), partition Hex Edit,
  persist partition backup/restore
- MediaTek: DA flash flow, NV backup/restore/fix, KG unlock, demo removal,
  userlock/FRP reset, BL unlock/relock, Mi Account reset
- ADB / Fastboot / Samsung / Spreadtrum categories
- Themes (Ocean Dark / Nord / Dracula / Tokyo Night / Gruvbox / Daylight),
  session persistence via `theme.txt`

## Restore after PC loss

1. Clone this repo.
2. `dotnet build` (SDK 9 required) — or just run
   `WinFormsApp1/bin/Debug/net9.0-windows/WinFormsApp1.exe` directly.
3. Ensure Python (3.x) is installed and on PATH.
4. If loader auto-detect was previously learned, re-create
   `device_database/auto_loader_map.txt` entries or re-run Detect with manual loader.
