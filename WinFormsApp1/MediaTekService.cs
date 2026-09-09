#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // MediaTek (MTK BROM) business logic service — Form1 ကနေ ခွဲထုတ်ထားတယ်။
    // UI confirm/dialog တွေက Form1 (handler) ဘက်မှာပဲ ရှိတယ်။
    public class MediaTekService
    {
        private static readonly Color MtkColor = Color.FromArgb(102, 187, 106);
        private static readonly Color SuccessColor = Color.FromArgb(129, 199, 132);
        private static readonly Color ErrorColor = Color.FromArgb(229, 115, 115);
        private static readonly Color WarningColor = Color.FromArgb(255, 213, 79);
        private static readonly Color InfoColor = Color.FromArgb(200, 200, 220);
        private static readonly Color QualcommColor = Color.FromArgb(239, 83, 80);

        private readonly LogHandler _log;
        private readonly Action<int, string> _updateProgress;
        private readonly Action<string> _setStatus;
        private readonly Action<bool> _setOperationState;
        private readonly Func<string> _getPythonPath;
        private readonly Func<string> _getCustomDaPath;      // _getCustomDaPath().Trim()
        private readonly FirmwareService _firmwareService;
        private readonly ProcessRunnerService _processRunner; // RunMtkSleekCommand နေရာ
        private readonly Action<string> _showGpt;             // ShowGptPartitions (grid) — Detect ပြီးရင်

        public MediaTekService(
            LogHandler log,
            Action<int, string> updateProgress,
            Action<string> setStatus,
            Action<bool> setOperationState,
            Func<string> getPythonPath,
            Func<string> getCustomDaPath,
            FirmwareService firmwareService,
            ProcessRunnerService processRunner,
            Action<string> showGpt)
        {
            _log = log ?? delegate { };
            _updateProgress = updateProgress ?? delegate { };
            _setStatus = setStatus ?? delegate { };
            _setOperationState = setOperationState ?? delegate { };
            _getPythonPath = getPythonPath ?? (() => "python");
            _getCustomDaPath = getCustomDaPath ?? (() => "");
            _firmwareService = firmwareService;
            _processRunner = processRunner;
            _showGpt = showGpt ?? delegate { };
        }

        private string Script => IOFile.Exists(AppConfig.MtkScript) ? AppConfig.MtkScript : AppConfig.MtkFallbackScript;

        public async Task DetectAsync()
        {
            _log("\n🔍 [MTK] Listening for MTK BROM Connection...", MtkColor);
            _log("📱 1. Power OFF the device completely.", MtkColor);
            _log("📱 2. Press & Hold (Volume Up + Volume Down).", MtkColor);
            _log("📱 3. Connect USB Cable NOW.", MtkColor);

            string script = AppConfig.MtkScript;
            if (!IOFile.Exists(script)) script = AppConfig.MtkFallbackScript;

            string customDaArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            string output = await _processRunner.RunMtkSleekCommand($"\"{script}\" {customDaArg}printgpt", "Connecting MTK Device...");
            if (!string.IsNullOrWhiteSpace(output))
            {
                _showGpt(output);
            }
        }
        public async Task BackupNvAsync(List<PartitionInfo> partitions, string backupDir)
        {
            string script = AppConfig.MtkScript;
            if (!IOFile.Exists(script)) script = AppConfig.MtkFallbackScript;

            _log("\n💾 [MTK] Starting One-Shot NVRAM & NVDATA Backup...", MtkColor);
            _log("📱 Power OFF device -> Hold (Vol+ & Vol-) -> Insert USB Cable", MtkColor);

            string[] nvPartitions = { "nvcfg", "nvdata", "protect1", "protect2", "nvram" };
            string partNamesArg = string.Join(",", nvPartitions);
            string filePathsArg = string.Join(",", nvPartitions.Select(p => Path.Combine(backupDir, $"{p}.img")));

            _updateProgress(10, "Starting...");
            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" r {partNamesArg} \"{filePathsArg}\"", "Backing up NV Partitions...");

            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _firmwareService.GeneratePmkScatterFile(partitions, backupDir, nvPartitions);
                _updateProgress(100, "Done");
                _log($"\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", SuccessColor);
                _log($"✅ All NV Partitions backed up successfully in ONE shot!", SuccessColor);
                _log($"📄 Auto-generated Scatter File: PMK_Android_scatter.txt", SuccessColor);
                _log($"📁 Saved Folder: {backupDir}", MtkColor);

                _log("🔄 [Auto Reboot] Restarting phone to System...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Device rebooted successfully!\n", SuccessColor);
            }
            else
            {
                _log("⚠️ Check log above for details.", WarningColor);
            }
        }
        public async Task WriteNvAsync(string inputFile, string partitionName)
        {
            string script = AppConfig.MtkScript;
            if (!IOFile.Exists(script)) script = AppConfig.MtkFallbackScript;

            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log($"\n✏️ Writing [{partitionName}] to device...", MtkColor);
            _log("📱 Connect device in BROM mode (Hold Vol+ & Vol- -> Insert USB)", MtkColor);

            string res = await _processRunner.RunProcessCommand(_getPythonPath(), $"\"{script}\" {daArg}w {partitionName} \"{inputFile}\"", $"Writing {partitionName}...", true);
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log($"✅ {partitionName} written successfully!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Device rebooted successfully!\n", SuccessColor);
            }
        }
        public async Task FormatFrpAsync()
        {
            string script = AppConfig.MtkScript;
            if (!IOFile.Exists(script)) script = AppConfig.MtkFallbackScript;

            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log("\n🔓 [MTK] Formatting FRP partition...", MtkColor);
            _log("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable", MtkColor);

            string res = await _processRunner.RunProcessCommand(_getPythonPath(), $"\"{script}\" {daArg}e frp", "Formatting FRP...", true);
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ FRP partition formatted successfully!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Phone is restarting to Welcome Screen!\n", SuccessColor);
            }
        }
        public async Task FullDumpAsync(List<PartitionInfo> partitions, string backupDir)
        {
            string script = AppConfig.MtkScript;
            if (!IOFile.Exists(script)) script = AppConfig.MtkFallbackScript;

            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log($"\n💾 [MTK] Starting Full ROM Backup (All Partitions including userdata)...", MtkColor);
            _log($"📁 Destination: {backupDir}", MtkColor);
            _log("📱 Connect device in BROM mode (Hold Vol+ & Vol- -> Insert USB)", MtkColor);

            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}rl \"{backupDir}\"", "Dumping Full ROM...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                var allParts = partitions.Select(p => p.Name).ToArray();
                _firmwareService.GeneratePmkScatterFile(partitions, backupDir, allParts.Length > 0 ? allParts : new string[] { "boot", "recovery", "super", "system", "vendor" });
                _log("✅ Full MediaTek Firmware backed up successfully!", SuccessColor);
                _log("📄 Generated MTK Scatter file successfully!", SuccessColor);

                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Device rebooted successfully!\n", SuccessColor);
            }
        }
        public async Task NormalDumpAsync(List<PartitionInfo> partitions, string backupDir)
        {
            string script = AppConfig.MtkScript;
            if (!IOFile.Exists(script)) script = AppConfig.MtkFallbackScript;

            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log($"\n💾 [MTK] Starting Normal ROM Backup (Skipping userdata/cache)...", MtkColor);
            _log($"📁 Destination: {backupDir}", MtkColor);
            _log("📱 Connect device in BROM mode (Hold Vol+ & Vol- -> Insert USB)", MtkColor);

            // mtk.py rl <dir> --skip userdata,cache
            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}rl \"{backupDir}\" --skip userdata,cache", "Dumping Normal ROM...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                var allParts = partitions.Where(p => !p.Name.Equals("userdata", StringComparison.OrdinalIgnoreCase) && !p.Name.Equals("cache", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToArray();
                _firmwareService.GeneratePmkScatterFile(partitions, backupDir, allParts.Length > 0 ? allParts : new string[] { "boot", "recovery", "super", "system", "vendor" });
                _log("✅ Normal MediaTek Firmware backed up successfully! (Small & Fast)", SuccessColor);
                _log("📄 Generated MTK Scatter file successfully!", SuccessColor);

                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Device rebooted successfully!\n", SuccessColor);
            }
        }
        public async Task UnlockBLAsync()
        {
            string script = AppConfig.MtkScript;
            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log("\n🔓 [MTK] Unlocking Bootloader (seccfg)...", MtkColor);
            _log("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable", MtkColor);

            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}da seccfg unlock", "Unlocking Bootloader...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ Bootloader Unlocked successfully!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Device is rebooting to Unlocked State!\n", SuccessColor);
            }
        }
        public async Task RelockBLAsync()
        {
            string script = AppConfig.MtkScript;
            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log("\n🔒 [MTK] Relocking Bootloader...", MtkColor);
            _log("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable", MtkColor);

            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}da seccfg lock", "Relocking Bootloader...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ Bootloader Relocked successfully!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Device is rebooting to Locked State!\n", SuccessColor);
            }
        }
        public async Task UserlockResetAsync()
        {
            string script = AppConfig.MtkScript;
            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log("\n🔑 [MTK] Resetting Userlock (Formatting userdata & metadata)...", MtkColor);
            _log("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable", MtkColor);

            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}e userdata,metadata", "Formatting Userlock...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ Screen lock removed successfully!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Phone is restarting to Factory Setup!\n", SuccessColor);
            }
        }
        public async Task MiAccountResetAsync()
        {
            string script = AppConfig.MtkScript;
            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log("\n☁️ [Xiaomi] Resetting Mi Account Lock...", MtkColor);
            _log("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable", MtkColor);

            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}e persist,frp", "Resetting Mi Account...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ Mi Account Reset completed!", SuccessColor);
                _log("⚠️ Disable OTA update after booting to prevent relocking.", WarningColor);
                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Device is rebooting!\n", SuccessColor);
            }
        }
        public async Task RemoveDemoAsync()
        {
            string script = AppConfig.MtkScript;
            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log("\n📱 [MTK] Removing Demo Mode (Oppo/Realme/Vivo)...", MtkColor);
            _log("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable", MtkColor);

            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}e opporeserve2,demo,devinfo", "Removing Demo Mode...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ Demo Mode removed successfully!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Device is rebooting to Normal Mode!\n", SuccessColor);
            }
        }
        public async Task SamsungKGResetAsync()
        {
            string script = AppConfig.MtkScript;
            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log("\n🛡️ [Samsung] Resetting KG Lock / Persistent...", MtkColor);
            _log("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable", MtkColor);

            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}e persistent,param,steady", "Resetting Samsung KG...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ Samsung KG / Persistent cleared successfully!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Phone is rebooting!\n", SuccessColor);
            }
        }
        public async Task FixNvramAsync()
        {
            string script = AppConfig.MtkScript;
            string daArg = (!string.IsNullOrWhiteSpace(_getCustomDaPath()) && IOFile.Exists(_getCustomDaPath())) ? $"--loader \"{_getCustomDaPath()}\" " : "";

            _log("\n📶 [MTK] Resetting NV Data & Sec Partitions...", MtkColor);
            _log("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable", MtkColor);

            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}e nvdata,nvcfg", "Fixing NVRAM...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ NVRAM Error cleared!", SuccessColor);
                _log("🔄 [Auto Reboot] Restarting phone...", MtkColor);
                await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                _log("📱 Phone is rebooting!\n", SuccessColor);
            }
        }
        public async Task RebootAsync()
        {
            string script = AppConfig.MtkScript;
            _log("\n🔄 [MTK] Sending Reset/Reboot command to device...", MtkColor);
            await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting Device...");
            _log("✅ Reboot command sent!", SuccessColor);
        }
    }
}
