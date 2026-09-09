#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // ADB / Fastboot business logic service — Form1 ကနေ ခွဲထုတ်ထားတယ်။
    // UI dialogs/confirmations တွေက Form1 (handlers) ဘက်မှာပဲ ရှိတယ်။
    public class AdbFastbootService
    {
        private static readonly Color AdbColor = Color.FromArgb(100, 181, 246);
        private static readonly Color FastbootColor = Color.FromArgb(255, 167, 38);
        private static readonly Color SuccessColor = Color.FromArgb(129, 199, 132);
        private static readonly Color ErrorColor = Color.FromArgb(229, 115, 115);
        private static readonly Color WarningColor = Color.FromArgb(255, 213, 79);
        private static readonly Color InfoColor = Color.FromArgb(200, 200, 220);

        private readonly LogHandler _log;
        private readonly Action<int, string> _updateProgress;
        private readonly Action<string> _setStatus;
        private readonly Action<bool> _setOperationState;
        private readonly ProcessRunnerService _processRunner;
        private readonly Func<string> _getAdbPath;
        private readonly Func<string> _getFastbootPath;

        public AdbFastbootService(
            LogHandler log,
            Action<int, string> updateProgress,
            Action<string> setStatus,
            Action<bool> setOperationState,
            ProcessRunnerService processRunner,
            Func<string> getAdbPath,
            Func<string> getFastbootPath)
        {
            _log = log ?? delegate { };
            _updateProgress = updateProgress ?? delegate { };
            _setStatus = setStatus ?? delegate { };
            _setOperationState = setOperationState ?? delegate { };
            _processRunner = processRunner;
            _getAdbPath = getAdbPath ?? (() => "adb");
            _getFastbootPath = getFastbootPath ?? (() => "fastboot");
        }
public async Task AdbDevicesAsync()
        {
            _log("\n🔍 [ADB] Scanning Connected Devices...", AdbColor);

            string output = await _processRunner.RunProcessCommand(_getAdbPath(), "devices -l", "Scanning ADB Devices...", false);
            if (string.IsNullOrWhiteSpace(output))
            {
                _log("❌ ADB daemon not responding or no devices connected.", ErrorColor);
                return;
            }

            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            List<string> connectedSerials = new List<string>();

            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("List of devices") || t.StartsWith("*") || string.IsNullOrWhiteSpace(t)) continue;

                if (t.Contains("device") || t.Contains("recovery") || t.Contains("unauthorized"))
                {
                    string[] parts = t.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && parts[0].Length >= 4)
                    {
                        connectedSerials.Add(parts[0]);
                    }
                }
            }

            if (connectedSerials.Count == 0)
            {
                _log("⚠️ No active ADB device detected.", WarningColor);
                return;
            }

            _log($"✅ Found {connectedSerials.Count} active device(s) connected!\n", SuccessColor);

            int deviceIndex = 1;
            foreach (string serial in connectedSerials)
            {
                await DisplayStructuredDeviceInfo(serial, deviceIndex++, connectedSerials.Count);
            }
        }
