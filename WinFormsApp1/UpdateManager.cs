#nullable disable
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // GitHub Releases က public download လုပ်လို့ရအောင် ထည့်ထားတဲ့ manifest ဖိုင်
    public sealed class UpdateManifest
    {
        public string version { get; set; }
        public string notes { get; set; }
        public string url { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
    }

    // Online update engine — repo ရဲ့ update/latest.json ကို ဖတ်ပြီး
    // version သစ်ရှိရင် download → SHA-256 စစ် → extract → ကိုယ့်ကိုယ်ကို အစားထိုး (self-update) လုပ်ပေးတယ်။
    // UI (MessageBox/button) တွေက Form1 ဘက်မှာပဲ ရှိတယ်။
    public class UpdateManager
    {
        private static readonly Color SuccessColor = Color.FromArgb(129, 199, 132);
        private static readonly Color ErrorColor = Color.FromArgb(229, 115, 115);
        private static readonly Color WarningColor = Color.FromArgb(255, 213, 79);
        private static readonly Color InfoColor = Color.FromArgb(200, 200, 220);

        // Manifest အဓိကလိပ်စာ — env var (PMK_UPDATE_URL) နဲ့ test အတွက် ပြောင်းလို့ရတယ်
        public const string DefaultManifestUrl =
            "https://raw.githubusercontent.com/huaxinmobile-alt/PMK-Unlock-Tool/main/update/latest.json";

        private readonly LogHandler _log;
        private readonly Action<int, string> _updateProgress;
        private readonly Action<string> _setStatus;
        private readonly Func<string> _getCurrentVersion;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public UpdateManager(LogHandler log, Action<int, string> updateProgress, Action<string> setStatus, Func<string> getCurrentVersion)
        {
            _log = log;
            _updateProgress = updateProgress;
            _setStatus = setStatus;
            _getCurrentVersion = getCurrentVersion;
        }

        public string ManifestUrl => Environment.GetEnvironmentVariable("PMK_UPDATE_URL") ?? DefaultManifestUrl;

        private string UpdatesDir => Path.Combine(AppConfig.BaseDir, "updates");

        // InformationalVersion က "4.0.0+c353a9a…" (git hash ပါ) လာတတ်လို့ version ပိုင်းကိုပဲ ယူတယ်
        public string CurrentVersionText
        {
            get
            {
                try
                {
                    string v = _getCurrentVersion() ?? "0.0.0";
                    int cut = v.IndexOfAny(new[] { '+', '-' });
                    if (cut > 0) v = v.Substring(0, cut);
                    return string.IsNullOrWhiteSpace(v) ? "0.0.0" : v.Trim();
                }
                catch { return "0.0.0"; }
            }
        }

        // Manifest ရဲ့ version က လက်ရှိ version ထက် ကြီးလား
        public bool IsNewer(UpdateManifest m)
        {
            try
            {
                if (m == null || string.IsNullOrWhiteSpace(m.version)) return false;
                if (!Version.TryParse(CurrentVersionText.Trim(), out var cur)) return false;
                return Version.TryParse(m.version.Trim(), out var mv) && mv > cur;
            }
            catch { return false; }
        }

        // Auto-check ကို တစ်နေ့တစ်ခါပဲ လုပ်ဖို့ throttle — check ပြီးတိုင်း ဒီနေ့ရက်ကို မှတ်တယ်
        public bool AutoCheckDue()
        {
            try
            {
                string f = Path.Combine(UpdatesDir, "last_check.txt");
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (IOFile.Exists(f) && IOFile.ReadAllText(f).Trim() == today) return false;
                Directory.CreateDirectory(UpdatesDir);
                IOFile.WriteAllText(f, today);
                return true;
            }
            catch { return true; }
        }

        // update/latest.json ကို ဖတ်တယ် — မရရင် null (offline / server မရောက်) — error log က caller ဖက်မှာ
        public async Task<UpdateManifest> FetchManifestAsync()
        {
            try
            {
                using var resp = await _http.GetAsync(ManifestUrl).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _log($"⚠️ Update server response: {(int)resp.StatusCode}", WarningColor);
                    return null;
                }
                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var m = JsonSerializer.Deserialize<UpdateManifest>(json, opt);
                if (m == null || string.IsNullOrWhiteSpace(m.url)) return null;
                return m;
            }
            catch (Exception ex)
            {
                _log($"⚠️ Update check: {ex.Message}", WarningColor);
                return null;
            }
        }

        // Zip ကို download → hash စစ် → updates\stage ထဲ extract → pending marker ရေး။
        // အောင်ရင် true — error ဆို false (log + status မှာ ပြထားပြီးသား)
        public async Task<bool> DownloadAndStageAsync(UpdateManifest m)
        {
            string updDir = UpdatesDir;
            string zipPath = Path.Combine(updDir, $"pmk_update_{m.version}.zip");
            string stageDir = Path.Combine(updDir, "stage");
            try
            {
                Directory.CreateDirectory(updDir);
                _setStatus?.Invoke($"Downloading update v{m.version}…");

                // Stage ဟောင်း ကျန်နေရင် အရင်ရှင်း (ပြီးခဲ့တဲ့ update မအောင်ခဲ့တာမျိုး)
                if (Directory.Exists(stageDir))
                {
                    try { Directory.Delete(stageDir, recursive: true); }
                    catch { _log("⚠️ Old update stage ကို မဖျက်နိုင်ဘူး — ခဏနေပြီး ပြန်စမ်းပါ", WarningColor); return false; }
                }
                if (IOFile.Exists(zipPath)) { try { IOFile.Delete(zipPath); } catch { } }

                _log($"⬇️ Downloading v{m.version}…", InfoColor);
                // Stream/handle တွေကို ဒီ scope ထဲမှာပဲ ပိတ်တယ် — zip ကို ပြန်ဖတ်ဖို့ handle ပိတ်ပြီးသား ဖြစ်ရမယ်
                long total;
                using (var resp = await _http.GetAsync(m.url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) { _log($"❌ Download failed ({(int)resp.StatusCode})", ErrorColor); return false; }
                    total = resp.Content.Headers.ContentLength ?? 0;
                    await using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var dst = IOFile.Create(zipPath);
                    byte[] buf = new byte[256 * 1024];
                    long done = 0;
                    int lastPct = -1;
                    while (true)
                    {
                        int n = await src.ReadAsync(buf).ConfigureAwait(false);
                        if (n == 0) break;
                        await dst.WriteAsync(buf.AsMemory(0, n)).ConfigureAwait(false);
                        done += n;
                        if (total > 0)
                        {
                            int pct = (int)(done * 100 / total);
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                _updateProgress?.Invoke(pct, $"Downloading update… {done / 1048576.0:0} MB / {total / 1048576.0:0} MB");
                            }
                        }
                    }
                }

                // SHA-256 စစ် — download မှားနေရင် install မလုပ်ဘဲ ရပ်တယ်
                if (!string.IsNullOrWhiteSpace(m.sha256))
                {
                    _log("🔐 Verifying download (SHA-256)…", InfoColor);
                    string actual;
                    await using (var fs = IOFile.OpenRead(zipPath))
                    using (var sha = SHA256.Create())
                    {
                        byte[] h = await sha.ComputeHashAsync(fs).ConfigureAwait(false);
                        actual = Convert.ToHexString(h).ToLowerInvariant();
                    }
                    if (!actual.Equals(m.sha256.Trim().ToLowerInvariant()))
                    {
                        _log("❌ SHA-256 မကိုက်ဘူး — download မှားနေတယ်၊ ပြန်စမ်းပါ", ErrorColor);
                        try { IOFile.Delete(zipPath); } catch { }
                        return false;
                    }
                    _log("✅ SHA-256 ကိုက်ပါတယ်", SuccessColor);
                }

                _updateProgress?.Invoke(95, "Extracting…");
                _log("📦 Extracting update files…", InfoColor);
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stageDir)).ConfigureAwait(false);

                if (!IOFile.Exists(Path.Combine(stageDir, "WinFormsApp1.exe")))
                {
                    _log("❌ Update package ထဲမှာ WinFormsApp1.exe မပါဘူး — package မှားနေတယ်", ErrorColor);
                    try { Directory.Delete(stageDir, recursive: true); } catch { }
                    return false;
                }

                // Zip နေရာ ရှင်းပြီး marker ရေး — restart ချိန်မှာ ဒီ marker ကို ကြည့်ပြီး apply လုပ်မယ်
                try { IOFile.Delete(zipPath); } catch { }
                IOFile.WriteAllText(Path.Combine(updDir, "pending.txt"), m.version);
                _updateProgress?.Invoke(100, "Update ready");
                _log($"✅ v{m.version} update အဆင်သင့်ပါပြီ — Tool က restart လုပ်တာနဲ့ အစားထိုးပါမယ်", SuccessColor);
                return true;
            }
            catch (Exception ex)
            {
                _log($"❌ Update download/extract error: {ex.Message}", ErrorColor);
                return false;
            }
        }

        // App ပိတ်ပြီးနောက် stage ကို install folder ပေါ် robocopy /MOVE နဲ့ လွှဲပြီး
        // app ပြန်ဖွင့်ပေးမယ့် apply_update.bat ကို ရေးပြီး လွှတ်တယ်။
        public bool LaunchUpdater()
        {
            try
            {
                string updDir = UpdatesDir;
                Directory.CreateDirectory(updDir);
                string bat = Path.Combine(updDir, "apply_update.bat");
                string content =
                    "@echo off\r\n" +
                    "set \"SRC=%~dp0stage\"\r\n" +
                    // Install folder = bat ရဲ့ မိဘ folder (updates\..) — argument မလိုတော့လို့ quoting ပြဿနာ လုံးဝမရှိ
                    "for %%D in (\"%~dp0..\") do set \"DST=%%~fD\"\r\n" +
                    "echo RUN SRC=%SRC% DST=%DST% > \"%~dp0apply_debug.txt\"\r\n" +
                    "timeout /t 5 /nobreak >nul 2>&1\r\n" +
                    "if not exist \"%SRC%\\WinFormsApp1.exe\" ( echo NO-STAGE-EXE >> \"%~dp0apply_debug.txt\" & goto fail )\r\n" +
                    "pushd \"%SRC%\" >nul 2>&1\r\n" +
                    "echo PUSHD=%errorlevel% >> \"%~dp0apply_debug.txt\"\r\n" +
                    "robocopy \".\" \"%DST%\" /E /MOVE /IS /IT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP >> \"%~dp0apply_debug.txt\" 2>&1\r\n" +
                    "set \"RC=%errorlevel%\"\r\n" +
                    "popd\r\n" +
                    "echo ROBO_RC=%RC% >> \"%~dp0apply_debug.txt\"\r\n" +
                    "if %RC% GEQ 8 goto fail\r\n" +
                    "rmdir /s /q \"%SRC%\" 2>nul\r\n" +
                    "del /q \"%~dp0pending.txt\" 2>nul\r\n" +
                    "del /q \"%~dp0apply_failed.txt\" 2>nul\r\n" +
                    "del /q \"%~dp0apply_debug.txt\" 2>nul\r\n" +
                    "start \"\" \"%DST%\\WinFormsApp1.exe\"\r\n" +
                    "del /q \"%~f0\" 2>nul\r\n" +
                    "exit /b 0\r\n" +
                    ":fail\r\n" +
                    "echo PMK-APPLY-FAILED rc=%RC% >> \"%~dp0apply_debug.txt\"\r\n" +
                    "echo PMK-APPLY-FAILED > \"%~dp0apply_failed.txt\"\r\n" +
                    "del /q \"%~f0\" 2>nul\r\n" +
                    "exit /b 1\r\n";
                IOFile.WriteAllText(bat, content);

                // ShellExecute နဲ့ .bat ကို လွှတ်တယ် — window ကို ဝှက်ထားတယ်။
                // Install folder ကို argument အဖြစ် မပို့တော့ဘူး (%~dp0.. ကနေ ကိုယ်တိုင် တွက်တယ်)
                var psi = new ProcessStartInfo
                {
                    FileName = bat,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = updDir
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                _log($"❌ Updater launch error: {ex.Message}", ErrorColor);
                return false;
            }
        }
    }
}
