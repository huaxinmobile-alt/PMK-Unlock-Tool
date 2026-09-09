using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Loader management / device database / auto-detect (HWID+PK_HASH → loader) service
    // — Form1 (God Class) ကနေ ခွဲထုတ်ထားတယ်။ Logging ကို LogHandler (FirmwareService နဲ့ အတူတူ) ကနေ ထိုးသွင်းတယ်။
    public class LoaderService
    {
        // Form1 ရဲ့ color fields တွေနဲ့ တူညီတဲ့ အရောင်တွေ
        private static readonly Color WarningColor = Color.FromArgb(255, 213, 79);

        private readonly LogHandler _log;

        public LoaderService(LogHandler logHandler)
        {
            _log = logHandler ?? delegate { };
        }

        // ===== Chipset → Brand → Model database (built-in) =====
        public Dictionary<string, Dictionary<string, List<string>>> ChipsetDatabase { get; } = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            { "MediaTek", new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) {
                { "# Auto Detect", new List<string> { "# Auto Detect" } },
                { "Xiaomi / Redmi (MTK)", new List<string> {
                    "# Auto Detect", "Redmi 6 (cereus)", "Redmi 6A (cactus)", "Redmi 9 / 9 Prime (lancelot)",
                    "Redmi 9A / 9AT / 9i (dandelion)", "Redmi 9C / 9C NFC (angelica)", "Redmi 10A (smoke)",
                    "Redmi 10C (fog)", "Redmi 10X 4G (merlin)", "Redmi Note 8 Pro (begonia)",
                    "Redmi Note 9 (merlin)", "Redmi Note 10S (rosemary)", "Redmi Note 11S 4G (fleur)",
                    "Redmi Note 11 Pro 4G (viva)", "Redmi Note 12S (sea)", "Redmi Note 12 Pro+ (ruby)",
                    "Redmi Note 13 Pro+ 5G (zircon)", "POCO M4 Pro 4G (fleur)", "POCO C31 (angelicain)",
                    "POCO C50 (water)", "POCO C51 (water)", "POCO C55 (earth)", "POCO X6 Neo (gold)"
                }},
                { "Samsung (MTK)", new List<string> {
                    "# Auto Detect", "Galaxy A01 Core (SM-A013F/G)", "Galaxy A02 (SM-A022F/G/M)",
                    "Galaxy A03s (SM-A037F/G/M)", "Galaxy A04 (SM-A045F/M)", "Galaxy A05 (SM-A055F/M)",
                    "Galaxy A10s (SM-A107F/M)", "Galaxy A12 (SM-A125F/M)", "Galaxy A13 5G (SM-A136B/U)",
                    "Galaxy A14 5G (SM-A146P/B)", "Galaxy A22 4G (SM-A225F)", "Galaxy A22 5G (SM-A226B)",
                    "Galaxy A24 4G (SM-A245F)", "Galaxy A31 (SM-A315F/G)", "Galaxy A32 4G (SM-A325F)",
                    "Galaxy A32 5G (SM-A326B)", "Galaxy A34 5G (SM-A346B)", "Galaxy M01s (SM-M017F)",
                    "Galaxy M02 (SM-M022F)", "Galaxy M22 (SM-M225F)", "Galaxy M32 4G (SM-M325F)", "Galaxy M32 5G (SM-M326B)"
                }},
                { "Oppo (MTK)", new List<string> {
                    "# Auto Detect", "Oppo A1k (CPH1923)", "Oppo A5s (CPH1909)", "Oppo A11k (CPH2071)",
                    "Oppo A12 / A12s (CPH2083/CPH2077)", "Oppo A15 / A15s (CPH2185/CPH2179)",
                    "Oppo A16 / A16k / A16e (CPH2269/CPH2349/CPH2421)", "Oppo A17 / A17k (CPH2477/CPH2471)",
                    "Oppo A31 (CPH2015)", "Oppo A35 (PEAM00)", "Oppo A54 (CPH2239)", "Oppo A57 2022 (CPH2387)",
                    "Oppo A77s / A78 5G (CPH2473/CPH2489)", "Oppo F9 / F9 Pro (CPH1823/CPH1825)",
                    "Oppo F11 / F11 Pro (CPH1911/CPH1969)", "Oppo F15 / F17 Pro (CPH2001/CPH2119)",
                    "Oppo Reno 2F (CPH1989)", "Oppo Reno 3 / 3 Pro (CPH2043/CPH2035)", "Oppo Reno 6 5G (CPH2251)",
                    "Oppo Reno 7 5G / Reno 8 5G", "Oppo Reno 8T 4G/5G"
                }},
                { "Vivo / iQOO (MTK)", new List<string> {
                    "# Auto Detect", "Vivo Y1s (1929/2015)", "Vivo Y3s (2043)", "Vivo Y12 / Y12s (1904/2026)",
                    "Vivo Y15 / Y15s / Y15a (1901/2120/2134)", "Vivo Y16 (V2204)", "Vivo Y17 (1902)",
                    "Vivo Y19 (1915)", "Vivo Y20G / Y20T (2037/2107)", "Vivo Y21 2021 (V2111)",
                    "Vivo Y22 2022 (V2207)", "Vivo Y30 / Y30i (1938/2019)", "Vivo Y81 / Y83 / Y91 / Y93 / Y95",
                    "Vivo S1 (1907)", "Vivo V15 / V15 Pro", "Vivo V21 5G / V21e (V2050/V2055)",
                    "Vivo V23e 4G/5G (V2116/V2126)", "Vivo V25 / V27 5G", "iQOO Z6 44W / Z7 5G"
                }},
                { "Realme (MTK)", new List<string> {
                    "# Auto Detect", "Realme 1 (CPH1859)", "Realme 3 / 3i (RMX1821/RMX1827)", "Realme C2 (RMX1941)",
                    "Realme C11 2020 (RMX2185)", "Realme C12 (RMX2189)", "Realme C15 MTK (RMX2180)",
                    "Realme C20 (RMX3061)", "Realme C21 (RMX3201)", "Realme C25 / C25s (RMX3193/RMX3195)",
                    "Realme 6 / 6i (RMX2001/RMX2040)", "Realme 7 4G / 7 5G (RMX2151/RMX2111)",
                    "Realme 8 4G / 8 5G (RMX3085/RMX3241)", "Realme 9 5G (RMX3388)", "Realme 10 4G (RMX3630)",
                    "Realme Narzo 10 / 10A", "Realme Narzo 20 / 30A / 30 4G", "Realme Narzo 50 / 50A / 50i"
                }},
                { "Infinix / Tecno (MTK)", new List<string> {
                    "# Auto Detect", "Infinix Hot 7 / 8 / 9 / 10 / 11 / 12 / 20 / 30",
                    "Infinix Smart 3 / 4 / 5 / 6 / 7 / 8", "Infinix Note 7 / 8 / 10 / 11 / 12 / 30",
                    "Infinix Zero 8 / X / 20 / 30", "Tecno Spark 4 / 5 / 6 / 7 / 8 / 9 / 10 / 20",
                    "Tecno Spark Go 2020/2021/2022/2023/2024", "Tecno Pova / Pova 2 / Pova 3 / Pova 4 / Pova 5",
                    "Tecno Camon 15 / 16 / 17 / 18 / 19 / 20", "Tecno Pop 2 / 3 / 4 / 5"
                }},
                { "Huawei / Honor (MTK)", new List<string> {
                    "# Auto Detect", "Huawei Y5 2019 (AMN-LX9)", "Huawei Y6p (MED-LX9)", "Huawei Y5p (DRA-LX9)",
                    "Honor 8A (JAT-LX1)", "Honor 9A (MOA-LX9)", "Honor Play 3e", "Honor X5 (VNL-LX2)", "Honor X6 (VNE-LX1)"
                }}
            }},
            { "Qualcomm", new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) {
                { "# Auto Detect", new List<string> { "# Auto Detect" } },
                { "Xiaomi / Redmi (Qualcomm)", new List<string> {
                    "# Auto Detect", "Redmi 8 / 8A (olive)", "Redmi 3 (ido)", "Redmi 3S / 3X (land)", "Redmi 4 (prada)", "Redmi 4 Prime (markw)",
                    "Redmi 4A (rolex)", "Redmi 4X (santoni)", "Redmi 5 (rosy)", "Redmi 5A (riva)", "Redmi 5 Plus (vince)",
                    "Redmi Note 3 Pro (kenzo)", "Redmi Note 4 QC (mido)", "Redmi Note 5 Pro (whyred)",
                    "Redmi Note 5A Prime (ugg)", "Redmi Note 5A (ugglite)", "Redmi Note 6 Pro (tulip)",
                    "Redmi Note 7 (lavender)", "Redmi Note 8 (ginkgo)", "Redmi Note 9 Pro", "Redmi Note 9s",
                    "Redmi Note 10 Lite", "Redmi Note 10 (sunny_mojito)", "Redmi S2 (YSL)",
                    "Mi 4C (libra)", "Mi 4S (aqua)", "Mi 5X (tiffany)", "Mi 6X (wayne)",
                    "Mi Max 2 (oxygen)", "Mi Max 3 (nitrogen)", "Mi Max (hydrogen/helium)", "Mi Note 3 (jason)", "Mi Pad 4 (clover)"
                }},
                { "Vivo / iQOO (Qualcomm)", new List<string> {
                    "# Auto Detect", "Vivo Y11 (1906)", "Vivo Y20 / Y20i / Y20s (PD2034F)", "Vivo Y31s",
                    "Vivo Y50 (PD1965F)", "Vivo Y53 (PD1628F)", "Vivo Y71 (PD1731F)", "Vivo Y95 (8937)",
                    "Vivo V5 Plus (PD1624F)", "Vivo V9 (PD1730F)", "Vivo V9 Youth", "Vivo V11 Pro (SDM660)",
                    "Vivo V15 Pro (SDM675)", "Vivo V17 Pro (SDM675)", "Vivo V19 (PD1969F)",
                    "Vivo X20 Plus", "Vivo X21S", "Vivo X23", "Vivo X27 Pro (SDM710)", "Vivo X50 / X50 Pro (PD2001F/PD2005)",
                    "Vivo Z1 Pro (PD1911F)", "Vivo Z1x (PD1921F)", "Vivo Z3", "Vivo Z5 / Z5x / Z5 Pro", "Vivo Z6 5G (PD1963)",
                    "Vivo NEX / NEX S (PD1805F)", "Vivo S1 Pro (SDM665)", "Vivo U3x", "Vivo iQOO / iQOO 3 5G / Neo 3 5G"
                }},
                { "Oppo (Qualcomm)", new List<string> {
                    "# Auto Detect", "Oppo A33 (8936)", "Oppo A51 (8936)", "Oppo A53 (8936)", "Oppo A57 (8940)",
                    "Oppo A71 (Qlm)", "Oppo A77", "Oppo F3 Plus (CPH1611_8976)", "Oppo R7s / R7 Plus",
                    "Oppo R11 / R11s (SDM660)", "Oppo Y55L / Y66 / Y71 / Y75s / Y79 / Y85"
                }},
                { "Samsung (Qualcomm)", new List<string> {
                    "# Auto Detect", "Galaxy A01 (SM-A015F/G/M)", "Galaxy A02s (SM-A025F/G/M)", "Galaxy A05s (SM-A057F/M)",
                    "Galaxy A11 (SM-A115F/M)", "Galaxy A20s (SM-A207F/M)", "Galaxy A23 4G (SM-A235F/M)",
                    "Galaxy A52 4G/5G", "Galaxy A70 / A71 / A72 / A73 5G"
                }}
            }},
            { "Spreadtrum", new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) {
                { "# Auto Detect", new List<string> { "# Auto Detect" } },
                { "Realme (Unisoc / SPD)", new List<string> {
                    "# Auto Detect", "Realme C11 2021 (RMX3231)", "Realme C21Y (RMX3261)",
                    "Realme C25Y (RMX3265)", "Realme C30 / C30s", "Realme C33", "Realme C35", "Realme C51 / C53"
                }},
                { "Samsung (Unisoc / SPD)", new List<string> {
                    "# Auto Detect", "Galaxy A03 (SM-A035F)", "Galaxy A03 Core (SM-A032F)", "Galaxy Tab A7 Lite"
                }},
                { "Infinix / Tecno (SPD)", new List<string> {
                    "# Auto Detect", "Infinix Smart 6 / 7 / 8", "Tecno Pop 5 / 6 / 7", "Tecno Spark 8C / 9 / 10C"
                }}
            }},
            { "Samsung", new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) {
                { "# Auto Detect", new List<string> { "# Auto Detect" } },
                { "Galaxy A Series", new List<string> { "# Auto Detect", "A01", "A02", "A03", "A03s", "A04", "A05", "A10s", "A12", "A13", "A14", "A20s", "A22", "A23", "A24", "A31", "A32", "A34", "A51", "A52", "A53", "A54", "A70", "A71", "A72", "A73" } },
                { "Galaxy M / F Series", new List<string> { "# Auto Detect", "M01s", "M02", "M11", "M12", "M13", "M14", "M21", "M22", "M31", "M32", "M33", "M51", "M52", "M53", "F12", "F13", "F22", "F23" } },
                { "Galaxy S / Note Series", new List<string> { "# Auto Detect", "S8 / S8+", "S9 / S9+", "S10 / S10+", "S20 / S20 FE / S20 Ultra", "S21 / S21 FE / S21 Ultra", "S22 / S22 Ultra", "S23 / S23 Ultra", "S24 / S24 Ultra", "Note 8 / 9 / 10 / 10+", "Note 20 / Note 20 Ultra" } }
            }}
        };

        // ================= Hybrid Auto Sig Finder =================
        public string FindXiaomiSigFile()
        {
            string[] sigPaths = {
                AppConfig.Combine(AppConfig.LoadersDir, "Xiaomi", "SIG's", "SIG #1.bin"),
                AppConfig.Combine(AppConfig.LoadersDir, "Xiaomi", "SIG's", "SIG #1", "sig.bin"),
                AppConfig.Combine(AppConfig.LoadersDir, "Xiaomi", "SIG", "SIG #1.bin"),
                AppConfig.SigBinRoot
            };

            foreach (var path in sigPaths)
            {
                if (IOFile.Exists(path)) return path;
            }

            try
            {
                string sigDir = AppConfig.Combine(AppConfig.LoadersDir, "Xiaomi", "SIG's");
                if (Directory.Exists(sigDir))
                {
                    var binFiles = Directory.GetFiles(sigDir, "*.bin", SearchOption.AllDirectories);
                    if (binFiles.Length > 0) return binFiles[0];
                }
            }
            catch (Exception ex) { _log($"⚠️ FindXiaomiSigFile warning: {ex.Message}", WarningColor); }

            return "";
        }

        // Qualcomm loader ကို brand+model နဲ့ ရှာဖွေခြင်း (Loaders folder tree)
        public string? FindQualcommLoader(string brand, string model)
        {
            try
            {
                string[] searchPaths = {
                    AppConfig.LoadersDir,
                    AppConfig.QualcommFirehosesDir,
                    AppConfig.EdlLoadersDir,
                    @"C:\Users\PMK\Downloads\Qualcomm-firehoses-main\Qualcomm-firehoses-main"
                };

                Match codeMatch = Regex.Match(model, @"\((.*?)\)");
                string codename = codeMatch.Success ? codeMatch.Groups[1].Value.Trim().ToUpperInvariant() : "";
                string cleanModel = Regex.Replace(model, @"\(.*?\)", "").Trim().ToUpperInvariant().Replace(" / ", " ").Replace("/", " ");

                string cleanBrand = brand.ToUpperInvariant();
                if (cleanBrand.Contains("XIAOMI") || cleanBrand.Contains("REDMI")) cleanBrand = "XIAOMI";
                else if (cleanBrand.Contains("VIVO")) cleanBrand = "VIVO";
                else if (cleanBrand.Contains("OPPO")) cleanBrand = "OPPO";
                else if (cleanBrand.Contains("REALME")) cleanBrand = "REALME";
                else if (cleanBrand.Contains("SAMSUNG")) cleanBrand = "SAMSUNG";

                foreach (var baseDir in searchPaths)
                {
                    if (!Directory.Exists(baseDir)) continue;

                    string brandDir = Path.Combine(baseDir, cleanBrand);
                    string targetDir = Directory.Exists(brandDir) ? brandDir : baseDir;

                    var allLoaderFiles = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories)
                                                  .Where(f => f.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
                                                              f.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase) ||
                                                              f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                                                  .ToList();

                    var priorityFiles = allLoaderFiles.OrderBy(f => {
                        if (f.Contains("No Auth", StringComparison.OrdinalIgnoreCase)) return 0;
                        if (f.Contains("Auth Skip", StringComparison.OrdinalIgnoreCase)) return 1;
                        if (f.Contains("SIG", StringComparison.OrdinalIgnoreCase)) return 2;
                        return 3;
                    }).ToList();

                    foreach (var file in priorityFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();

                        if (!string.IsNullOrEmpty(cleanModel) && !cleanModel.StartsWith("#"))
                        {
                            string[] modelKeywords = cleanModel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (modelKeywords.Length >= 2 && modelKeywords.All(k => fileName.Contains(k)))
                                return file;
                            else if (fileName.Contains(cleanModel))
                                return file;
                        }

                        if (!string.IsNullOrEmpty(codename) && fileName.Contains(codename))
                            return file;
                    }
                }
            }
            catch (Exception ex) { _log($"⚠️ FindQualcommLoader error: {ex.Message}", WarningColor); }

            return null;
        }

        // Brand display name → Loaders folder name ကို ရှာဖွေခြင်း (Qualcomm)
        public string ResolveQualcommLoaderFolder(string brand)
        {
            if (string.IsNullOrEmpty(brand)) return "";
            string b = brand.ToUpperInvariant();
            if (b.Contains("XIAOMI") || b.Contains("REDMI")) return "Xiaomi";
            if (b.Contains("VIVO")) return "Vivo";
            if (b.Contains("OPPO")) return "Oppo";
            if (b.Contains("REALME")) return "Realme";
            if (b.Contains("SAMSUNG")) return "Samsung";
            if (b.Contains("IQOO")) return "Iqoo";

            try
            {
                string loadersRoot = AppConfig.LoadersDir;
                if (Directory.Exists(loadersRoot))
                {
                    foreach (var dir in Directory.GetDirectories(loadersRoot))
                    {
                        string folderName = Path.GetFileName(dir);
                        if (folderName.Equals(brand, StringComparison.OrdinalIgnoreCase)) return folderName;
                    }
                }
            }
            catch (Exception ex) { _log($"⚠️ ResolveQualcommLoaderFolder error: {ex.Message}", WarningColor); }
            return "";
        }

        // Loaders/<folder> ထဲက loader ဖိုင်နာမည်တွေကနေ model list ဆောက်ခြင်း (Qualcomm tab အတွက် အပြည့်အစုံ)
        public List<string> GetQualcommFolderModels(string folderName)
        {
            var models = new List<string>();
            if (string.IsNullOrEmpty(folderName)) return models;
            try
            {
                string baseDir = AppConfig.Combine(AppConfig.LoadersDir, folderName);
                if (!Directory.Exists(baseDir)) return models;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(baseDir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".elf" && ext != ".mbn" && ext != ".bin" && ext != ".melf") continue;

                    string name = Path.GetFileNameWithoutExtension(f).Trim();
                    if (name.Length == 0) continue;

                    // generic programmer / meta ဖိုင်တွေကို model အဖြစ် မထည့်ဘူး
                    if (Regex.IsMatch(name, @"^(prog_|sahara|fh_loader|patch|rawprogram)", RegexOptions.IgnoreCase)) continue;
                    if (Regex.IsMatch(name, @"^\d+$")) continue; // chipset number သက်သက် (610, 430...) က model မဟုတ်ဘူး

                    if (seen.Add(name)) models.Add(name);
                }
                models.Sort(FirmwareService.CompareNatural); // G9 → G10 စဉ်မှန်အောင် natural order
            }
            catch (Exception ex) { _log($"⚠️ GetQualcommFolderModels error: {ex.Message}", WarningColor); }
            return models;
        }

        // Loaders folder ထဲက brand အမည်စာရင်း (natural order, duplicate မရှိ)
        public List<string> EnumerateLoaderBrands()
        {
            var brands = new List<string>();
            try
            {
                string loadersRoot = AppConfig.LoadersDir;
                if (!Directory.Exists(loadersRoot)) return brands;

                foreach (var dir in Directory.GetDirectories(loadersRoot))
                {
                    string folderName = Path.GetFileName(dir);
                    bool hasLoaders = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                        .Any(f => f.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
                                  f.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase) ||
                                  f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
                    if (hasLoaders) brands.Add(folderName);
                }
                brands.Sort(FirmwareService.CompareNatural);
            }
            catch (Exception ex) { _log($"⚠️ EnumerateLoaderBrands error: {ex.Message}", WarningColor); }
            return brands;
        }

        // ============ Auto-Detect loader learning (HWID+PK_HASH → loader) ============
        // အောင်မြင်တဲ့ loader upload ရဲ့ hwid+pkhash → loader path ကို မှတ်ထားတယ် (နောက် Auto Detect အတွက်)
        public void PersistAutoLoaderMap(string hwid, string pkhash, string currentLoaderPath)
        {
            try
            {
                if (hwid.Length == 0 || pkhash.Length == 0) return;
                string loader = currentLoaderPath;
                if (loader.Length == 0) return;
                if (!Directory.Exists(AppConfig.DeviceDatabaseDir)) Directory.CreateDirectory(AppConfig.DeviceDatabaseDir);

                string key = hwid + "_" + pkhash;
                // PC ပြောင်းရင် (clone နေရာပြောင်းရင်) အလုပ်ဖြစ်အောင် — app folder အောက်က loader ဆိုရင် relative path နဲ့ မှတ်တယ်
                string storePath = loader.StartsWith(AppConfig.BaseDir, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetRelativePath(AppConfig.BaseDir, loader)
                    : loader;
                var lines = IOFile.Exists(AppConfig.AutoLoaderMapFile) ? IOFile.ReadAllLines(AppConfig.AutoLoaderMapFile).ToList() : new List<string>();
                lines.RemoveAll(l => l.StartsWith(key + "|"));
                lines.Add($"{key}|{storePath}");
                IOFile.WriteAllLines(AppConfig.AutoLoaderMapFile, lines);

                // python ရဲ့ auto-loader DB အတွက်ပါ — loader ကို <hwid>_<pkhash>_FHPRG.bin နာမည်နဲ့ ထည့်ပေး
                // (ဒါဆို နောက်တစ်ခါ loader မရွေးဘဲ python က session တစ်ခုတည်းနဲ့ auto အကုန်လုပ်နိုင်တယ်)
                try
                {
                    string pyLoaders = AppConfig.EdlClientLoadersDir;
                    if (!Directory.Exists(pyLoaders)) Directory.CreateDirectory(pyLoaders);
                    string baseName = hwid.Replace("0x", "").ToLowerInvariant() + "_" +
                                      pkhash.Replace("0x", "").ToLowerInvariant();
                    foreach (var suffix in new[] { "FHPRG", "ENPRG" })
                    {
                        IOFile.Copy(loader, Path.Combine(pyLoaders, $"{baseName}_{suffix}.bin"), true);
                    }
                }
                catch (Exception ex) { _log($"⚠️ python auto-loader DB မိတ္တူကူးရာမှာ မအောင်မြင်ပါ: {ex.Message}", WarningColor); }
            }
            catch (Exception ex) { _log($"⚠️ loader map မှတ်တမ်းသိမ်းရာမှာ မအောင်မြင်ပါ: {ex.Message}", WarningColor); }
        }

        // ဒီဖုန်း (hwid+pkhash) အတွက် မှတ်ထားတဲ့ loader ရှိရင် ပြန်ယူတယ်
        public string TryResolveAutoLoader(string hwid, string pkhash)
        {
            try
            {
                if (hwid.Length == 0 || pkhash.Length == 0) return "";
                if (!IOFile.Exists(AppConfig.AutoLoaderMapFile)) return "";
                string key = hwid + "_" + pkhash;
                foreach (var l in IOFile.ReadAllLines(AppConfig.AutoLoaderMapFile))
                {
                    if (l.StartsWith(key + "|"))
                    {
                        string stored = l.Substring(l.IndexOf('|') + 1).Trim();
                        // absolute (အဟောင်း format) ဖြစ်ရင် တည့်တည့်စမ်း၊ မဟုတ်ရင် app folder (bin) နဲ့ ယှဉ်တဲ့ relative path အနေနဲ့ စမ်းတယ်
                        string loader = Path.IsPathRooted(stored) ? stored : AppConfig.Combine(AppConfig.BaseDir, stored);
                        if (IOFile.Exists(loader)) return loader;
                    }
                }
            }
            catch (Exception ex) { _log($"⚠️ TryResolveAutoLoader error: {ex.Message}", WarningColor); }
            return "";
        }
    }
}