using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Firmware inspection / parsing / generation methods ကို Form1 (God Class) ကနေ ခွဲထုတ်ထားတဲ့ service
    // — UI logging ကို delegate (LogHandler) ကနေ ထိုးသွင်းတယ်။ ဒီ service ထဲမှာ UI control တွေ မထိတော့ဘူး။
    public delegate void LogHandler(string message, Color color);

    // Partition data (Form1 ရဲ့ DataGridView + FirmwareService နှစ်ခုလုံး သုံး)
    // — FirmwareService ရဲ့ public API တွေမှာ ထုတ်သုံးထားလို့ public ဖြစ်ရတယ်
    public class PartitionInfo
    {
        public string Name { get; set; } = "";
        public string Offset { get; set; } = "";
        public string Length { get; set; } = "";
        public string Type { get; set; } = "";
    }

    public class FirmwareService
    {
        // Form1 ရဲ့ color fields တွေနဲ့ တူညီတဲ့ အရောင်တွေ (log output အရောင်တွေ မပြောင်းစေရဘူး)
        private static readonly Color AdbColor = Color.FromArgb(100, 181, 246);
        private static readonly Color FastbootColor = Color.FromArgb(255, 167, 38);
        private static readonly Color MtkColor = Color.FromArgb(102, 187, 106);
        private static readonly Color QualcommColor = Color.FromArgb(239, 83, 80);
        private static readonly Color SamsungColor = Color.FromArgb(171, 71, 188);
        private static readonly Color SpdColor = Color.FromArgb(0, 188, 212);
        private static readonly Color SuccessColor = Color.FromArgb(129, 199, 132);
        private static readonly Color ErrorColor = Color.FromArgb(229, 115, 115);
        private static readonly Color WarningColor = Color.FromArgb(255, 213, 79);
        private static readonly Color InfoColor = Color.FromArgb(200, 200, 220);

        private readonly LogHandler _log;

        public FirmwareService(LogHandler logHandler)
        {
            _log = logHandler ?? delegate { };
        }

        // ================= Inspect Firmwares =================
        public void InspectTarFirmware(string filePath, string slotName)
        {
            try
            {
                FileInfo fi = new FileInfo(filePath);
                double sizeMB = fi.Length / (1024.0 * 1024.0);
                string szStr = sizeMB >= 1024 ? $"{sizeMB / 1024.0:F2} GB" : $"{sizeMB:F2} MB";

                _log($"\n╔══════════════════════════════════════════════════════════╗", SamsungColor);
                _log($"║         📦 SAMSUNG BINARY LOADED [{slotName.PadRight(8)}]            ║", SamsungColor);
                _log($"╚══════════════════════════════════════════════════════════╝", SamsungColor);
                _log($"  • File Name  : {fi.Name}", SuccessColor);
                _log($"  • Total Size : {szStr}", InfoColor);

                var insideImages = new List<string>();
                using (FileStream fs = IOFile.OpenRead(filePath))
                {
                    byte[] buffer = new byte[512];
                    while (fs.Read(buffer, 0, 512) == 512 && insideImages.Count < 20)
                    {
                        string entryName = System.Text.Encoding.ASCII.GetString(buffer, 0, 100).Trim('\0', ' ');
                        if (!string.IsNullOrEmpty(entryName) && (entryName.EndsWith(".img") || entryName.EndsWith(".lz4") || entryName.EndsWith(".bin") || entryName.EndsWith(".pit")))
                        {
                            insideImages.Add(entryName);
                            string sizeOctal = System.Text.Encoding.ASCII.GetString(buffer, 124, 11).Trim('\0', ' ');
                            long entryBytes = 0;
                            try { entryBytes = Convert.ToInt64(sizeOctal, 8); } catch (Exception ex) { _log($"❌ InspectTarFirmware entry parse failed: {ex.Message}", ErrorColor); }
                            long blocks = (entryBytes + 511) / 512;
                            fs.Seek(blocks * 512, SeekOrigin.Current);
                        }
                    }
                }

                if (insideImages.Count > 0)
                {
                    _log("  • Partitions Inside Binary Archive:", FastbootColor);
                    foreach (var img in insideImages)
                    {
                        _log($"    ➔ 📄 {img}", Color.FromArgb(178, 235, 242));
                    }
                }
                _log("────────────────────────────────────────────────────────────\n", SamsungColor);
            }
            catch (Exception ex) { _log($"❌ InspectTarFirmware failed: {ex.Message}", ErrorColor); }
        }

        public void InspectXmlFirmware(string xmlPath)
        {
            try
            {
                string text = IOFile.ReadAllText(xmlPath);
                var matches = Regex.Matches(text, @"filename=""(.*?)""\s+label=""(.*?)""", RegexOptions.IgnoreCase);

                _log($"\n╔══════════════════════════════════════════════════════════╗", QualcommColor);
                _log("║          📦 QUALCOMM RAWPROGRAM XML LOADED               ║", QualcommColor);
                _log("╚══════════════════════════════════════════════════════════╝", QualcommColor);
                _log($"  • XML Name     : {Path.GetFileName(xmlPath)}", SuccessColor);
                _log($"  • Total Images : {matches.Count} partition images parsed", InfoColor);
                _log("────────────────────────────────────────────────────────────", QualcommColor);
                _log("📋 [Partitions Queue to Flash]:", AdbColor);

                int idx = 1;
                foreach (Match m in matches)
                {
                    if (idx <= 15)
                    {
                        _log($"  [{idx++:D2}] 🎯 {m.Groups[2].Value.PadRight(16)} ➔ 📁 {m.Groups[1].Value}", Color.FromArgb(179, 229, 252));
                    }
                }
                if (matches.Count > 15) _log($"  ... and {matches.Count - 15} more partitions", InfoColor);
                _log("────────────────────────────────────────────────────────────\n", QualcommColor);
            }
            catch (Exception ex) { _log($"❌ InspectXmlFirmware failed: {ex.Message}", ErrorColor); }
        }

        public void InspectScatterFirmware(string scatterPath)
        {
            try
            {
                string[] lines = IOFile.ReadAllLines(scatterPath);
                string platform = "MTK Universal";
                string storageType = "EMMC";
                int partCount = 0;

                foreach (string line in lines)
                {
                    if (line.StartsWith("platform:", StringComparison.OrdinalIgnoreCase)) platform = line.Substring(line.IndexOf(":") + 1).Trim();
                    else if (line.StartsWith("storage:", StringComparison.OrdinalIgnoreCase)) storageType = line.Substring(line.IndexOf(":") + 1).Trim();
                    else if (line.StartsWith("partition_name:", StringComparison.OrdinalIgnoreCase)) partCount++;
                }

                _log($"\n╔══════════════════════════════════════════════════════════╗", MtkColor);
                _log("║             📦 MEDIATEK SCATTER LOADED                   ║", MtkColor);
                _log("╚══════════════════════════════════════════════════════════╝", MtkColor);
                _log($"  • Scatter File : {Path.GetFileName(scatterPath)}", SuccessColor);
                _log($"  • Target SoC   : {platform} [{storageType}]", InfoColor);
                _log($"  • Partitions   : {partCount} defined in partition layout", SuccessColor);
                _log("────────────────────────────────────────────────────────────\n", MtkColor);
            }
            catch (Exception ex) { _log($"❌ InspectScatterFirmware failed: {ex.Message}", ErrorColor); }
        }

        public void InspectPacFirmware(string pacPath)
        {
            try
            {
                FileInfo fi = new FileInfo(pacPath);
                double sizeMB = fi.Length / (1024.0 * 1024.0);

                _log($"\n╔══════════════════════════════════════════════════════════╗", SpdColor);
                _log("║             📦 SPREADTRUM PAC FIRMWARE LOADED            ║", SpdColor);
                _log("╚══════════════════════════════════════════════════════════╝", SpdColor);
                _log($"  • PAC Name     : {fi.Name}", SuccessColor);
                _log($"  • Package Size : {sizeMB:F2} MB", InfoColor);
                _log("────────────────────────────────────────────────────────────\n", SpdColor);
            }
            catch (Exception ex) { _log($"❌ InspectPacFirmware failed: {ex.Message}", ErrorColor); }
        }

        public string FormatKbSize(string kbStr)
        {
            try
            {
                double kb = Convert.ToDouble(kbStr, System.Globalization.CultureInfo.InvariantCulture);
                double bytes = kb * 1024.0;
                if (bytes >= 1024.0 * 1024.0 * 1024.0) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                if (bytes >= 1024.0 * 1024.0) return $"{bytes / (1024.0 * 1024.0):F2} MB";
                return $"{kb:F0} KB";
            }
            catch (Exception ex) { _log($"⚠️ FormatKbSize fallback: {ex.Message}", WarningColor); return kbStr; }
        }

        public void GenerateQualcommRawprogram(List<PartitionInfo> parts, string backupDir)
        {
            try
            {
                string xmlPath = Path.Combine(backupDir, "rawprogram0.xml");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" ?>");
                sb.AppendLine("<data>");

                foreach (var p in parts)
                {
                    sb.AppendLine($"  <program SECTOR_SIZE_IN_BYTES=\"512\" file_sector_offset=\"0\" filename=\"{p.Name}.bin\" label=\"{p.Name}\" num_partition_sectors=\"0\" physical_partition_number=\"0\" size_in_KB=\"0\" sparse=\"false\" start_byte_hex=\"{p.Offset}\" start_sector=\"0\"/>");
                }
                sb.AppendLine("</data>");
                IOFile.WriteAllText(xmlPath, sb.ToString());

                string patchPath = Path.Combine(backupDir, "patch0.xml");
                IOFile.WriteAllText(patchPath, "<?xml version=\"1.0\" ?>\n<patches>\n</patches>");
            }
            catch (Exception ex) { _log($"❌ rawprogram/patch0 xml ဖိုင်တွေ ရေးရာမှာ မအောင်မြင်ပါ: {ex.Message}", ErrorColor); }
        }

        public void GeneratePmkScatterFile(List<PartitionInfo> parts, string backupDir, string[] nvPartList)
        {
            try
            {
                string scatterFilePath = Path.Combine(backupDir, "PMK_Android_scatter.txt");
                var sb = new System.Text.StringBuilder();

                sb.AppendLine("############################################################################################################");
                sb.AppendLine("#  PMK Android Scatter Configuration");
                sb.AppendLine("############################################################################################################");
                sb.AppendLine("- general: MTK_PLATFORM_CFG");
                sb.AppendLine("  info:");
                sb.AppendLine("    - config_version: V1.1.2");
                sb.AppendLine("      platform: MT6765");
                sb.AppendLine("      project: PMK_Unlock_Tool");
                sb.AppendLine("      storage: EMMC");
                sb.AppendLine("      boot_channel: MSDC_0");
                sb.AppendLine("      block_size: 0x20000\n");

                int index = 0;
                foreach (var pName in nvPartList)
                {
                    var pInfo = parts.FirstOrDefault(p => p.Name.Equals(pName, StringComparison.OrdinalIgnoreCase));
                    string offset = pInfo != null && !string.IsNullOrEmpty(pInfo.Offset) ? pInfo.Offset : "0x0";
                    string length = pInfo != null && !string.IsNullOrEmpty(pInfo.Length) ? pInfo.Length : "0x4000000";

                    sb.AppendLine($"- partition_index: SYS{index++}");
                    sb.AppendLine($"  partition_name: {pName}");
                    sb.AppendLine($"  file_name: {pName}.img");
                    sb.AppendLine("  is_download: true");
                    sb.AppendLine("  type: NORMAL_ROM");
                    sb.AppendLine($"  linear_start_addr: {offset}");
                    sb.AppendLine($"  physical_start_addr: {offset}");
                    sb.AppendLine($"  partition_size: {length}");
                    sb.AppendLine("  region: EMMC_USER");
                    sb.AppendLine("  storage: HW_STORAGE_EMMC");
                    sb.AppendLine("  boundary_check: true");
                    sb.AppendLine("  is_reserved: false");
                    sb.AppendLine("  operation_type: UPDATE");
                    sb.AppendLine("  reserve: 0x00\n");
                }

                IOFile.WriteAllText(scatterFilePath, sb.ToString());
            }
            catch (Exception ex) { _log($"❌ Scatter file ရေးရာမှာ မအောင်မြင်ပါ: {ex.Message}", ErrorColor); }
        }

        // GPT output ကို parse ပြီး PartitionInfo list ပြန်ပေးတယ် (grid/UI ကို Form1 ဘက်က ပြင်ဆင်တယ်)
        public List<PartitionInfo> ParseGptOutput(string gptOutput)
        {
            var result = new List<PartitionInfo>();
            if (string.IsNullOrWhiteSpace(gptOutput)) return result;

            string[] lines = gptOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            string FormatHexSize(string hexStr)
            {
                try
                {
                    if (hexStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hexStr = hexStr.Substring(2);
                    ulong bytes = Convert.ToUInt64(hexStr, 16);
                    if (bytes >= 1024UL * 1024UL * 1024UL) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                    if (bytes >= 1024UL * 1024UL) return $"{bytes / (1024.0 * 1024.0):F2} MB";
                    if (bytes >= 1024UL) return $"{bytes / (1024.0 * 1024.0):F2} KB";
                    return $"{bytes} B";
                }
                catch (Exception ex) { _log($"⚠️ FormatHexSize fallback: {ex.Message}", WarningColor); return "N/A"; }
            }

            foreach (string rawLine in lines)
            {
                string line = Regex.Replace(rawLine, @"\x1B\[[^@-~]*[=@-~]", "").Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("=") || line.StartsWith("-") || line.Contains("DAXFlash") || line.Contains("[LIB]")) continue;

                Match mGPT = Regex.Match(line, @"^([a-zA-Z0-9_\-\.]+):\s*Offset\s+(0x[0-9A-Fa-f]+),\s*Length\s+(0x[0-9A-Fa-f]+)", RegexOptions.IgnoreCase);
                Match mBracket = Regex.Match(line, @"\[\s*\d+\s*\]\s+([a-zA-Z0-9_\-\.]+)\s*:\s*(0x[0-9A-Fa-f]+)\s*-\s*(0x[0-9A-Fa-f]+)(?:\s*\((.*?)\))?");

                string name = "";
                string offset = "0x0";
                string length = "0x0";
                string sizeDisplay = "Raw";

                if (mGPT.Success)
                {
                    name = mGPT.Groups[1].Value.Trim();
                    offset = mGPT.Groups[2].Value.Trim();
                    length = mGPT.Groups[3].Value.Trim();
                    sizeDisplay = FormatHexSize(length);
                }
                else if (mBracket.Success)
                {
                    name = mBracket.Groups[1].Value.Trim();
                    offset = mBracket.Groups[2].Value.Trim();
                    length = mBracket.Groups[3].Value.Trim();
                    sizeDisplay = FormatHexSize(length);
                }

                if (!string.IsNullOrEmpty(name) && !name.Equals("Total", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new PartitionInfo { Name = name, Offset = offset, Length = length, Type = sizeDisplay });
                }
            }
            return result;
        }

        // "G9" → "G10" စဉ်မှန်အောင် natural sort (model/brand list တွေ စီဖို့)
        public static int CompareNatural(string a, string b)
        {
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                char ca = a[ia], cb = b[ib];
                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    int sa = ia, sb = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                    string na = a.Substring(sa, ia - sa).TrimStart('0');
                    string nb = b.Substring(sb, ib - sb).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length - nb.Length;
                    int cmp = string.CompareOrdinal(na, nb);
                    if (cmp != 0) return cmp;
                }
                else
                {
                    int cmp = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));
                    if (cmp != 0) return cmp;
                    ia++; ib++;
                }
            }
            return (a.Length - ia) - (b.Length - ib);
        }
    }
}
