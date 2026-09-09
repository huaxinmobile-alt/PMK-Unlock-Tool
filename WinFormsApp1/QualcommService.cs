#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Qualcomm EDL 9008 business logic service — Form1 (God Class) ကနေ ခွဲထုတ်ထားတယ်
    public class QualcommService
    {
        private static readonly Color SuccessColor = Color.FromArgb(129, 199, 132);
        private static readonly Color ErrorColor = Color.FromArgb(229, 115, 115);
        private static readonly Color WarningColor = Color.FromArgb(255, 213, 79);
        private static readonly Color InfoColor = Color.FromArgb(200, 200, 220);

        private readonly LogHandler _log;
        private readonly ProcessRunnerService _processRunner;
        private readonly LoaderService _loaderService;
        private readonly Func<string> _getSelectedPort;   // mobilePortCombo.SelectedItem
        private readonly Func<string> _getLoaderPath;     // CurrentLoaderPath (txtFirmwarePath / txtSlot1)
        private readonly Func<string> _getMemoryType;     // currentMemoryType (eMMC/UFS)

        public QualcommService(
            LogHandler log,
            ProcessRunnerService processRunner,
            LoaderService loaderService,
            Func<string> getSelectedPort,
            Func<string> getLoaderPath,
            Func<string> getMemoryType)
        {
            _log = log ?? delegate { };
            _processRunner = processRunner;
            _loaderService = loaderService;
            _getSelectedPort = getSelectedPort ?? (() => "");
            _getLoaderPath = getLoaderPath ?? (() => "");
            _getMemoryType = getMemoryType ?? (() => "eMMC");
        }

        // ================= USB transport state =================
        private bool usb9008Available = false;
        private DateTime usbProbeTime = DateTime.MinValue;

        public bool UseUsbTransport()
        {
            if (IsUsb9008Available()) return true; // device က libusb ကနေ မြင်ရပြီးသား
            try
            {
                if (SerialPort.GetPortNames().Length == 0) return true; // COM port မရှိ = WinUSB driver setup
            }
            catch (Exception ex) { _log($"⚠️ UseUsbTransport warning: {ex.Message}", WarningColor); }
            return false;
        }
        public bool IsUsb9008Available()
        {
            if ((DateTime.Now - usbProbeTime).TotalSeconds < 3) return usb9008Available;
            usbProbeTime = DateTime.Now;
            try
            {
                // device ကို မြင်ရုံနဲ့ မလုံလောက် — libusb က တကယ် OPEN လို့ရမှ USB mode မှန်တယ်
                // (usbser driver နဲ့ ချိတ်ထားရင် find က YES ပြန်ပေမဲ့ open မရတတ်လို့)
                string probe = _processRunner.RunPythonOneShot("-c \"import usb.core\nok=False\ntry:\n d=usb.core.find(idVendor=0x05c6,idProduct=0x9008)\n if d is not None:\n  d.get_active_configuration()\n  ok=True\nexcept Exception:\n pass\nprint('YES' if ok else 'NO')\"", 12);
                usb9008Available = probe != null && probe.Contains("YES");
            }
            catch (Exception ex) { _log($"⚠️ IsUsb9008Available warning: {ex.Message}", WarningColor); usb9008Available = false; }
            return usb9008Available;
        }
        public string GetEdlLoaderArg()
        {
            // Memory type (eMMC/UFS) ကို ထည့်ပေးပါ — မထည့်ရင် stock edl က UFS (4096 sector) လို့ မှတ်ယူလို့ eMMC (512) ဖုန်းတွေမှာ sector error တက်ပါတယ်
            // USB (WinUSB bulk) transport ရှိရင် USB mode သုံးမယ် - serial ထက် ~3.5x မြန်ပါတယ်
            bool usbMode = UseUsbTransport();
            string args = usbMode
                ? $"--memory={_getMemoryType()} "
                : $"--serial --memory={_getMemoryType()} ";

            if (!usbMode)
            {
                string selectedPort = _getSelectedPort();
                string targetPort = "";

                if (selectedPort.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                    targetPort = selectedPort;
                else
                {
                    string[] ports = SerialPort.GetPortNames();
                    if (ports.Length > 0)
                        targetPort = ports.FirstOrDefault(p => p.StartsWith("COM", StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrEmpty(targetPort))
                    args += $"--portname={targetPort} ";
            }

            string loader = _getLoaderPath();
            if (!string.IsNullOrEmpty(loader))
                args += $"--loader=\"{loader}\" ";

            // Xiaomi Signature Bypass ကို Auto တွဲပေးခြင်း
            // (edl.py က --sig option ကို ထောက်ခံမှသာ ထည့်ပေးပါ)
            string sigFile = _loaderService.FindXiaomiSigFile();
            if (!string.IsNullOrEmpty(sigFile) && EdlSupportsSig())
            {
                args += $"--sig=\"{sigFile}\" ";
            }

            return args;
        }
        public string GetEdlResetArgs()
        {
            if (UseUsbTransport()) return "";
            try
            {
                string[] ports = SerialPort.GetPortNames();
                if (ports.Length > 0) return $"--serial --portname={ports[0]} ";
            }
            catch (Exception ex) { _log($"⚠️ GetEdlResetArgs warning: {ex.Message}", WarningColor); }
            return "--serial ";
        }
        public bool EdlSupportsSig()
        {
            try
            {
                return IOFile.Exists(AppConfig.EdlScript) &&
                       IOFile.ReadAllText(AppConfig.EdlScript).Contains("--sig", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { _log($"⚠️ EdlSupportsSig fallback: {ex.Message}", WarningColor); return false; }
        }
        public string EnsureQcPatchFile(string romDir, string patchXml)
        {
            if (!string.IsNullOrEmpty(patchXml) && IOFile.Exists(Path.Combine(romDir, patchXml)))
                return patchXml;
            string empty = Path.Combine(romDir, "_pmk_empty_patch.xml");
            if (!IOFile.Exists(empty))
                IOFile.WriteAllText(empty, "<?xml version=\"1.0\" ?>\n<patches>\n</patches>");
            return Path.GetFileName(empty);
        }

        // CARDAPP → PMKDAPP patch core — patched ဖိုင် ထုတ်ပေးပြီး path ပြန်တယ် (မအောင်ရင် null)
        // ================= CARDAPP → PMKDAPP streaming patch (memory-safe) =================
        // NON-HLOS modem ဖိုင်က 100-300MB+ ဖြစ်နိုင်လို့ file တစ်ခုလုံး RAM ထဲ တင်ပြီး Latin1 string
        // ပြောင်းတာကို ရှောင်ပြီး 4MB chunk နဲ့ byte-level ရှာ/ပြင်တယ်။
        private static readonly byte[] CardAppFind = { 0x43, 0x41, 0x52, 0x44, 0x41, 0x50, 0x50 }; // "CARDAPP"
        private static readonly byte[] PmkdAppRepl = { 0x50, 0x4D, 0x4B, 0x44, 0x41, 0x50, 0x50 }; // "PMKDAPP"
        private const int PatchChunkSize = 4 * 1024 * 1024;

        // ဖတ်ရမဲ့ bytes တွေ အကုန် ရအောင် ဖတ်တယ် (stream က နည်းနည်းချင်း ပြန်ပေးရင်လည်း)
        private static void ReadExact(Stream s, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int n = s.Read(buffer, offset, count - offset);
                if (n <= 0) break;
                offset += n;
            }
        }

        // CARDAPP နေရာတွေ + PMKDAPP ရှိပြီးသားလားဆိုတာ streaming ရှာတယ်။
        // Chunk စပ်မှာ 7-byte pattern ပြတ်နေရင် မလွတ်အောင် chunk ရဲ့ နောက်ဆုံး (pattern-1) bytes
        // ကို overlap ပြန်ထည့်ဖတ်ပြီး (pattern က tail ထဲမှာ စလို့ မရတဲ့အတွက်) hit တိုင်းကို တစ်ခါတည်း တွေ့တယ်။
        public static (List<long> Offsets, bool AlreadyPmkd) ScanCardAppOffsets(string src)
        {
            var offsets = new List<long>();
            bool alreadyPmkd = false;
            using var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, PatchChunkSize, FileOptions.SequentialScan);
            long len = fs.Length;
            if (len < CardAppFind.Length) return (offsets, false);

            int tail = CardAppFind.Length - 1;
            byte[] buf = new byte[PatchChunkSize + tail];
            long abs = 0;
            long remaining = len;
            while (remaining > 0)
            {
                int want = (int)Math.Min(buf.Length, remaining);
                fs.Position = abs;
                ReadExact(fs, buf, want);

                var span = buf.AsSpan(0, want);
                if (!alreadyPmkd && span.IndexOf(PmkdAppRepl) >= 0) alreadyPmkd = true;
                int consumed = 0;
                int j = span.IndexOf(CardAppFind);
                while (j >= 0)
                {
                    offsets.Add(abs + consumed + j);
                    consumed += j + CardAppFind.Length;
                    span = span.Slice(j + CardAppFind.Length);
                    j = span.IndexOf(CardAppFind);
                }

                abs += PatchChunkSize;
                remaining -= PatchChunkSize;
            }
            return (offsets, alreadyPmkd);
        }

        // Scan ရလာတဲ့ offsets တွေအတိုင်း src → dst streaming ပုံတူဖိုင် ရေးတယ်။
        // Hit တစ်ခုရောက်တိုင်း အဲ့ဒီ 7 bytes ကို မူရင်းကနေ မဖတ်ဘဲ REPL နဲ့ ချရေးပြီး ရှေ့ဆက်တယ်
        // → chunk စပ်မှာ ပြတ်နေတဲ့ hit တွေပါ မပျက်၊ memory က constant ပဲ။
        public static void WritePatchedFile(string src, string dst, List<long> offsets)
        {
            using var inFs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, PatchChunkSize, FileOptions.SequentialScan);
            using var outFs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, PatchChunkSize, FileOptions.SequentialScan);
            byte[] buf = new byte[PatchChunkSize];
            long fileLen = inFs.Length;
            long pos = 0; // output/input stream မှာ ရောက်နေတဲ့ absolute position
            int oi = 0;
            while (pos < fileLen)
            {
                long nextHit = oi < offsets.Count ? offsets[oi] : long.MaxValue;
                long segEnd = Math.Min(nextHit, fileLen);

                // hit မရောက်ခင် segment တွေကို ပုံမှန် copy
                while (pos < segEnd)
                {
                    int want = (int)Math.Min(buf.Length, segEnd - pos);
                    ReadExact(inFs, buf, want);
                    outFs.Write(buf, 0, want);
                    pos += want;
                }
                if (pos >= fileLen) break;

                // offset ကျတဲ့ 7 bytes နေရာမှာ PMKDAPP ရေးတယ် (input ထဲက မူရင်း 7 bytes ကို skip)
                if (inFs.Position != pos + CardAppFind.Length) inFs.Position = pos + CardAppFind.Length;
                outFs.Write(PmkdAppRepl, 0, PmkdAppRepl.Length);
                pos += PmkdAppRepl.Length;
                oi++;
            }
        }

        public string PatchModemFileCore(string src)
        {
            try
            {
                var (offsets, alreadyPmkd) = ScanCardAppOffsets(src);

                if (offsets.Count == 0)
                {
                    if (alreadyPmkd)
                        _log("ℹ️ 'PMKDAPP' ရှိနေပြီးသား — patch လုပ်ပြီးသားပါ။", InfoColor);
                    else
                        _log("❌ 'CARDAPP' မတွေ့ပါ — ဒီ device/build အတွက် ဒီနည်း မသက်ဆိုင်တာ ဖြစ်နိုင်တယ်။", ErrorColor);
                    return null;
                }

                string dir = Path.GetDirectoryName(src);
                string baseName = Path.GetFileNameWithoutExtension(src);
                string backupPath = Path.Combine(dir, baseName + "_original.bak");
                string outPath = Path.Combine(dir, baseName + "_PMK_bypass.bin");

                try { if (!IOFile.Exists(backupPath)) IOFile.Copy(src, backupPath); } catch (Exception ex) { _log($"⚠️ Original backup (.bak) ကူးရာမှာ မအောင်မြင်ပါ: {ex.Message}", WarningColor); }

                WritePatchedFile(src, outPath, offsets);
                foreach (long off in offsets)
                    _log($"  ✏️ Offset 0x{off:X8}: CARDAPP → PMKDAPP", SuccessColor);
                _log($"✅ Patch ပြီးပါပြီ — {offsets.Count} နေရာ အစားထိုးပြီး။", SuccessColor);
                _log($"📁 Patched file : {outPath}", SuccessColor);
                return outPath;
            }
            catch (Exception ex)
            {
                _log("❌ Patch error: " + ex.Message, ErrorColor);
                return null;
            }
        }
    }
}
