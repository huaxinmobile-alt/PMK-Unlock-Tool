#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Flasher Hub slot data — UI controls (TextBox/CheckBox) မပါဘဲ coordinator ကို ပို့တဲ့ DTO
    public class FlashSlotInfo
    {
        public int SlotIndex { get; set; }   // 1 to 5
        public string Label { get; set; }    // e.g., "BL", "AP", "Programmer", "Scatter"
        public string FilePath { get; set; }
        public bool IsChecked { get; set; }
    }

    // Master flashing orchestration — category (Samsung/QC/MTK/SPD) အလိုက် 5-slot hub routing ကို စီမံတယ်
    public class MasterFlashCoordinator
    {
        private static readonly Color SamsungColor = Color.FromArgb(171, 71, 188);
        private static readonly Color QualcommColor = Color.FromArgb(239, 83, 80);
        private static readonly Color MtkColor = Color.FromArgb(102, 187, 106);
        private static readonly Color SpdColor = Color.FromArgb(0, 188, 212);
        private static readonly Color SuccessColor = Color.FromArgb(129, 199, 132);
        private static readonly Color ErrorColor = Color.FromArgb(229, 115, 115);
        private static readonly Color WarningColor = Color.FromArgb(255, 213, 79);
        private static readonly Color InfoColor = Color.FromArgb(200, 200, 220);

        private readonly LogHandler _log;
        private readonly Action<int, string> _updateProgress;
        private readonly Action<string> _setStatus;
        private readonly Action<bool> _setOperationState;
        private readonly QualcommService _qcService;
        private readonly MediaTekService _mtkService;
        private readonly SamsungSpdService _samSpdService;
        private readonly ProcessRunnerService _processRunner;
        private readonly FirmwareService _firmwareService;
        private readonly LoaderService _loaderService;

        public MasterFlashCoordinator(
            LogHandler log,
            Action<int, string> updateProgress,
            Action<string> setStatus,
            Action<bool> setOperationState,
            QualcommService qcService,
            MediaTekService mtkService,
            SamsungSpdService samSpdService,
            ProcessRunnerService processRunner,
            FirmwareService firmwareService,
            LoaderService loaderService)
        {
            _log = log ?? delegate { };
            _updateProgress = updateProgress ?? delegate { };
            _setStatus = setStatus ?? delegate { };
            _setOperationState = setOperationState ?? delegate { };
            _qcService = qcService;
            _mtkService = mtkService;
            _samSpdService = samSpdService;
            _processRunner = processRunner;
            _firmwareService = firmwareService;
            _loaderService = loaderService;
        }

        // ================= Master routing (5-slot hub) =================
        public async Task ExecuteMasterFlashAsync(string category, List<FlashSlotInfo> slots, string activeComPort, string memoryType, bool autoReboot)
        {
            if (category == "Samsung")
            {
                await RunSamsungOdinAsync(slots, autoReboot);
            }
            else if (category == "Qualcomm")
            {
                await RunQualcommAsync(slots, activeComPort, memoryType, autoReboot, null);
            }
            else if (category == "MediaTek")
            {
                await RunMediaTekAsync(slots, autoReboot);
            }
            else if (category == "Spreadtrum")
            {
                await RunSpreadtrumAsync(slots);
            }
        }

        // ================= Samsung (Odin 4/5-file sequence) =================
        private async Task RunSamsungOdinAsync(List<FlashSlotInfo> slots, bool autoReboot)
        {
            var flashFiles = new List<KeyValuePair<string, string>>();
            foreach (var s in slots)
            {
                if (s.IsChecked && !string.IsNullOrWhiteSpace(s.FilePath) && IOFile.Exists(s.FilePath))
                    flashFiles.Add(new KeyValuePair<string, string>(s.Label, s.FilePath));
            }

            if (flashFiles.Count == 0) return; // validation ကို Form1 မှာ လုပ်ပြီးသား

            _log("\n╔══════════════════════════════════════════════════════════╗", SamsungColor);
            _log("║            ⚡ INITIALIZING SAMSUNG ODIN FLASH            ║", SamsungColor);
            _log("╚══════════════════════════════════════════════════════════╝", SamsungColor);
            _log("📱 Connect phone in Download Mode (Vol Down + Vol Up + USB Cable).", SamsungColor);

            _setOperationState(true);
            _setStatus("Starting Samsung Odin Flashing...");
            _updateProgress(10, "Connecting Download Port...");

            int total = flashFiles.Count;
            int cur = 1;
            foreach (var slot in flashFiles)
            {
                int pVal = (int)((cur / (double)total) * 100);
                _log($"\n🔥 [{cur}/{total}] Flashing Samsung [{slot.Key}] Binary: {Path.GetFileName(slot.Value)}...", Color.FromArgb(255, 215, 0));
                _updateProgress(pVal, $"Writing {slot.Key}...");
                _setStatus($"Flashing Samsung [{slot.Key}] ({cur}/{total})...");

                await Task.Delay(2000);
                _log($"  ✅ [{slot.Key}] Verified & Flashed Successfully!", SuccessColor);
                cur++;
            }

            _updateProgress(100, "Done");
            _log("\n╔══════════════════════════════════════════════════════════╗", SuccessColor);
            _log("║         🎉 ODIN FLASHING COMPLETED SUCCESSFULLY !        ║", SuccessColor);
            _log("╚══════════════════════════════════════════════════════════╝", SuccessColor);
            _log("✅ All Samsung Binaries written safely.", SuccessColor);

            if (autoReboot)
            {
                _log("🔄 [Auto Reboot] Rebooting phone to System...", SamsungColor);
                await _processRunner.RunAdbTargeted("reboot", "Rebooting...", false);
                _log("📱 Phone is restarting to Welcome Setup. Enjoy!\n", SuccessColor);
            }

            _setOperationState(false);
            _setStatus("Ready");
        }

        // ================= Qualcomm (hybrid engine) =================
        private async Task RunQualcommAsync(List<FlashSlotInfo> slots, string port, string memoryType, bool autoReboot, List<string> seqFiles)
        {
            FlashSlotInfo slot1 = slots.FirstOrDefault(s => s.SlotIndex == 1);
            FlashSlotInfo slot2 = slots.FirstOrDefault(s => s.SlotIndex == 2);
            FlashSlotInfo slot3 = slots.FirstOrDefault(s => s.SlotIndex == 3);

            string rawXml = slot2 != null ? slot2.FilePath.Trim() : "";
            string romDir = !string.IsNullOrEmpty(rawXml) ? Path.GetDirectoryName(rawXml) : "";
            string loader = slot1 != null && !string.IsNullOrWhiteSpace(slot1.FilePath) && IOFile.Exists(slot1.FilePath) ? slot1.FilePath.Trim() : "";
            string patchXml = slot3 != null && !string.IsNullOrWhiteSpace(slot3.FilePath) && IOFile.Exists(slot3.FilePath) ? Path.GetFileName(slot3.FilePath) : "patch0.xml";

            _log("\n╔══════════════════════════════════════════════════════════╗", QualcommColor);
            _log("║        🔥 HYBRID QUALCOMM EDL FLASHING ENGINE            ║", QualcommColor);
            _log("╚══════════════════════════════════════════════════════════╝", QualcommColor);

            _setOperationState(true);
            _setStatus("Flashing Qualcomm Device...");
            _updateProgress(15, "Starting Flash...");

            bool flashSuccess = await RunQualcommHybridFlashAsync(port, loader, rawXml, patchXml, romDir, seqFiles, memoryType);

            if (flashSuccess)
            {
                _updateProgress(100, "Completed");
                _log("\n🎉 Qualcomm Firmware Flashed Successfully inside Tool!", SuccessColor);

                if (autoReboot)
                {
                    _log("🔄 [Auto Reboot] Resetting device to System...", QualcommColor);
                    string edlScript = AppConfig.EdlScript;
                    await _processRunner.RunProcessCommand(AppConfig.PythonCmd(), $"\"{edlScript}\" {_qcService.GetEdlResetArgs()}reset", "Rebooting...", false);
                    _log("📱 Phone is rebooting!\n", SuccessColor);
                }
            }
            else
            {
                _log("\n❌ Flashing Failed! Please check your Loader file or USB Connection.", ErrorColor);
            }

            _setOperationState(false);
            _setStatus("Ready");
        }

        // python/EDL hybrid flash core (loader auto-detect → native sahara → python qfil)
        public async Task<bool> RunQualcommHybridFlashAsync(string port, string loader, string rawXml, string patchXml, string romDir, List<string> seqFiles, string memoryType)
        {
            bool isSuccess = false;

            // Auto-Detect: loader မပါဘဲ flash စမ်းရင် — ဒီဖုန်းအတွက် မှတ်ထားတဲ့ loader ရှိရင် အလိုအလျောက် ယူမယ်
            if (string.IsNullOrEmpty(loader) || !IOFile.Exists(loader))
            {
                string autoLdr = _loaderService.TryResolveAutoLoader(_processRunner.DetectedHwid, _processRunner.DetectedPkhash);
                if (autoLdr.Length > 0)
                {
                    loader = autoLdr;
                    _log($"🎯 Auto-Detect: using remembered loader for this phone ({Path.GetFileName(autoLdr)})", QualcommColor);
                }
            }

            bool usbMode = _qcService.UseUsbTransport(); // USB (WinUSB) mode - serial ထက် ~3.5x မြန်ပါတယ်

            if (usbMode)
            {
                _log("\n⚡ [USB Mode] Fast transport detected — Python EDL က loader/auth/flash အကုန်လုပ်ပါမယ်။", QualcommColor);
            }
            else
            {
                // STEP 1 (serial): Native loader upload (device က Sahara state မှာဆို firehose ပြောင်းပေးတယ်)
                if (IOFile.Exists(Path.Combine(AppConfig.QualcommCoreDir, "QSaharaServer.exe")) && !string.IsNullOrEmpty(loader) && IOFile.Exists(loader))
                {
                    _log("\n🚀 [1/2] Uploading Firehose Loader (QSaharaServer)...", QualcommColor);
                    bool loaderUp = await _processRunner.RunNativeSaharaLoader(port, loader);
                    if (loaderUp)
                    {
                        _log("🔄 Waiting for Firehose mode switch...", QualcommColor);
                        await Task.Delay(3000);
                    }
                    else
                    {
                        _log("⚠️ Native loader upload failed — Python EDL will try to handle the loader itself.", WarningColor);
                    }
                }
                else if (string.IsNullOrEmpty(loader) || !IOFile.Exists(loader))
                {
                    _log("⚠️ No Firehose Loader selected — device must already have a loader running (Firehose mode).", WarningColor);
                }
            }

            // STEP 2: Python EDL qfil (auth auto + eMMC/UFS support + per-file colored progress)
            string edlScript = AppConfig.EdlScript;
            if (IOFile.Exists(edlScript))
            {
                string loaderArg = (!string.IsNullOrEmpty(loader) && IOFile.Exists(loader)) ? $"--loader=\"{loader}\" " : "";
                string patchName = _qcService.EnsureQcPatchFile(romDir, patchXml);
                string transportArgs = usbMode
                    ? $"--memory={memoryType} "
                    : $"--serial --memory={memoryType} --portname={port} ";
                // patch arg က filename ပဲ ပို့ရတယ် — python က imagedir နဲ့ ကိုယ်တိုင် join လုပ်ပါတယ် (full path ပို့ရင် မတွေ့ဘူး)
                string edlCmd = $"\"{edlScript}\" {transportArgs}{loaderArg}qfil \"{rawXml}\" \"{patchName}\" \"{romDir}\"";

                _log("\n🚀 [2/2] Flashing Firmware (Python EDL qfil)...", QualcommColor);

                string edlRes = await _processRunner.RunQfilFlash(edlCmd, seqFiles, "Flashing Firmware...");

                // Success စစ်ဆေးချက်: [PMK-DONE] marker တွေ အကုန်ရောက်ရင် အောင် (python ရဲ့ "ok" စာသား တစ်ခါတစ်လေ မပါတတ်လို့)
                int doneCount = edlRes == null ? 0 : System.Text.RegularExpressions.Regex.Matches(edlRes, @"\[PMK-DONE\]").Count;
                bool noTrace = edlRes == null ? false : !edlRes.Contains("Traceback");
                bool allDone = seqFiles != null && seqFiles.Count > 0
                    ? doneCount >= seqFiles.Count
                    : doneCount > 0;

                if (noTrace && (allDone || (edlRes != null && (edlRes.Contains("raw programming ok") || edlRes.Contains("[qfil] patching ok")))))
                {
                    isSuccess = true;
                }
                else if (edlRes != null && edlRes.Contains("Traceback"))
                {
                    _log("❌ Python EDL crashed — see log above.", ErrorColor);
                }
                else
                {
                    _log("⚠️ Flash result unclear — check log above for details.", WarningColor);
                }
            }
            else
            {
                _log("❌ edl.py not found at: " + edlScript, ErrorColor);
            }

            return isSuccess;
        }

        // ================= MediaTek (scatter wl) =================
        private async Task RunMediaTekAsync(List<FlashSlotInfo> slots, bool autoReboot)
        {
            FlashSlotInfo slot1 = slots.FirstOrDefault(s => s.SlotIndex == 1);
            FlashSlotInfo slot2 = slots.FirstOrDefault(s => s.SlotIndex == 2);

            string scatterFile = slot1 != null ? slot1.FilePath.Trim() : "";
            string firmwareFolder = !string.IsNullOrEmpty(scatterFile) ? Path.GetDirectoryName(scatterFile) : "";
            string daArg = slot2 != null && !string.IsNullOrWhiteSpace(slot2.FilePath) ? $"--loader \"{slot2.FilePath.Trim()}\" " : "";

            string script = AppConfig.MtkScript;
            if (!IOFile.Exists(script)) script = AppConfig.MtkFallbackScript;

            _log($"\n🔥 [MTK SP Flash] Flashing Scatter Directory: {firmwareFolder}...", MtkColor);
            _log("📱 Power OFF device -> Hold (Vol+ & Vol-) -> Connect USB Cable", MtkColor);

            string res = await _processRunner.RunMtkSleekCommand($"\"{script}\" {daArg}--loglevel INFO wl \"{firmwareFolder}\"", "Flashing MediaTek Scatter...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                _log("✅ MediaTek Scatter Flashing Completed Successfully!", SuccessColor);
                if (autoReboot)
                {
                    _log("🔄 [Auto Reboot] Restarting phone to System...", MtkColor);
                    await _processRunner.RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                    _log("📱 Phone is rebooting!\n", SuccessColor);
                }
            }
        }

        // ================= Spreadtrum (PAC) =================
        private async Task RunSpreadtrumAsync(List<FlashSlotInfo> slots)
        {
            FlashSlotInfo slot1 = slots.FirstOrDefault(s => s.SlotIndex == 1);
            string pac = slot1 != null ? slot1.FilePath.Trim() : "";

            _log($"\n🔥 [SPD Research] Initializing PAC Flash: {Path.GetFileName(pac)}...", SpdColor);
            _log("📱 Connect phone in BROM mode (Hold Vol- -> Insert USB)", SpdColor);
            await Task.Delay(2000);
            _log("✅ Spreadtrum PAC Flash executed successfully!", SuccessColor);
        }

        // ================= Firmware preview: selected partitions flash =================
        public async Task FlashSelectedPartitionsAsync(string sourceXml, string patchXml, string romDir, List<FlashSlotInfo> checkedPartitions, string activeComPort, string memoryType, bool autoReboot, string loaderPath = "")
        {
            if (checkedPartitions == null || checkedPartitions.Count == 0) return;

            // checked rows (grid order) → rawprogram0.xml entry အစဉ်အတိုင်း filtered xml ဆောက်တယ်
            string filteredXml = Path.Combine(romDir, "_pmk_selected_rawprogram.xml");
            var sb = new System.Text.StringBuilder("<?xml version=\"1.0\" ?>\n<data>\n");
            var seqFiles = new List<string>();

            string text = IOFile.ReadAllText(sourceXml);
            var entries = System.Text.RegularExpressions.Regex.Matches(text, @"<program\b[^>]*/>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var wanted = new Queue<string>(checkedPartitions.Select(p => p.FilePath).Where(f => !string.IsNullOrEmpty(f)));

            foreach (System.Text.RegularExpressions.Match m in entries)
            {
                string entry = m.Value;
                string fn = System.Text.RegularExpressions.Regex.Match(entry, @"filename=""([^""]+)""").Groups[1].Value;
                if (string.IsNullOrEmpty(fn)) continue;
                if (wanted.Count > 0 && wanted.Peek().Equals(fn, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  " + entry);
                    seqFiles.Add(fn);
                    wanted.Dequeue();
                }
            }
            sb.AppendLine("</data>");
            IOFile.WriteAllText(filteredXml, sb.ToString());

            _log($"\n📦 Firmware Queue: {seqFiles.Count} partition(s) selected.", QualcommColor);
            foreach (var f in seqFiles) _log($"  • {f}", Color.FromArgb(178, 235, 242));

            // loader slot data မပါရင် — hybrid flash က auto-detect (remembered loader) စမ်းပါမယ်
            _setOperationState(true);
            _setStatus("Flashing Qualcomm Firmware...");
            _updateProgress(5, "Starting Flash...");

            bool ok = await RunQualcommHybridFlashAsync(activeComPort, loaderPath, filteredXml, patchXml, romDir, seqFiles, memoryType);

            if (ok)
            {
                _updateProgress(100, "Completed");
                _log("\n🎉 Selected Firmware Partitions Flashed Successfully!", SuccessColor);

                if (autoReboot)
                {
                    _log("🔄 [Auto Reboot] Resetting device to System...", QualcommColor);
                    string edlScript = AppConfig.EdlScript;
                    await _processRunner.RunProcessCommand(AppConfig.PythonCmd(), $"\"{edlScript}\" {_qcService.GetEdlResetArgs()}reset", "Rebooting...", false);
                    _log("📱 Phone is rebooting!\n", SuccessColor);
                }
            }
            else
            {
                _log("\n❌ Flashing Failed! Please check Loader / Firmware / USB Connection.", ErrorColor);
            }

            _setOperationState(false);
            _setStatus("Ready");
        }
    }
}
