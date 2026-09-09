# PMK Unlock Tool v4.0

Phone repair / flashing tool (Qualcomm EDL 9008 · MediaTek SP Flash · Samsung · Spreadtrum · Fastboot · ADB).

> **Private full backup repository** — contains source code AND runtime assets
> (Loader DB, edl client, mtkclient, build output under `WinFormsApp1/bin`).
> The runtime copy under `bin/Debug/net9.0-windows` is the working folder the
> app actually uses; the project-level folders (`Loaders/`, `edl/`, `mtkclient/`)
> are the maintained sources that get copied in.

## Folder map

| Path | Purpose |
|---|---|
| `WinFormsApp1/Form1.cs` | Entire UI + logic (code-built WinForms, no designer) |
| `WinFormsApp1/Loaders/` | Qualcomm firehose loader DB by brand/model |
| `WinFormsApp1/edl/` | Python EDL client (bkerler-based) used for QC operations |
| `WinFormsApp1/mtkclient/` | MTK (MediaTek) client source |
| `WinFormsApp1/bin/Debug/net9.0-windows/` | Working runtime (edl + Loaders copy + QFL/MiFlash tools) |
| `WinFormsApp1/bin/.../device_database/auto_loader_map.txt` | Learned HWID→loader map |

## Build

```
dotnet build WinFormsApp1/WinFormsApp1.csproj -c Debug
```

Requires .NET 9 SDK (`net9.0-windows`) and Python on PATH at runtime.

## Features (v4.0)

- Qualcomm EDL: Detect 9008 / Read GPT / full partition backup / Safe Format /
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