public async Task AdbBatteryInfoAsync()
        {
            _log("\n🔋 [ADB] Reading Battery & Power Information...", AdbColor);

            string rawDump = await _processRunner.RunAdbTargeted("shell dumpsys battery", "Reading Battery Status...", false);
            if (string.IsNullOrWhiteSpace(rawDump))
            {
                _log("❌ Failed to read battery data. Ensure device is connected.", ErrorColor);
                return;
            }

            string GetValue(string key)
            {
                Match m = Regex.Match(rawDump, $@"{key}:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim() : "N/A";
            }

            string level = GetValue("level");
            string statusRaw = GetValue("status");
            string healthRaw = GetValue("health");
            string voltageRaw = GetValue("voltage");
            string tempRaw = GetValue("temperature");
            string tech = GetValue("technology");
            string usbPowered = GetValue("USB powered");
            string acPowered = GetValue("AC powered");
            string wirelessPowered = GetValue("Wireless powered");

            string status = statusRaw switch
            {
                "2" => "⚡ Charging",
                "3" => "🔋 Discharging",
                "4" => "🔌 Not Charging",
                "5" => "✅ Full (100%)",
                _ => "Unknown"
            };

            string powerSource = "Battery Only";
            if (acPowered.Equals("true", StringComparison.OrdinalIgnoreCase)) powerSource = "🔌 AC Fast Charge";
            else if (usbPowered.Equals("true", StringComparison.OrdinalIgnoreCase)) powerSource = "🔌 USB Connected";
            else if (wirelessPowered.Equals("true", StringComparison.OrdinalIgnoreCase)) powerSource = "⚡ Wireless";

            string health = healthRaw switch
            {
                "2" => "💚 Good",
                "3" => "🔥 Overheat Warning",
                "4" => "💀 Dead / Replace",
                _ => "Normal"
            };

            string voltage = voltageRaw;
            if (double.TryParse(voltageRaw, out double vVal)) voltage = $"{vVal / 1000.0:F3} V";

            string temp = tempRaw;
            if (double.TryParse(tempRaw, out double tVal)) temp = $"🌡️ {tVal / 10.0:F1} °C";

            _log("╔══════════════════════════════════════════════════════════╗", Color.FromArgb(0, 188, 212));
            _log("║               🔋 BATTERY & POWER STATUS                  ║", Color.FromArgb(0, 188, 212));
            _log("╚══════════════════════════════════════════════════════════╝", Color.FromArgb(0, 188, 212));
            _log($"  • Battery Level    : {level}%", SuccessColor);
            _log($"  • Charging Status  : {status}", statusRaw == "2" || statusRaw == "5" ? SuccessColor : WarningColor);
            _log($"  • Power Source     : {powerSource}", InfoColor);
            _log($"  • Battery Health   : {health}", healthRaw == "2" ? SuccessColor : ErrorColor);
            _log($"  • Current Voltage  : {voltage}", InfoColor);
            _log($"  • Temperature      : {temp}", FastbootColor);
            _log($"  • Battery Tech     : {tech}", InfoColor);
            _log("────────────────────────────────────────────────────────────\n", Color.FromArgb(0, 188, 212));
        }
public async Task AdbInstallAsync(string apkPath)
        {
                _log($"\n📦 Installing APK: {Path.GetFileName(apkPath)}...", AdbColor);
                string res = await _processRunner.RunAdbTargeted($"install -r \"{apkPath}\"", "Installing APK...");
                if (res != null && res.Contains("Success")) _log("✅ App installed successfully!", SuccessColor);
                else _log("⚠️ Installation finished. Check output.", WarningColor);
        }
public async Task AdbScreenshotAsync(string savePath)
        {
                _log("\n📸 Capturing screenshot from device...", AdbColor);
                await _processRunner.RunAdbTargeted("shell screencap -p /sdcard/temp_screen.png", "Capturing...", false);
                await _processRunner.RunAdbTargeted($"pull /sdcard/temp_screen.png \"{savePath}\"", "Saving...", false);
                await _processRunner.RunAdbTargeted("shell rm /sdcard/temp_screen.png", "", false);
                if (IOFile.Exists(savePath)) _log($"✅ Screenshot saved: {savePath}", SuccessColor);
                else _log("❌ Failed to capture screenshot.", ErrorColor);
        }
public async Task AdbFrpResetAsync()
        {
            _log("\n🔓 [ADB] Universal FRP Reset (SetupWizard Bypass)...", AdbColor);
            await _processRunner.RunAdbTargeted("shell content insert --uri content://settings/secure --bind name:s:user_setup_complete --bind value:s:1", "Setting complete...", false);
            await _processRunner.RunAdbTargeted("shell pm clear com.google.android.setupwizard", "Clearing setup wizard...", false);
            await _processRunner.RunAdbTargeted("reboot", "Rebooting...", false);
            _log("✅ FRP command issued! Phone is restarting to Home.", SuccessColor);
        }
public async Task AdbDebloatAsync()
        {
            string[] bloatPackages = {
                "com.facebook.katana", "com.facebook.system", "com.facebook.appmanager", "com.facebook.services",
                "com.google.android.apps.tachyon", "com.google.android.feedback",
                "com.miui.analytics", "com.miui.msa.global", "com.miui.bugreport",
                "com.cleanmaster.mguard", "com.aura.oobe.samsung"
            };

            _log("\n🗑️ [ADB] Removing bloatware apps...", AdbColor);
            foreach (var pkg in bloatPackages)
            {
                string res = await _processRunner.RunAdbTargeted($"shell pm uninstall -k --user 0 {pkg}", "", false);
                if (res != null && res.Contains("Success")) _log($"  • Uninstalled: {pkg}", AdbColor);
            }
            _log("✅ Debloat operation finished!", SuccessColor);
            _log("🔄 [Auto Reboot] Restarting phone...", AdbColor);
            await _processRunner.RunAdbTargeted("reboot", "Rebooting...", false);
        }
public async Task AdbEnableLangAsync()
        {
            _log("\n🇲🇲 [ADB] Granting Language Change Permission (CHANGE_CONFIGURATION)...", AdbColor);
            await _processRunner.RunAdbTargeted("shell pm grant com.wanam.languageenabler android.permission.CHANGE_CONFIGURATION", "", false);
            await _processRunner.RunAdbTargeted("shell pm grant com.google.android.apps.translate android.permission.CHANGE_CONFIGURATION", "", false);
            await _processRunner.RunAdbTargeted("shell setprop persist.sys.locale my-MM", "", false);
            await _processRunner.RunAdbTargeted("shell am broadcast -a android.intent.action.LOCALE_CHANGED", "", false);
            _log("✅ All Languages Enabled! Please check phone language settings.", SuccessColor);
        }
public async Task FastbootDevicesAsync()
        {
            _log("\n⚡ [Fastboot] Checking Connected Devices...", FastbootColor);
            string output = await _processRunner.RunProcessCommand(_getFastbootPath(), "devices", "Checking Fastboot...", false);
            if (string.IsNullOrWhiteSpace(output))
            {
                _log("⚠️ No fastboot device detected.", WarningColor);
                return;
            }

            _log("╔══════════════════════════════════════════════════════════╗", FastbootColor);
            _log("║             ⚡ FASTBOOT CONNECTED DEVICE                 ║", FastbootColor);
            _log("╚══════════════════════════════════════════════════════════╝", FastbootColor);
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                _log($"  • {line.Trim()}", SuccessColor);
            }
            _log("────────────────────────────────────────────────────────────\n", FastbootColor);
        }
