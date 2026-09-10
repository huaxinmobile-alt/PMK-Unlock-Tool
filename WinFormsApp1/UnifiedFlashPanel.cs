using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WinFormsApp1
{
    /// <summary>
    /// Firmware folder scan ရလဒ် — chipset အလိုက် ရှာတွေ့တဲ့ ဖိုင်လမ်းကြောင်းများ (ရှာမတွေ့ရင် "")
    /// </summary>
    internal sealed class FlashFileSet
    {
        public string Folder = "";
        public string Programmer = "";     // Qualcomm: firehose loader (.elf/.mbn)
        public string RawProgram = "";     // Qualcomm: rawprogram*.xml
        public string Patch = "";          // Qualcomm: patch*.xml
        public string Scatter = "";        // MediaTek: scatter*.txt
        public string DownloadAgent = "";  // MediaTek: DA*.bin
        public string Auth = "";           // MediaTek: *.auth
        public string Pac = "";            // Spreadtrum: *.pac
        public string SamsungBl = "";
        public string SamsungAp = "";
        public string SamsungCp = "";
        public string SamsungCsc = "";
        public string SamsungUserData = "";
    }

    /// <summary>
    /// Flash options — Settings ကဲ့သို့ ဖိုင်တစ်ဖိုင်မှာ သိမ်းတယ် (tool ပိတ်ဖွင့်လည်း မပျောက်)
    /// </summary>
    internal sealed class FlashOptions
    {
        public bool EraseBefore;
        public bool VerifyAfter;
        public bool AutoReboot = true;
        public bool SkipUserData;

        public static string FilePath => Path.Combine(AppConfig.BaseDir, "flash_options.txt");

        public static FlashOptions Load()
        {
            var o = new FlashOptions();
            try
            {
                if (!File.Exists(FilePath)) return o;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    bool v = line.Substring(eq + 1).Trim() == "1";
                    if (k == "erase") o.EraseBefore = v;
                    else if (k == "verify") o.VerifyAfter = v;
                    else if (k == "reboot") o.AutoReboot = v;
                    else if (k == "skipuserdata") o.SkipUserData = v;
                }
            }
            catch { /* option ဖတ်မရရင် default အတိုင်း */ }
            return o;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "erase=" + (EraseBefore ? "1" : "0"),
                    "verify=" + (VerifyAfter ? "1" : "0"),
                    "reboot=" + (AutoReboot ? "1" : "0"),
                    "skipuserdata=" + (SkipUserData ? "1" : "0"),
                });
            }
            catch { /* မရေးနိုင်ရင် ဆက်သွား — flash အလုပ်ကို မနှောင့်ရ */ }
        }
    }

    /// <summary>
    /// Firmware folder ထဲက ဖိုင်တွေကို chipset အလိုက် ရှာပေးတယ် (top-level ကို အရင်ဦးစားပေး၊
    /// မတွေ့ရင် အောက်ဆုံး folder ထိ ရှာ)။ ဖိုင်နာမည် keyword အလိုက် ဦးစားပေး ရွေးတယ်
    /// (ဥပမာ rawprogram0.xml ကို rawprogram1.xml ထက် ဦးစား)။
    /// </summary>
    internal static class FirmwareFolderScanner
    {
        public static FlashFileSet Scan(string folder, string category)
        {
            var set = new FlashFileSet { Folder = folder ?? "" };
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return set;

            var files = SafeList(folder);

            if (category == "Qualcomm")
            {
                set.Programmer =
                    Pick(files, f => f.EndsWith(".elf"), "firehose", "ddr", "prog", "loader") ??
                    Pick(files, f => f.EndsWith(".mbn"), "firehose", "ddr", "prog") ?? "";
                set.RawProgram = Pick(files, f => f.StartsWith("rawprogram") && f.EndsWith(".xml"), "rawprogram0") ?? "";
                set.Patch = Pick(files, f => f.StartsWith("patch") && f.EndsWith(".xml"), "patch0") ?? "";
            }
            else if (category == "MediaTek")
            {
                set.Scatter =
                    Pick(files, f => f.StartsWith("scatter") && f.EndsWith(".txt"), "scatter.txt") ??
                    Pick(files, f => f.EndsWith(".txt"), "scatter") ?? "";
                set.DownloadAgent =
                    Pick(files, f => f.EndsWith(".bin") && f.Contains("da"), "da.bin", "da_") ??
                    Pick(files, f => f.EndsWith(".bin") && f.Contains("preloader"), "preloader") ?? "";
                set.Auth = Pick(files, f => f.EndsWith(".auth"), "auth") ?? "";
            }
            else if (category == "Spreadtrum")
            {
                set.Pac = Pick(files, f => f.EndsWith(".pac"), "") ?? "";
            }
            else if (category == "Samsung")
            {
                set.SamsungBl = Pick(files, f => IsTar(f) && HasPrefix(f, "bl_"), "bl_") ?? "";
                set.SamsungAp = Pick(files, f => IsTar(f) && HasPrefix(f, "ap_"), "ap_") ?? "";
                set.SamsungCp = Pick(files, f => IsTar(f) && HasPrefix(f, "cp_"), "cp_") ?? "";
                set.SamsungCsc = Pick(files, f => IsTar(f) && (HasPrefix(f, "csc_") || HasPrefix(f, "home_csc_")), "home_csc_", "csc_") ?? "";
                set.SamsungUserData = Pick(files, f => IsTar(f) && HasPrefix(f, "userdata"), "userdata") ?? "";
            }

            return set;
        }

        private static bool IsTar(string name) => name.EndsWith(".tar") || name.EndsWith(".tar.md5");
        private static bool HasPrefix(string name, string prefix) => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        // folder ထဲက ဖိုင် ၃၀၀၀ အထိ — အနက်ဆုံး folder တွေကို နောက်ဆုံးမှ စစ်တယ် (top-level ဦးစား)
        private static List<string> SafeList(string root)
        {
            var list = new List<string>();
            try
            {
                foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    list.Add(f);
                    if (list.Count >= 3000) break;
                }
            }
            catch { }
            list.Sort((a, b) => Depth(root, a) != Depth(root, b)
                ? Depth(root, a).CompareTo(Depth(root, b))
                : string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        private static int Depth(string root, string path)
        {
            int d = 0, i = root.Length;
            for (; i < path.Length; i++) if (path[i] == Path.DirectorySeparatorChar || path[i] == Path.AltDirectorySeparatorChar) d++;
            return d;
        }

        // predicate ကိုက်တဲ့ ပထမဆုံးဖိုင် — preferred keyword ပါတဲ့ ဖိုင်ကို ဦးစားပေး
        private static string? Pick(List<string> files, Func<string, bool> match, params string[] preferred)
        {
            string? first = null;
            foreach (string path in files)
            {
                string name = Path.GetFileName(path).ToLowerInvariant();
                if (!match(name)) continue;
                first ??= path;
                foreach (string key in preferred)
                {
                    if (key.Length > 0 && name.Contains(key)) return path;
                }
                if (preferred.Length == 0) return path;
            }
            return first;
        }
    }

    /// <summary>
    /// Chipset tab (Qualcomm/MediaTek/Spreadtrum/Samsung) တွေအတွက် တစ်ခုတည်းသော Firmware Flashing panel.
    /// Folder တစ်ခု ရွေးလိုက်ရင် firmware ဖိုင်တွေကို auto-detect လုပ်ပြပြီး၊
    /// partition count + flash options + START FLASHING ခလုတ်ကို ဒီကနေ ထိန်းတယ်။
    /// (တကယ့် flash အလုပ်ကို Form1 က လုပ်တယ် — ဒီ panel က UI သက်သက်)
    /// </summary>
    public class UnifiedFlashPanel : UserControl
    {
        public event EventHandler? FlashRequested;
        public event EventHandler<string>? FolderSelected;
        public event EventHandler? OptionsChanged;

        internal Label lblTitle = null!;
        internal Button btnSelectFolder = null!;
        internal TextBox txtFolder = null!;
        internal Button btnClearFolder = null!;
        internal Button btnRescan = null!;
        private readonly ToolTip folderTip = new ToolTip();
        internal CheckBox chkErase = null!;
        internal CheckBox chkVerify = null!;
        internal CheckBox chkAutoReboot = null!;
        internal CheckBox chkSkipUserData = null!;
        internal Button btnStartFlash = null!;
        internal ProgressBar progress = null!;
        internal Label lblProgress = null!;

        private readonly string[] roles = new string[5];
        private FlashFileSet files = new FlashFileSet();
        private int fileRowCount;
        private string category = "Qualcomm";
        private bool busy;

        private static readonly Color BgCard = Color.FromArgb(24, 32, 43);
        private static readonly Color FgText = Color.FromArgb(215, 228, 242);
        private static readonly Color OkGreen = Color.FromArgb(0, 230, 118);
        private static readonly Color WarnOrange = Color.FromArgb(255, 152, 0);

        public UnifiedFlashPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = BgCard;
            Padding = new Padding(0);
            AllowDrop = true;

            lblTitle = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 181, 246),
                BackColor = Color.Transparent,
                Text = "⚡ FIRMWARE FLASHING"
            };
            Controls.Add(lblTitle);

            btnSelectFolder = MakeButton("📁 Select Firmware Folder", Color.FromArgb(33, 150, 243), 200, 28);
            btnSelectFolder.Click += (s, e) => BrowseFolder();
            Controls.Add(btnSelectFolder);

            txtFolder = new TextBox
            {
                ReadOnly = false, // path ကို တိုက်ရိုက် paste/ရိုက်လို့ရ — Enter နှိပ်ရင် load
                BackColor = Color.FromArgb(16, 22, 30),
                ForeColor = FgText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                AllowDrop = true,
                // Hint စာသား — label မလိုဘဲ နေရာမကုန်ဘဲ ပြတယ်
                PlaceholderText = "📁  Select a firmware folder to auto-detect files  (drag & drop / paste path + Enter)"
            };
            txtFolder.DragEnter += OnDragEnterOrOver;
            txtFolder.DragDrop += OnDragDropAny;
            txtFolder.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                RaiseFolder(txtFolder.Text);
            };
            Controls.Add(txtFolder);

            btnClearFolder = MakeButton("✖", Color.FromArgb(70, 80, 95), 30, 28);
            btnClearFolder.Click += (s, e) => ClearFolder();
            Controls.Add(btnClearFolder);

            // Folder ထဲက ဖိုင်တွေကို နေရာမကုန်ဘဲ ပြဖို့ — folder button ရဲ့ tooltip နဲ့ ခလုတ်စာသားပဲ သုံးတယ်
            folderTip.AutoPopDelay = 30000;
            folderTip.InitialDelay = 250;
            folderTip.ReshowDelay = 100;
            folderTip.ShowAlways = true;

            btnRescan = MakeButton("🔍", Color.FromArgb(60, 90, 120), 34, 28);
            btnRescan.Click += (s, e) => RaiseFolder(txtFolder.Text); // folder ထဲ ဖိုင်အသစ်ထည့်ပြီး ပြန်ရှာချင်ရင်
            btnRescan.Visible = false;
            Controls.Add(btnRescan);

            // Select All / Deselect All က partition grid ရဲ့ အပေါ်မှာ သီးသန့် (Form1 ရဲ့ partitionToolbar)
            chkErase = MakeOption("Erase before flash", 150);
            chkVerify = MakeOption("Verify after flash", 155);
            chkAutoReboot = MakeOption("Auto reboot after flash", 175);
            chkSkipUserData = MakeOption("Skip userdata (fast flash)", 195);
            chkAutoReboot.Checked = true;

            btnStartFlash = MakeButton("⚡ START FLASHING", Color.FromArgb(230, 60, 60), 300, 36);
            btnStartFlash.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnStartFlash.Click += (s, e) => { if (!busy) FlashRequested?.Invoke(this, EventArgs.Empty); };
            Controls.Add(btnStartFlash);

            progress = new ProgressBar { Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100, Value = 0 };
            Controls.Add(progress);

            lblProgress = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = OkGreen,
                BackColor = Color.Transparent,
                Text = "0%",
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(lblProgress);

            DragEnter += OnDragEnterOrOver;
            DragDrop += OnDragDropAny;

            SetCategory("Qualcomm");
        }

        private CheckBox MakeOption(string text, int width)
        {
            var chk = new CheckBox
            {
                Text = text,
                AutoSize = false,
                Width = width,
                Height = 22,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = FgText,
                BackColor = Color.Transparent
            };
            chk.CheckedChanged += (s, e) => OptionsChanged?.Invoke(this, EventArgs.Empty);
            Controls.Add(chk);
            return chk;
        }

        private static Button MakeButton(string text, Color back, int w, int h)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                UseVisualStyleBackColor = false,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        // ===== Category အလိုက် role စာရင်း + title =====
        internal void SetCategory(string cat)
        {
            category = cat ?? "";
            string[] newRoles;
            string title;
            Color titleColor;

            switch (category)
            {
                case "MediaTek":
                    newRoles = new[] { "Scatter File", "Download Agent (DA)", "Auth File" };
                    title = "⚡ FIRMWARE FLASHING — MEDIATEK SP FLASH";
                    titleColor = Color.FromArgb(129, 199, 132);
                    break;
                case "Spreadtrum":
                    newRoles = new[] { "PAC Firmware" };
                    title = "⚡ FIRMWARE FLASHING — SPREADTRUM / UNISOC";
                    titleColor = Color.FromArgb(255, 179, 71);
                    break;
                case "Samsung":
                    newRoles = new[] { "BL (Bootloader)", "AP (System/PDA)", "CP (Modem)", "CSC / HOME_CSC", "USERDATA" };
                    title = "⚡ FIRMWARE FLASHING — SAMSUNG ODIN";
                    titleColor = Color.FromArgb(100, 181, 246);
                    break;
                default:
                    newRoles = new[] { "Programmer (ELF/MBN)", "RawProgram (XML)", "Patch (XML)" };
                    title = "⚡ FIRMWARE FLASHING — QUALCOMM EDL 9008";
                    titleColor = Color.FromArgb(100, 181, 246);
                    break;
            }

            for (int i = 0; i < roles.Length; i++)
                roles[i] = i < newRoles.Length ? newRoles[i] : "";
            fileRowCount = newRoles.Length;

            lblTitle.Text = title;
            lblTitle.ForeColor = titleColor;

            Relayout();
            RefreshDetectionUi();
        }

        internal void SetFolder(string folder)
        {
            txtFolder.Text = folder ?? "";
            if (btnRescan != null) btnRescan.Visible = !string.IsNullOrWhiteSpace(txtFolder.Text);
        }

        internal void SetFiles(FlashFileSet set)
        {
            files = set ?? new FlashFileSet();
            SetFolder(files.Folder);
            RefreshDetectionUi();
        }

        internal string GetRolePath(int index)
        {
            if (index < 0 || index >= fileRowCount) return "";
            switch (category)
            {
                case "MediaTek":
                    return index == 0 ? files.Scatter : index == 1 ? files.DownloadAgent : index == 2 ? files.Auth : "";
                case "Spreadtrum":
                    return index == 0 ? files.Pac : "";
                case "Samsung":
                    return index switch
                    {
                        0 => files.SamsungBl,
                        1 => files.SamsungAp,
                        2 => files.SamsungCp,
                        3 => files.SamsungCsc,
                        4 => files.SamsungUserData,
                        _ => ""
                    };
                default:
                    return index == 0 ? files.Programmer : index == 1 ? files.RawProgram : index == 2 ? files.Patch : "";
            }
        }

        /// <summary>Loader auto-detect (Brand/Model) ကနေ ရလာတဲ့ ဖိုင်ကို သက်ဆိုင်ရာ row မှာ ပြဖို့</summary>
        internal void SetRoleFile(int index, string path)
        {
            if (index < 0 || index >= fileRowCount || string.IsNullOrWhiteSpace(path)) return;
            switch (category)
            {
                case "MediaTek":
                    if (index == 0) files.Scatter = path; else if (index == 1) files.DownloadAgent = path; else files.Auth = path;
                    break;
                case "Spreadtrum":
                    files.Pac = path;
                    break;
                case "Samsung":
                    if (index == 0) files.SamsungBl = path;
                    else if (index == 1) files.SamsungAp = path;
                    else if (index == 2) files.SamsungCp = path;
                    else if (index == 3) files.SamsungCsc = path;
                    else files.SamsungUserData = path;
                    break;
                default:
                    if (index == 0) files.Programmer = path; else if (index == 1) files.RawProgram = path; else files.Patch = path;
                    break;
            }
            if (string.IsNullOrEmpty(files.Folder)) SetFolder(Path.GetDirectoryName(path) ?? "");
            RefreshDetectionUi();
        }

        internal void ClearFolder()
        {
            files = new FlashFileSet();
            SetFolder("");
            RefreshDetectionUi();
        }

        // ===== Detection display =====
        // ဖိုင် status ကို label တွေနဲ့ မပြတော့ဘူး (နေရာ ကုန်တယ်) — folder button ရဲ့ tooltip ထဲမှာ
        // ဖိုင်စာရင်း (✅ name + size / ❌ Not found) ကို ပြ၊ ခလုတ်စာသားမှာ တွေ့တဲ့ အရေအတွက် ပြ
        private void RefreshDetectionUi()
        {
            if (btnSelectFolder == null) return;
            bool hasFolder = !string.IsNullOrWhiteSpace(txtFolder.Text);

            if (!hasFolder)
            {
                btnSelectFolder.Text = "📁 Select Firmware Folder";
                btnSelectFolder.BackColor = Color.FromArgb(33, 150, 243);
                folderTip.SetToolTip(btnSelectFolder, "Firmware folder တစ်ခု ရွေးပါ — ဖိုင်တွေကို အလိုအလျောက် ရှာပြပါမယ်");
                folderTip.SetToolTip(txtFolder, "Folder path ကို paste/ရိုက်ထည့်ပြီး Enter နှိပ်လည်း ရပါတယ်");
                return;
            }

            int found = CountFound();
            var sb = new System.Text.StringBuilder();
            if (found == 0) sb.AppendLine("⚠️  No firmware files detected in this folder");
            else sb.AppendLine($"✅  {found} / {fileRowCount} files detected");
            sb.AppendLine();
            for (int i = 0; i < fileRowCount; i++)
            {
                string path = GetRolePath(i);
                bool ok = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
                sb.AppendLine(ok ? $"✅  {roles[i]}:   {Describe(path)}" : $"❌  {roles[i]}:   Not found");
            }
            string tip = sb.ToString().TrimEnd();

            btnSelectFolder.Text = found > 0
                ? $"📁 Firmware Folder  •  {found} ✅"
                : "📁 Firmware Folder  •  none ⚠";
            btnSelectFolder.BackColor = found > 0 ? Color.FromArgb(46, 125, 50) : WarnOrange;
            folderTip.SetToolTip(btnSelectFolder, tip);
            folderTip.SetToolTip(txtFolder, tip);
        }

        /// <summary>Category အလိုက် တွေ့တဲ့ firmware ဖိုင် အရေအတွက်</summary>
        internal int CountFound()
        {
            int n = 0;
            for (int i = 0; i < fileRowCount; i++)
            {
                string path = GetRolePath(i);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) n++;
            }
            return n;
        }

        /// <summary>Category အလိုက် လိုအပ်တဲ့ ဖိုင် အရေအတွက်</summary>
        internal int ExpectedCount => fileRowCount;

        private static string Describe(string path)
        {
            long len = 0;
            try { len = new FileInfo(path).Length; } catch { }
            return $"{Path.GetFileName(path)}   ({FormatSize(len)})";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024 / 1024:0.00} GB";
            if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.0} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
            return bytes + " B";
        }

        // ===== Partition count / busy / progress =====
        internal void SetPartitionInfo(int selected, int total)
        {
            if (busy) return;
            btnStartFlash.Text = total > 0 ? $"⚡ START FLASHING  ({selected}/{total} partitions)" : "⚡ START FLASHING";
        }

        internal void SetBusy(bool isBusy)
        {
            busy = isBusy;
            btnStartFlash.Enabled = !isBusy;
            btnSelectFolder.Enabled = !isBusy;
            if (btnRescan != null) btnRescan.Enabled = !isBusy;
            btnStartFlash.Text = isBusy ? "⏳ FLASHING — ခဏစောင့်ပါ..." : "⚡ START FLASHING";
            btnStartFlash.BackColor = isBusy ? Color.FromArgb(120, 60, 60) : Color.FromArgb(230, 60, 60);
        }

        internal void SetProgress(int percent, string status)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            progress.Value = percent;
            lblProgress.Text = string.IsNullOrEmpty(status) ? $"{percent}%" : $"{percent}% ({status})";
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal FlashOptions Options
        {
            get => new FlashOptions
            {
                EraseBefore = chkErase.Checked,
                VerifyAfter = chkVerify.Checked,
                AutoReboot = chkAutoReboot.Checked,
                SkipUserData = chkSkipUserData.Checked,
            };
            set
            {
                if (value == null) return;
                chkErase.Checked = value.EraseBefore;
                chkVerify.Checked = value.VerifyAfter;
                chkAutoReboot.Checked = value.AutoReboot;
                chkSkipUserData.Checked = value.SkipUserData;
            }
        }

        // ===== Folder browse / drag-drop =====
        private void BrowseFolder()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Firmware folder ကို ရွေးပါ (rawprogram0.xml / scatter.txt / *.pac ပါတဲ့ folder)",
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() == DialogResult.OK) RaiseFolder(dlg.SelectedPath);
        }

        private void RaiseFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            folder = folder.Trim().Trim('"');
            // ဖိုင်တစ်ခုကို ရွေး/ရိုက်ထားရင် သူ့ folder ကို ယူ
            if (File.Exists(folder)) folder = Path.GetDirectoryName(folder) ?? folder;
            SetFolder(folder);
            FolderSelected?.Invoke(this, folder);
        }

        private void OnDragEnterOrOver(object? sender, DragEventArgs e)
        {
            if (busy) { e.Effect = DragDropEffects.None; return; }
            e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDropAny(object? sender, DragEventArgs e)
        {
            if (busy || e.Data == null) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
            string first = paths[0];
            string folder = Directory.Exists(first) ? first : (Path.GetDirectoryName(first) ?? "");
            if (!string.IsNullOrEmpty(folder)) RaiseFolder(folder);
        }

        // ===== Layout =====
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        internal void Relayout()
        {
            int w = Width;
            int y = 6;

            lblTitle.Location = new Point(10, y);
            y += 20;

            const int btnW = 210, btnH = 28, clearW = 30, rescanW = 34, gap = 6;
            int rescanX = w - 10 - clearW - gap - rescanW;
            btnSelectFolder.SetBounds(10, y, btnW, btnH);
            btnClearFolder.SetBounds(w - 10 - clearW, y, clearW, btnH);
            if (btnRescan != null) btnRescan.SetBounds(rescanX, y, rescanW, btnH);
            txtFolder.SetBounds(10 + btnW + gap, y + 2,
                Math.Max(80, rescanX - (10 + btnW + gap) - gap), btnH - 4);
            y += btnH + 10;

            // Options — ဘယ်ဘက်ညီ နှစ်တန်း (ခလုတ်တွေ ရောမနေရအောင်)
            int optY = y;
            chkErase.Location = new Point(14, optY + 2);
            chkVerify.Location = new Point(14 + 160, optY + 2);
            chkAutoReboot.Location = new Point(14, optY + 24);
            chkSkipUserData.Location = new Point(14 + 190, optY + 24);
            y += 48;

            const int flashH = 36;
            btnStartFlash.SetBounds(10, y, 300, flashH);
            lblProgress.SetBounds(w - 10 - 150, y + 2, 150, 16);
            progress.SetBounds(320, y + 8, Math.Max(60, w - 10 - 150 - 320 - 8), 20);

            int newHeight = y + flashH + 10;
            if (Height != newHeight) Height = newHeight;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 4 || Height < 4) return;
            Color c = BackColor;
            using (var dark = new Pen(Form1.Ui3D.Darken(c, 0.45f)))
            {
                e.Graphics.DrawLine(dark, 0, 0, Width - 1, 0);
                e.Graphics.DrawLine(dark, 0, Height - 1, Width - 1, Height - 1);
                e.Graphics.DrawLine(dark, 0, 0, 0, Height - 1);
                e.Graphics.DrawLine(dark, Width - 1, 0, Width - 1, Height - 1);
            }
            using (var hi = new Pen(Form1.Ui3D.Lighten(c, 0.16f)))
                e.Graphics.DrawLine(hi, 1, 1, Width - 2, 1);
        }
    }
}