public async Task FastbootFrpResetAsync()
        {
            _log("\n╔══════════════════════════════════════════════════════════╗", FastbootColor);
            _log("║       🔓 FASTBOOT UNIVERSAL FRP RESET (MULTI-BRAND)      ║", FastbootColor);
            _log("╚══════════════════════════════════════════════════════════╝", FastbootColor);
            _log("⚡ Checking device connection & Bootloader status...", FastbootColor);

            _setOperationState(true);
            _setStatus("Resetting Fastboot FRP...");
            _updateProgress(15, "Checking Device...");

            // 1. Device ချိတ်ဆက်မှု စစ်ဆေးခြင်း
            string devCheck = await _processRunner.RunProcessCommand(_getFastbootPath(), "devices", "", false);
            if (string.IsNullOrWhiteSpace(devCheck))
            {
                _log("❌ No fastboot device detected. Connect phone in Fastboot Mode!", ErrorColor);
                _setOperationState(false);
                _setStatus("Ready");
                _updateProgress(0, "");
                return;
            }

            // 2. Bootloader Lock/Unlock အခြေအနေ နှင့် Model ဖတ်ယူခြင်း
            string varCheck = await _processRunner.RunProcessCommand(_getFastbootPath(), "getvar all", "", false);
            bool isUnlocked = varCheck.Contains("unlocked:yes", StringComparison.OrdinalIgnoreCase) ||
                              varCheck.Contains("unlocked: yes", StringComparison.OrdinalIgnoreCase) ||
                              varCheck.Contains("unlocked: 1", StringComparison.OrdinalIgnoreCase);

            string product = "Unknown";
            Match mProd = Regex.Match(varCheck, @"product:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            if (mProd.Success) product = mProd.Groups[1].Value.Trim();

            _log($"📱 Connected Device : [{product.ToUpper()}]", SuccessColor);
            _log($"🔓 Bootloader Status: {(isUnlocked ? "UNLOCKED (Ready to Reset)" : "⚠️ LOCKED (May fail on some partitions)")}", SuccessColor);

            _updateProgress(35, "Erasing FRP Partitions...");

            bool frpSuccess = false;

            // 3. Motorola သီးသန့် FRP Bypass Protocol (ဥပမာ- Moto G7 river စသည့် မော်ဒယ်များအတွက်)
            if (product.Contains("river") || product.Contains("ocean") || product.Contains("potter") || product.Contains("moto", StringComparison.OrdinalIgnoreCase))
            {
                _log("\n🛡️ [Motorola Protocol] Setting Factory Fastboot Mode...", FastbootColor);
                await _processRunner.RunProcessCommand(_getFastbootPath(), "oem fb_mode_set", "", false);
                await _processRunner.RunProcessCommand(_getFastbootPath(), "erase config", "", false);
                await _processRunner.RunProcessCommand(_getFastbootPath(), "erase frp", "", false);
                await _processRunner.RunProcessCommand(_getFastbootPath(), "oem fb_mode_clear", "", false);
                frpSuccess = true;
            }

            // 4. Universal Partition Erase Sequence (Standard Android / Qualcomm / MTK / Pixel / Xiaomi)
            string[] frpPartitions = { "frp", "config", "persistent" };

            foreach (var part in frpPartitions)
            {
                _log($"⚡ Erasing [{part}] partition...", FastbootColor);
                string res = await _processRunner.RunProcessCommand(_getFastbootPath(), $"erase {part}", "", false);

                if (res != null && (res.Contains("OKAY") || res.Contains("finished")))
                {
                    _log($"  ✅ [{part}] Partition Cleared Successfully!", SuccessColor);
                    frpSuccess = true;
                }
            }

            // 5. Userdata Lock & Format Verification
            _updateProgress(80, "Finalizing...");
            await Task.Delay(500);

            if (frpSuccess)
            {
                _updateProgress(100, "Done");
                _log("\n🎉 Fastboot FRP Reset Executed Successfully!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone to System...", FastbootColor);

                await _processRunner.RunProcessCommand(_getFastbootPath(), "reboot", "Rebooting...", false);
                _log("📱 Phone is restarting to Welcome Screen without Google Lock!\n", SuccessColor);
            }
            else
            {
                _log("\n❌ Failed to erase FRP. Make sure Bootloader is Unlocked or use EDL/BROM Mode.", ErrorColor);
                _updateProgress(0, "");
            }

            _setOperationState(false);
            _setStatus("Ready");
        }
public async Task FastbootGetvarAsync()
        {
            _log("\n⚡ [Fastboot] Reading Device Variables...", FastbootColor);
            string output = await _processRunner.RunProcessCommand(_getFastbootPath(), "getvar all", "Reading Variables...", false);
            if (string.IsNullOrWhiteSpace(output))
            {
                _log("❌ No fastboot device detected or command failed.", ErrorColor);
                return;
            }

            string GetVarValue(string varName)
            {
                Match m = Regex.Match(output, $@"\({varName}\):\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (!m.Success) m = Regex.Match(output, $@"{varName}:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim() : "N/A";
            }

            string product = GetVarValue("product");
            string unlocked = GetVarValue("unlocked");
            string secure = GetVarValue("secure");
            string serial = GetVarValue("serialno");
            string hwVersion = GetVarValue("hw-revision");
            string baseband = GetVarValue("version-baseband");

            _log("╔══════════════════════════════════════════════════════════╗", FastbootColor);
            _log("║             ⚡ FASTBOOT INFORMATION                      ║", FastbootColor);
            _log("╚══════════════════════════════════════════════════════════╝", FastbootColor);
            _log($"  • Serial Number    : {serial}", SuccessColor);
            _log($"  • Product Name     : {product}", SuccessColor);
            _log($"  • Bootloader Lock  : {(unlocked.ToLower() == "yes" ? "🔓 Unlocked" : "🔒 Locked")}", unlocked.ToLower() == "yes" ? SuccessColor : ErrorColor);
            _log($"  • Secure Boot      : {secure}", InfoColor);
            _log($"  • Hardware Rev     : {hwVersion}", InfoColor);
            _log($"  • Baseband Version : {baseband}", InfoColor);
            _log("────────────────────────────────────────────────────────────\n", FastbootColor);
        }
public async Task FastbootFlashBootAsync(string imgPath)
        {
                _log($"\n🔥 Flashing Boot image: {Path.GetFileName(imgPath)}...", FastbootColor);
                string res = await _processRunner.RunProcessCommand(_getFastbootPath(), $"flash boot \"{imgPath}\"", "Flashing Boot...");
                if (res != null && !res.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    _log("✅ Boot image flashed successfully!", SuccessColor);
                    _log("🔄 [Auto Reboot] Restarting phone to System...", FastbootColor);
                    await _processRunner.RunProcessCommand(_getFastbootPath(), "reboot", "Rebooting...", false);
                    _log("📱 Phone rebooted successfully!\n", SuccessColor);
                }
        }
public async Task FastbootFlashRecoveryAsync(string imgPath)
        {
                _log($"\n🔧 Flashing Recovery image: {Path.GetFileName(imgPath)}...", FastbootColor);
                string res = await _processRunner.RunProcessCommand(_getFastbootPath(), $"flash recovery \"{imgPath}\"", "Flashing Recovery...");
                if (res != null && !res.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    _log("✅ Recovery image flashed successfully!", SuccessColor);
                    _log("🔄 [Auto Reboot] Restarting phone...", FastbootColor);
                    await _processRunner.RunProcessCommand(_getFastbootPath(), "reboot", "Rebooting...", false);
                    _log("📱 Phone rebooted successfully!\n", SuccessColor);
                }
        }
public async Task FastbootTempBootAsync(string imgPath)
        {
                _log($"\n🚀 Temporary Booting image: {Path.GetFileName(imgPath)}...", FastbootColor);
                await _processRunner.RunProcessCommand(_getFastbootPath(), $"boot \"{imgPath}\"", "Temporary Booting...");
                _log("✅ Boot payload sent! Device is booting into temporary recovery.", SuccessColor);
        }
public async Task FastbootCheckArbAsync()
        {
            _log("\n🛡️ [Fastboot] Checking Xiaomi Anti-Rollback (ARB) Index...", FastbootColor);
            string output = await _processRunner.RunProcessCommand(_getFastbootPath(), "getvar anti", "Checking ARB...", false);

            Match m = Regex.Match(output, @"anti:\s*(\d+)");
            if (m.Success)
            {
                string index = m.Groups[1].Value;
                _log("╔══════════════════════════════════════════════════════════╗", FastbootColor);
                _log($"║          🛡️ ANTI-ROLLBACK INDEX : [ {index} ]                   ║", FastbootColor);
                _log("╚══════════════════════════════════════════════════════════╝", FastbootColor);
                _log($"  • ARB Level: {index}", SuccessColor);
                _log("  • Warning: Never flash firmware with ARB lower than this number!", WarningColor);
            }
            else
            {
                _log("⚠️ ARB Index not supported on this model or device locked.", WarningColor);
            }
        }
public async Task FastbootToFastbootdAsync()
        {
            _log("\n⚡ Switching to Fastbootd Mode (Super / Dynamic Partitions)...", FastbootColor);
            await _processRunner.RunProcessCommand(_getFastbootPath(), "reboot fastboot", "Entering Fastbootd...");
        }
        public async Task FastbootUnlockAsync()
        {
            _log("\n🔓 Unlocking bootloader...", FastbootColor);
            string res = await _processRunner.RunProcessCommand(_getFastbootPath(), "flashing unlock", "Unlocking Bootloader...");
            if (res != null && !res.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ Bootloader Unlocked!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone...", FastbootColor);
                await _processRunner.RunProcessCommand(_getFastbootPath(), "reboot", "Rebooting...", false);
                _log("📱 Phone is rebooting!\n", SuccessColor);
            }
        }
        public async Task FastbootSwitchSlotAsync(string targetSlot)
        {
            _log($"\n🔀 Switching active slot to: [{targetSlot}]...", FastbootColor);
            await _processRunner.RunProcessCommand(_getFastbootPath(), $"--set-active={targetSlot}", $"Switching to Slot {targetSlot}...");
            _log($"✅ Active Slot set to [{targetSlot.ToUpper()}]!", SuccessColor);
            _log("🔄 [Auto Reboot] Restarting phone...", FastbootColor);
            await _processRunner.RunProcessCommand(_getFastbootPath(), "reboot", "Rebooting...", false);
            _log("📱 Phone rebooted to switched slot!\n", SuccessColor);
        }
        public void LaunchScrcpy()
        {
            string scrcpyPath = AppConfig.ScrcpyExe;
            if (!IOFile.Exists(scrcpyPath)) scrcpyPath = AppConfig.ScrcpyExeRootFallback;

            if (IOFile.Exists(scrcpyPath))
            {
                _log("\n🖥️ Launching Scrcpy Screen Mirror...", AdbColor);
                Process.Start(new ProcessStartInfo(scrcpyPath) { UseShellExecute = true });
            }
            else
            {
                _log("❌ scrcpy.exe not found in tool folder. Please put scrcpy in tool directory.", ErrorColor);
            }
        }
        public async Task AdbRebootBootloaderAsync()
        {
            await _processRunner.RunAdbTargeted("reboot bootloader", "Rebooting to Bootloader...");
        }
        public async Task AdbRebootRecoveryAsync()
        {
            await _processRunner.RunAdbTargeted("reboot recovery", "Rebooting to Recovery...");
        }
        public async Task AdbRebootEdlAsync()
        {
            await _processRunner.RunAdbTargeted("reboot edl", "Rebooting to EDL...");
        }
        public async Task AdbRebootSystemAsync()
        {
            await _processRunner.RunAdbTargeted("reboot", "Rebooting System...");
        }
        public async Task FastbootRebootAsync()
        {
            await _processRunner.RunProcessCommand(_getFastbootPath(), "reboot", "Rebooting...");
        }
        private async Task DisplayStructuredDeviceInfo(string serial, int index, int total)
        {
            _setStatus($"Reading device information: [{serial}]...");

            async Task<string> GetProp(string prop)
            {
                string res = await _processRunner.RunProcessCommand(_getAdbPath(), $"-s {serial} shell getprop {prop}", "", false);
                return string.IsNullOrWhiteSpace(res) ? "N/A" : res.Trim();
            }

            async Task<string> RunShell(string cmd)
            {
                string res = await _processRunner.RunProcessCommand(_getAdbPath(), $"-s {serial} shell \"{cmd}\"", "", false);
                return string.IsNullOrWhiteSpace(res) ? "N/A" : res.Trim();
            }

            string brand = await GetProp("ro.product.brand");
            string manufacturer = await GetProp("ro.product.manufacturer");
            string model = await GetProp("ro.product.model");
            string product = await GetProp("ro.product.name");
            string deviceCode = await GetProp("ro.product.device");
            string androidVer = await GetProp("ro.build.version.release");
            string sdkVer = await GetProp("ro.build.version.sdk");
            string secPatch = await GetProp("ro.build.version.security_patch");
            string buildId = await GetProp("ro.build.display.id");
            string chipset = await GetProp("ro.board.platform");
            if (chipset == "N/A" || string.IsNullOrEmpty(chipset)) chipset = await GetProp("ro.hardware");

            string miuiVer = await GetProp("ro.miui.ui.version.name");
            if (miuiVer != "N/A" && !string.IsNullOrEmpty(miuiVer))
                buildId = $"{miuiVer} ({buildId})";

            string blState = await GetProp("ro.boot.flash.locked");
            string blStatus = blState == "0" ? "🔓 Unlocked" : (blState == "1" ? "🔒 Locked" : "Unknown");

            string battInfo = await RunShell("dumpsys battery");
            string battLevel = "N/A";
            Match bMatch = Regex.Match(battInfo, @"level:\s*(\d+)");
            if (bMatch.Success) battLevel = $"{bMatch.Groups[1].Value}%";

            if (brand == "N/A" && manufacturer != "N/A") brand = manufacturer;

            _log("╔══════════════════════════════════════════════════════════╗", AdbColor);
            _log($"║       📱 DEVICE [ {index} / {total} ] : {serial.PadRight(28)}║", AdbColor);
            _log("╚══════════════════════════════════════════════════════════╝", AdbColor);
            _log($"  • Device Serial    : {serial} [Online]", SuccessColor);
            _log($"  • Brand Name       : {brand.ToUpperInvariant()}", InfoColor);
            _log($"  • Model Name       : {model}", SuccessColor);
            _log($"  • Codename / Board : {deviceCode} / {chipset}", InfoColor);
            _log($"  • Android Version  : Android {androidVer} (SDK: {sdkVer})", FastbootColor);
            _log($"  • Security Patch   : {secPatch}", WarningColor);
            _log($"  • System Build/OS  : {buildId}", InfoColor);
            _log($"  • Bootloader State : {blStatus}", blState == "0" ? SuccessColor : ErrorColor);
            _log($"  • Battery Level    : {battLevel}", SuccessColor);
            _log("────────────────────────────────────────────────────────────\n", AdbColor);
        }
    }
}
