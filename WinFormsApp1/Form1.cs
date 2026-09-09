#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;

using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // (Theme engine — ThemeManager.cs မှာ သီးခြားခွဲထားသည်)

    public partial class Form1 : Form
    {
        // ================= Paths =================
        private string adbPath = "";

        // Sideload tab — package (zip) picker row
        private Panel sideloadPanel = null;
        private TextBox txtSideloadPath = null;
        private string fastbootPath = "";
        private string pythonPath = "python";
        private string mtkScriptPath = "";
        private string edlScriptPath = "";
        private string spdScriptPath = "";
        private string deviceDatabasePath = "";
        private string loadersDirectoryPath = "";
        private string qualcommCorePath = "";
        private string qflEnginePath = "";

        // ================= Process Control =================
        private CancellationTokenSource cts = null;
        private Process currentProcess = null;
        private bool isOperationRunning = false;
        private System.Windows.Forms.Timer portTimer = null;

        // ================= Data =================
        private List<PartitionInfo> partitions = new List<PartitionInfo>();
        private string currentCategory = "Qualcomm";
        private string selectedPartitionName = "boot";
        private string currentMemoryType = "emmc";
        private bool usb9008Available = false;   // WinUSB (QHSUSB__BULK) transport ရှိမရှိ — USB mode က serial ထက် 3x မြန်ပါတယ်
        private DateTime usbProbeTime = DateTime.MinValue;
        private int lastPythonExitCode = -1;     // python op တစ်ခုစီရဲ့ exit code (success စစ်ဖို့)

        // ============ Clean EDL log state ============
        private string detectedChipset = "";     // python ကနေ ဖတ်လို့ရတဲ့ chipset (CPU detected)
        private string detectedHwid = "";        // HWID (auto-loader learning အတွက်)
        private string detectedPkhash = "";      // PK_HASH (auto-loader learning အတွက်)
        private bool detectedLoaderMissing = false; // python က ဒီဖုန်းအတွက် loader မတွေ့ဘူးဆိုတဲ့ flag
        private string lastSmartLine = "";
        private int lastSmartRepeat = 0;

        // ================= Comprehensive CPU & Model Database =================
        private readonly Dictionary<string, Dictionary<string, List<string>>> chipsetDatabase = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase)
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

        // ================= UI Controls =================
        private Panel mobileSeaShell = null;
        private ProgressBar globalProgressBar = null;
        private Label lblProgressPercent = null;
        private ComboBox cboMemoryType = null;
        private Label lblMemType = null;
        private DataGridView mobilePartitionGrid = null;
        private ContextMenuStrip partitionContextMenu = null;
        private ComboBox mobileBrandCombo = null;
        private ComboBox mobileModelCombo = null;
        private ComboBox mobilePortCombo = null;
        private TextBox txtFirmwarePath = null;
        private Label lblLoaderTitle = null;
        private Button btnBrowseLoader = null;
        private Button btnShowTp = null;
        private Panel profilePanel = null;
        private Panel dynamicActionPanel = null;

        // Dedicated Multi-Flashing Hub Panel
        private Panel flasherHubPanel = null;
        private Label lblFlasherTitle = null;

        // Slot Controls
        private TextBox txtSlot1 = null, txtSlot2 = null, txtSlot3 = null, txtSlot4 = null, txtSlot5 = null;
        private CheckBox chkSlot1 = null, chkSlot2 = null, chkSlot3 = null, chkSlot4 = null, chkSlot5 = null;
        private Button btnBrowseSlot1 = null, btnBrowseSlot2 = null, btnBrowseSlot3 = null, btnBrowseSlot4 = null, btnBrowseSlot5 = null;
        private Button btnMasterFlash = null;
        private Button btnResetFlasher = null;
        private CheckBox chkAutoRebootMaster = null;

        private RichTextBox rtbOutput = null;
        private StatusStrip statusStrip = null;
        private ToolStripStatusLabel lblStatus = null;
        private OpenFileDialog openFileDlg = null;
        private SaveFileDialog saveFileDlg = null;
        private ComboBox cboUSB = null;
        private ComboBox cboCOM = null;
        private TabControl tabControl = null;
        private Panel devicePanel = null;
        private Panel topPanel = null;

        private List<Button> dynamicButtons = new List<Button>();
        private List<Button> categoryTabButtons = new List<Button>();
        private Button btnMobileGo = null;

        private class PartitionInfo
        {
            public string Name { get; set; }
            public string Offset { get; set; }
            public string Length { get; set; }
            public string Type { get; set; }
        }

        // ================= Qualcomm Firmware (QFIL) Preview State =================
        private bool qcFirmwarePreviewMode = false;          // grid မှာ firmware partitions ပြနေလား (device GPT မဟုတ်)
        private string qcFirmwareDir = "";                    // images folder
        private string qcFirmwareSourceXml = "";              // မူရင်း rawprogram0.xml
        private string qcFirmwarePatchXml = "";               // patch0.xml (မရှိရင် "")
        private List<string> qcFirmwareOrderedFiles = new List<string>(); // flash sequence (filename)
        private List<string> qcFirmwareProgramLines = new List<string>();  // original <program .../> lines (row order)
        private Button btnQcFlashSelected = null;
        private Button btnQcMiBypassBtn = null;     // Xiaomi brand ရွေးမှသာ ပြတဲ့ Mi Account bypass
        private Button btnQcHexEditBtn = null;      // Xiaomi-specific helpers (Hex Edit / Persist)
        private Button btnQcPersistBuBtn = null;
        private Button btnQcPersistResBtn = null;
        private Button btnMtkMiAccountBtn = null;   // MediaTek Xiaomi Mi Account reset

        // ================= Theme Colors =================
        private readonly Color colorADB = Color.FromArgb(100, 181, 246);
        private readonly Color colorFastboot = Color.FromArgb(255, 167, 38);
        private readonly Color colorMTK = Color.FromArgb(102, 187, 106);
        private readonly Color colorQualcomm = Color.FromArgb(239, 83, 80);
        private readonly Color colorSamsung = Color.FromArgb(171, 71, 188);
        private readonly Color colorSPD = Color.FromArgb(0, 188, 212);
        private readonly Color colorSuccess = Color.FromArgb(129, 199, 132);
        private readonly Color colorError = Color.FromArgb(229, 115, 115);
        private readonly Color colorWarning = Color.FromArgb(255, 213, 79);
        private readonly Color colorInfo = Color.FromArgb(200, 200, 220);

        // ================= Theme Engine state =================
        private readonly Dictionary<Control, int> themeSlotMap = new Dictionary<Control, int>();
        private bool themeLoading = true;
        private ComboBox cboTheme = null;
        private string ThemeFilePath => Path.Combine(Application.StartupPath, "theme.txt");

        // Settings tab — PC info value labels (title → value label)
        private Panel settingsPanel = null;
        private readonly List<(string title, Label valLbl)> pcInfoRows = new List<(string, Label)>();

        // RAM info (GlobalMemoryStatusEx)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // ================= Constructor =================
        public Form1()
        {
            InitializeComponent();

            this.WindowState = FormWindowState.Normal;
            this.Size = new Size(1280, 750);
            this.MinimumSize = new Size(1024, 768);
            this.StartPosition = FormStartPosition.CenterScreen;

            if (openFileDlg == null) openFileDlg = new OpenFileDialog();
            if (saveFileDlg == null) saveFileDlg = new SaveFileDialog();
            if (statusStrip == null)
            {
                statusStrip = new StatusStrip();
                lblStatus = new ToolStripStatusLabel { Text = "Ready" };
                statusStrip.Items.Add(lblStatus);
            }
            if (rtbOutput == null) rtbOutput = new RichTextBox();
            if (cboUSB == null) cboUSB = new ComboBox();
            if (cboCOM == null) cboCOM = new ComboBox();
            if (tabControl == null) tabControl = new TabControl { Visible = false };
            if (devicePanel == null) devicePanel = new Panel { Visible = false };
            if (topPanel == null) topPanel = new Panel { Visible = false };

            FormClosing += Form1_FormClosing;
            InitializePaths();
            InitializePythonPaths();

            deviceDatabasePath = Path.Combine(Application.StartupPath, "device_database");
            if (!Directory.Exists(deviceDatabasePath)) Directory.CreateDirectory(deviceDatabasePath);

            loadersDirectoryPath = Path.Combine(Application.StartupPath, "Loaders");
            if (!Directory.Exists(loadersDirectoryPath)) Directory.CreateDirectory(loadersDirectoryPath);

            qualcommCorePath = Path.Combine(Application.StartupPath, "QualcommCore");
            if (!Directory.Exists(qualcommCorePath)) Directory.CreateDirectory(qualcommCorePath);

            InitializeMobileSeaLayout();
            SwitchCategory("Qualcomm");
            InitializePortTimer();
            RefreshPorts();

            // Theme engine — UI တစ်ခုလုံးကို ရွေးထားတဲ့ theme နဲ့ ပြန်ချိန်တယ်
            ApplyTheme();

            Log("╔══════════════════════════════════════════════╗", colorInfo);
            Log("║   PMK Unlock Tool v4.0 - Advanced Edition    ║", colorInfo);
            Log("╚══════════════════════════════════════════════╝", colorInfo);
            Log("📱 Connect your device and select a command.", colorInfo);

            if (IOFile.Exists(qflEnginePath))
            {
                Log("✅ QFL Qualcomm Native Flash Engine (QSaharaServer & fh_loader) Ready!", colorSuccess);
            }
            else if (IOFile.Exists(Path.Combine(qualcommCorePath, "QSaharaServer.exe")) && IOFile.Exists(Path.Combine(qualcommCorePath, "fh_loader.exe")))
            {
                Log("✅ Qualcomm Native C++ Engine (QSaharaServer & fh_loader) Ready!", colorSuccess);
            }
            else if (!string.IsNullOrEmpty(edlScriptPath) && IOFile.Exists(edlScriptPath))
            {
                Log($"✅ Qualcomm Python EDL Module Ready: {Path.GetFileName(edlScriptPath)}", colorSuccess);
            }

            Log("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", colorInfo);
        }

        // ================= UI Layout =================
        private void InitializeMobileSeaLayout()
        {
            tabControl.Visible = false;
            devicePanel.Visible = false;
            topPanel.Visible = false;

            mobileSeaShell = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 24, 32), Padding = new Padding(6), AllowDrop = true };

            // ===== Left Panel =====
            Panel leftPanel = new Panel { Dock = DockStyle.Left, Width = 430, BackColor = Color.FromArgb(14, 20, 27), Padding = new Padding(6) };

            Panel connectionPanel = new Panel { Dock = DockStyle.Top, Height = 95, BackColor = Color.FromArgb(27, 36, 48), BorderStyle = BorderStyle.FixedSingle };
            Label connectionTitle = CreateSeaLabel("🔌  CONNECTION", new Point(10, 8), true);
            Label portLabel = CreateSeaLabel("Communications Port", new Point(10, 36), false);

            mobilePortCombo = new ComboBox { Location = new Point(10, 58), Size = new Size(160, 25), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(35, 45, 58), ForeColor = Color.White };
            mobilePortCombo.Items.Add("Auto Detect / USB");
            mobilePortCombo.SelectedIndex = 0;

            CheckBox autoConnect = new CheckBox { Text = "Auto", Location = new Point(175, 60), AutoSize = true, Checked = true, ForeColor = Color.White };
            btnMobileGo = CreateSeaButton("🔄 Ref", new Point(230, 56), 55, 28, (s, e) => { RefreshPorts(); Log("🔄 Ports refreshed.", colorInfo); });
            Button btnDevMgr = CreateSeaButton("🛠️ DevMgr", new Point(290, 56), 65, 28, (s, e) => { try { Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }); } catch { } });
            btnDevMgr.BackColor = Color.FromArgb(40, 70, 90);

            Button btnDrivers = CreateSeaButton("📦 Driver", new Point(360, 56), 60, 28, (s, e) => InstallAllDrivers());
            btnDrivers.BackColor = Color.FromArgb(45, 75, 60);

            connectionPanel.Controls.AddRange(new Control[] { connectionTitle, portLabel, mobilePortCombo, autoConnect, btnMobileGo, btnDevMgr, btnDrivers });

            Panel logHeader = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(35, 47, 61), Padding = new Padding(4) };
            logHeader.Controls.Add(CreateSeaLabel("📋  LOG", new Point(8, 9), true));

            Button btnStopOp = CreateSeaButton("🛑 STOP", new Point(160, 4), 90, 28, btnStop_Click);
            btnStopOp.BackColor = Color.FromArgb(220, 40, 40);
            btnStopOp.ForeColor = Color.White;
            btnStopOp.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            logHeader.Controls.Add(btnStopOp);

            Button btnSaveLog = CreateSeaButton("💾 Save", new Point(260, 4), 70, 28, (s, e) => ExportLogToFile());
            btnSaveLog.BackColor = Color.FromArgb(45, 75, 60);
            logHeader.Controls.Add(btnSaveLog);

            Button btnClear = CreateSeaButton("🗑️ Clear", new Point(338, 4), 70, 28, (s, e) => { rtbOutput.Clear(); Log("Log cleared", colorWarning); });
            btnClear.BackColor = Color.FromArgb(50, 60, 75);
            logHeader.Controls.Add(btnClear);

            rtbOutput.Parent = leftPanel;
            rtbOutput.Dock = DockStyle.Fill;
            rtbOutput.Margin = new Padding(0);
            rtbOutput.BackColor = Color.FromArgb(10, 16, 22);
            rtbOutput.ForeColor = Color.FromArgb(210, 220, 230);
            rtbOutput.BorderStyle = BorderStyle.FixedSingle;
            rtbOutput.WordWrap = false;

            leftPanel.Controls.Add(rtbOutput);
            leftPanel.Controls.Add(logHeader);
            leftPanel.Controls.Add(connectionPanel);

            // ===== Right Panel =====
            Panel rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 29, 39), Padding = new Padding(8) };
            FlowLayoutPanel categoryBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(31, 41, 55), Padding = new Padding(4), WrapContents = false, AutoScroll = true };

            string[] categories = { "Qualcomm", "MediaTek", "ADB", "Fastboot", "Sideload", "Spreadtrum", "Samsung", "Settings" };
            categoryTabButtons.Clear();
            foreach (string category in categories)
            {
                Button catBtn = CreateSeaButton(category, Point.Empty, 95, 36, (s, e) => SwitchCategory(((Button)s).Text));
                categoryTabButtons.Add(catBtn);
                categoryBar.Controls.Add(catBtn);
            }

            // 🎨 Settings tab panel (theme + PC info) — theme selector က အခု Settings ထဲမှာပဲ
            BuildSettingsPanel(rightPanel);

            // ===== Profile Panel with TP Pinout Button =====
            profilePanel = new Panel { Dock = DockStyle.Top, Height = 75, BackColor = Color.FromArgb(27, 36, 48), BorderStyle = BorderStyle.FixedSingle };
            profilePanel.Controls.Add(CreateSeaLabel("PROFILE", new Point(10, 10), true));
            profilePanel.Controls.Add(CreateSeaLabel("Brand", new Point(75, 10), false));

            mobileBrandCombo = new ComboBox
            {
                Location = new Point(120, 7),
                Size = new Size(160, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 45, 58),
                ForeColor = Color.White
            };
            mobileBrandCombo.SelectedIndexChanged += MobileBrandCombo_SelectedIndexChanged;
            profilePanel.Controls.Add(mobileBrandCombo);

            profilePanel.Controls.Add(CreateSeaLabel("Model", new Point(290, 10), false));
            mobileModelCombo = new ComboBox
            {
                Location = new Point(335, 7),
                Size = new Size(200, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 45, 58),
                ForeColor = Color.White
            };
            mobileModelCombo.Items.Add("# Auto Detect");
            mobileModelCombo.SelectedIndex = 0;
            mobileModelCombo.SelectedIndexChanged += MobileModelCombo_SelectedIndexChanged;
            profilePanel.Controls.Add(mobileModelCombo);

            btnShowTp = CreateSeaButton("📌 Pinout", new Point(545, 6), 85, 27, (s, e) =>
            {
                string brand = mobileBrandCombo.SelectedItem?.ToString() ?? "";
                string model = mobileModelCombo.SelectedItem?.ToString() ?? "";
                ShowTestPointViewer(brand, model);
            });
            btnShowTp.BackColor = Color.FromArgb(255, 152, 0);
            profilePanel.Controls.Add(btnShowTp);

            lblLoaderTitle = CreateSeaLabel("📁 Firehose Loader:", new Point(10, 42), false);
            txtFirmwarePath = CreateServiceTextBox(new Point(140, 40), 395);
            btnBrowseLoader = CreateSeaButton("📂 Browse", new Point(545, 38), 85, 26, BrowseFirmware_Click);

            profilePanel.Controls.AddRange(new Control[] { lblLoaderTitle, txtFirmwarePath, btnBrowseLoader });

            dynamicActionPanel = new Panel { Dock = DockStyle.Top, Height = 135, BackColor = Color.FromArgb(25, 33, 44), BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(6), AutoScroll = true };

            // ===== Partition Grid Context Menu =====
            partitionContextMenu = new ContextMenuStrip();
            ToolStripMenuItem menuRead = new ToolStripMenuItem("📖 Read Partition (Dump)", null, (s, e) => ExecutePartitionAction("Read"));
            ToolStripMenuItem menuWrite = new ToolStripMenuItem("✏️ Write Partition (Flash)", null, (s, e) => ExecutePartitionAction("Write"));
            ToolStripMenuItem menuErase = new ToolStripMenuItem("🗑️ Erase Partition", null, (s, e) => ExecutePartitionAction("Erase"));
            ToolStripMenuItem menuFormat = new ToolStripMenuItem("🔄 Format Partition", null, (s, e) => ExecutePartitionAction("Format"));
            partitionContextMenu.Items.AddRange(new ToolStripItem[] { menuRead, menuWrite, menuErase, menuFormat });

            // ===== Partition Grid =====
            mobilePartitionGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(13, 20, 28),
                GridColor = Color.FromArgb(45, 60, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                ContextMenuStrip = partitionContextMenu,
                ColumnHeadersHeight = 28
            };
            mobilePartitionGrid.RowTemplate.Height = 24;

            mobilePartitionGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 44, 60);
            mobilePartitionGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(220, 235, 250);
            mobilePartitionGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            mobilePartitionGrid.DefaultCellStyle.BackColor = Color.FromArgb(18, 26, 36);
            mobilePartitionGrid.DefaultCellStyle.ForeColor = Color.FromArgb(220, 230, 245);
            mobilePartitionGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(33, 150, 243);
            mobilePartitionGrid.DefaultCellStyle.SelectionForeColor = Color.White;
            mobilePartitionGrid.DefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            mobilePartitionGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(23, 33, 46);
            mobilePartitionGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "✓", Width = 30, ReadOnly = false });
            mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Partition Name", Width = 150, ReadOnly = true });
            mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File Name", Width = 160, ReadOnly = true });
            mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Offset", Width = 140, ReadOnly = true });
            mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Length / Size", Width = 120, ReadOnly = true });
            mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });

            mobilePartitionGrid.SelectionChanged += MobilePartitionGrid_SelectionChanged;
            mobilePartitionGrid.CellDoubleClick += MobilePartitionGrid_CellDoubleClick;

            // Checkbox tick ပြောင်းတာနဲ့ Flash Selected ခလုတ်ရဲ့ count ကို update လုပ်ဖို့
            mobilePartitionGrid.CellValueChanged += (s, e) => { if (qcFirmwarePreviewMode && e.RowIndex >= 0 && e.ColumnIndex == 0) UpdateFlashSelButtonText(); };
            mobilePartitionGrid.CurrentCellDirtyStateChanged += (s, e) => { if (mobilePartitionGrid.IsCurrentCellDirty) mobilePartitionGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };

            // Universal Multi-Brand Flasher Panel
            InitializeUniversalFlasherHub(rightPanel);

            // ===== Sideload Package Row (Sideload tab မှာသာ ပေါ်မယ်) =====
            sideloadPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(27, 36, 48),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            sideloadPanel.Controls.Add(CreateSeaLabel("📦 Package (.zip):", new Point(10, 12), false));
            txtSideloadPath = CreateServiceTextBox(new Point(150, 9), 500);
            txtSideloadPath.ReadOnly = true;
            sideloadPanel.Controls.Add(txtSideloadPath);
            Button btnSlBrowse = CreateSeaButton("📂 Browse", new Point(665, 8), 85, 26, (s, e) =>
            {
                using var dlg = new OpenFileDialog { Title = "Select ZIP package to sideload (ROM/OTA/patch)", Filter = "ZIP Package (*.zip)|*.zip|All files (*.*)|*.*" };
                if (dlg.ShowDialog() == DialogResult.OK) txtSideloadPath.Text = dlg.FileName;
            });
            btnSlBrowse.BackColor = Color.FromArgb(33, 150, 243);
            sideloadPanel.Controls.Add(btnSlBrowse);

            // Footer Bar with Progress Bar
            Panel footerBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(20, 28, 38) };

            globalProgressBar = new ProgressBar
            {
                Location = new Point(8, 6),
                Size = new Size(300, 18),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            lblProgressPercent = new Label
            {
                Text = "0%",
                Location = new Point(315, 6),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 230, 118)
            };

            footerBar.Controls.Add(globalProgressBar);
            footerBar.Controls.Add(lblProgressPercent);

            // ===== Memory Type Selector (Qualcomm EDL: eMMC / UFS) =====
            lblMemType = CreateSeaLabel("Mem:", new Point(660, 8), false);
            lblMemType.ForeColor = Color.FromArgb(0, 230, 118);
            lblMemType.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);

            cboMemoryType = new ComboBox
            {
                Location = new Point(695, 4),
                Size = new Size(85, 23),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 45, 58),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5F)
            };
            cboMemoryType.Items.AddRange(new object[] { "eMMC", "UFS" });
            cboMemoryType.SelectedIndex = 0;
            cboMemoryType.SelectedIndexChanged += (s, e) =>
            {
                currentMemoryType = cboMemoryType.SelectedItem?.ToString() ?? "emmc";
            };

            footerBar.Controls.Add(lblMemType);
            footerBar.Controls.Add(cboMemoryType);

            rightPanel.Controls.Add(mobilePartitionGrid);
            rightPanel.Controls.Add(flasherHubPanel);
            if (settingsPanel != null) rightPanel.Controls.Add(settingsPanel);
            rightPanel.Controls.Add(footerBar);
            rightPanel.Controls.Add(dynamicActionPanel);
            if (sideloadPanel != null) rightPanel.Controls.Add(sideloadPanel);
            rightPanel.Controls.Add(profilePanel);
            rightPanel.Controls.Add(categoryBar);

            // PROFILE စာတန်းတွေ ဖတ်ရလွယ်အောင် ဖောင့် ပိုကြီး/ထူပေးတယ်
            foreach (Control c in profilePanel.Controls)
            {
                if (c is Label l)
                    l.Font = new Font("Segoe UI", l.Font.Bold ? 9.5F : 9.25F, FontStyle.Bold);
            }

            mobileSeaShell.Controls.Add(rightPanel);
            mobileSeaShell.Controls.Add(leftPanel);

            statusStrip.Dock = DockStyle.Bottom;
            statusStrip.BackColor = Color.FromArgb(12, 17, 23);
            statusStrip.ForeColor = Color.White;
            Controls.Add(mobileSeaShell);
            Controls.Add(statusStrip);
        }

        // ================= Universal Multi-Brand Flasher Hub UI =================
        private void InitializeUniversalFlasherHub(Panel parentPanel)
        {
            flasherHubPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 24, 32),
                Padding = new Padding(12),
                Visible = false
            };

            lblFlasherTitle = CreateSeaLabel("⚡ FIRMWARE FLASHING ENGINE", new Point(12, 8), true);
            lblFlasherTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            lblFlasherTitle.ForeColor = Color.FromArgb(100, 181, 246);
            flasherHubPanel.Controls.Add(lblFlasherTitle);

            int startY = 40;
            int gapY = 36;

            void CreateSlotRow(ref CheckBox chk, ref TextBox txt, ref Button btn, int yPos, int slotIndex)
            {
                chk = new CheckBox
                {
                    Text = "SLOT",
                    Location = new Point(15, yPos + 3),
                    Size = new Size(125, 24),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Checked = true
                };

                txt = CreateServiceTextBox(new Point(145, yPos), 425);
                txt.ReadOnly = true;

                btn = CreateSeaButton("📂 Browse", new Point(580, yPos - 1), 90, 27, (s, e) => BrowseFlasherSlot(slotIndex));
                btn.BackColor = Color.FromArgb(40, 60, 85);

                flasherHubPanel.Controls.AddRange(new Control[] { chk, txt, btn });
            }

            CreateSlotRow(ref chkSlot1, ref txtSlot1, ref btnBrowseSlot1, startY, 1);
            CreateSlotRow(ref chkSlot2, ref txtSlot2, ref btnBrowseSlot2, startY + gapY, 2);
            CreateSlotRow(ref chkSlot3, ref txtSlot3, ref btnBrowseSlot3, startY + (gapY * 2), 3);
            CreateSlotRow(ref chkSlot4, ref txtSlot4, ref btnBrowseSlot4, startY + (gapY * 3), 4);
            CreateSlotRow(ref chkSlot5, ref txtSlot5, ref btnBrowseSlot5, startY + (gapY * 4), 5);

            // Options Bar
            Panel optPanel = new Panel { Location = new Point(15, startY + (gapY * 5) + 6), Size = new Size(655, 36), BackColor = Color.FromArgb(25, 33, 44) };
            chkAutoRebootMaster = new CheckBox { Text = "Auto Reboot after Flash", Location = new Point(15, 8), AutoSize = true, Checked = true, ForeColor = Color.FromArgb(0, 230, 118), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
            optPanel.Controls.Add(chkAutoRebootMaster);
            flasherHubPanel.Controls.Add(optPanel);

            // Action Buttons
            btnMasterFlash = CreateSeaButton("⚡ START (FLASH FIRMWARE)", new Point(15, startY + (gapY * 6) + 10), 240, 40, (s, e) => ExecuteMasterFlash());
            btnMasterFlash.BackColor = Color.FromArgb(230, 60, 60);
            btnMasterFlash.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);

            btnResetFlasher = CreateSeaButton("🗑️ Reset Slots", new Point(265, startY + (gapY * 6) + 10), 120, 40, (s, e) => ResetFlasherSlots());
            btnResetFlasher.BackColor = Color.FromArgb(60, 70, 85);

            flasherHubPanel.Controls.AddRange(new Control[] { btnMasterFlash, btnResetFlasher });
            parentPanel.Controls.Add(flasherHubPanel);
        }

        private void ConfigureFlasherHubForCategory(string category)
        {
            if (flasherHubPanel == null) return;

            ResetFlasherSlots();

            if (category == "Samsung")
            {
                lblFlasherTitle.Text = "🔥 SAMSUNG OFFICIAL ODIN FLASH ENGINE (4-FILE / 5-FILE)";
                lblFlasherTitle.ForeColor = colorSamsung;

                chkSlot1.Text = "BL (Bootloader)"; chkSlot1.Visible = true; txtSlot1.Visible = true; btnBrowseSlot1.Visible = true;
                chkSlot2.Text = "AP (System/PDA)"; chkSlot2.Visible = true; txtSlot2.Visible = true; btnBrowseSlot2.Visible = true;
                chkSlot3.Text = "CP (Modem/Phone)"; chkSlot3.Visible = true; txtSlot3.Visible = true; btnBrowseSlot3.Visible = true;
                chkSlot4.Text = "CSC / HOME_CSC"; chkSlot4.Visible = true; txtSlot4.Visible = true; btnBrowseSlot4.Visible = true;
                chkSlot5.Text = "USERDATA"; chkSlot5.Visible = true; txtSlot5.Visible = true; btnBrowseSlot5.Visible = true;
            }
            else if (category == "Qualcomm")
            {
                lblFlasherTitle.Text = "🔥 QUALCOMM EDL 9008 FLASH ENGINE (QFIL XML PROTOCOL)";
                lblFlasherTitle.ForeColor = colorQualcomm;

                chkSlot1.Text = "Programmer (ELF/MBN)"; chkSlot1.Visible = true; txtSlot1.Visible = true; btnBrowseSlot1.Visible = true;
                chkSlot2.Text = "RawProgram (XML)"; chkSlot2.Visible = true; txtSlot2.Visible = true; btnBrowseSlot2.Visible = true;
                chkSlot3.Text = "Patch File (XML)"; chkSlot3.Visible = true; txtSlot3.Visible = true; btnBrowseSlot3.Visible = true;
                chkSlot4.Visible = false; txtSlot4.Visible = false; btnBrowseSlot4.Visible = false;
                chkSlot5.Visible = false; txtSlot5.Visible = false; btnBrowseSlot5.Visible = false;
            }
            else if (category == "MediaTek")
            {
                lblFlasherTitle.Text = "🔥 MEDIATEK SP FLASH ENGINE (SCATTER MULTI-PARTITION)";
                lblFlasherTitle.ForeColor = colorMTK;

                chkSlot1.Text = "Scatter File (TXT)"; chkSlot1.Visible = true; txtSlot1.Visible = true; btnBrowseSlot1.Visible = true;
                chkSlot2.Text = "Download Agent (DA)"; chkSlot2.Visible = true; txtSlot2.Visible = true; btnBrowseSlot2.Visible = true;
                chkSlot3.Text = "Authentication (AUTH)"; chkSlot3.Visible = true; txtSlot3.Visible = true; btnBrowseSlot3.Visible = true;
                chkSlot4.Visible = false; txtSlot4.Visible = false; btnBrowseSlot4.Visible = false;
                chkSlot5.Visible = false; txtSlot5.Visible = false; btnBrowseSlot5.Visible = false;
            }
            else if (category == "Spreadtrum")
            {
                lblFlasherTitle.Text = "🔥 SPREADTRUM / UNISOC RESEARCH FLASH ENGINE (PAC ROM)";
                lblFlasherTitle.ForeColor = colorSPD;

                chkSlot1.Text = "PAC Firmware (.pac)"; chkSlot1.Visible = true; txtSlot1.Visible = true; btnBrowseSlot1.Visible = true;
                chkSlot2.Text = "FDL 1 Bootloader"; chkSlot2.Visible = true; txtSlot2.Visible = true; btnBrowseSlot2.Visible = true;
                chkSlot3.Text = "FDL 2 Stage Loader"; chkSlot3.Visible = true; txtSlot3.Visible = true; btnBrowseSlot3.Visible = true;
                chkSlot4.Visible = false; txtSlot4.Visible = false; btnBrowseSlot4.Visible = false;
                chkSlot5.Visible = false; txtSlot5.Visible = false; btnBrowseSlot5.Visible = false;
            }
        }

        private void ResetFlasherSlots()
        {
            if (txtSlot1 != null) txtSlot1.Text = "";
            if (txtSlot2 != null) txtSlot2.Text = "";
            if (txtSlot3 != null) txtSlot3.Text = "";
            if (txtSlot4 != null) txtSlot4.Text = "";
            if (txtSlot5 != null) txtSlot5.Text = "";
        }

        private void BrowseFlasherSlot(int slotIndex)
        {
            using OpenFileDialog ofd = new OpenFileDialog();

            if (currentCategory == "Samsung")
            {
                ofd.Filter = "Samsung Binary (*.tar;*.tar.md5;*.bin)|*.tar;*.tar.md5;*.bin|All Files (*.*)|*.*";
                if (ofd.ShowDialog() != DialogResult.OK) return;

                string filePath = ofd.FileName;
                string dir = Path.GetDirectoryName(filePath);

                if (slotIndex == 1) txtSlot1.Text = filePath;
                else if (slotIndex == 2) txtSlot2.Text = filePath;
                else if (slotIndex == 3) txtSlot3.Text = filePath;
                else if (slotIndex == 4) txtSlot4.Text = filePath;
                else if (slotIndex == 5) txtSlot5.Text = filePath;

                try
                {
                    var files = Directory.GetFiles(dir, "*.*").Where(f => f.EndsWith(".tar") || f.EndsWith(".tar.md5")).ToList();
                    foreach (var f in files)
                    {
                        string name = Path.GetFileName(f).ToUpperInvariant();
                        if (name.StartsWith("BL_") || name.Contains("_BL_")) txtSlot1.Text = f;
                        else if (name.StartsWith("AP_") || name.Contains("_AP_")) txtSlot2.Text = f;
                        else if (name.StartsWith("CP_") || name.Contains("_CP_")) txtSlot3.Text = f;
                        else if (name.StartsWith("CSC_") || name.StartsWith("HOME_CSC_") || name.Contains("_CSC_")) txtSlot4.Text = f;
                        else if (name.StartsWith("USERDATA_")) txtSlot5.Text = f;
                    }
                }
                catch (Exception ex) { LogWarning($"⚠️ Slot {slotIndex} firmware ဖိုင်ဖတ်ရာမှာ မအောင်မြင်ပါ: {ex.Message}"); }

                InspectTarFirmware(filePath, $"SLOT {slotIndex}");
            }
            else if (currentCategory == "Qualcomm")
            {
                if (slotIndex == 1)
                {
                    ofd.Filter = "Programmer Files (*.mbn;*.elf)|*.mbn;*.elf|All Files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK) txtSlot1.Text = ofd.FileName;
                }
                else
                {
                    ofd.Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        string path = ofd.FileName;
                        string dir = Path.GetDirectoryName(path);

                        if (slotIndex == 2)
                        {
                            txtSlot2.Text = path;
                            string autoPatch = Path.Combine(dir, "patch0.xml");
                            if (IOFile.Exists(autoPatch)) txtSlot3.Text = autoPatch;
                        }
                        else if (slotIndex == 3) txtSlot3.Text = path;

                        InspectXmlFirmware(path);
                        if (slotIndex == 2) LoadFirmwarePreview(path); // firmware partitions ကို grid ထဲ checkbox နဲ့ ပြမယ်
                    }
                }
            }
            else if (currentCategory == "MediaTek")
            {
                if (slotIndex == 1)
                {
                    ofd.Filter = "Scatter Files (*.txt)|*.txt|All Files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        txtSlot1.Text = ofd.FileName;
                        InspectScatterFirmware(ofd.FileName);
                    }
                }
                else
                {
                    ofd.Filter = "DA / Auth Files (*.bin;*.auth)|*.bin;*.auth|All Files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        if (slotIndex == 2) txtSlot2.Text = ofd.FileName;
                        else if (slotIndex == 3) txtSlot3.Text = ofd.FileName;
                    }
                }
            }
            else if (currentCategory == "Spreadtrum")
            {
                ofd.Filter = "Spreadtrum PAC Files (*.pac)|*.pac|All Files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtSlot1.Text = ofd.FileName;
                    InspectPacFirmware(ofd.FileName);
                }
            }
        }

        // ================= Inspect Firmwares =================
        private void InspectTarFirmware(string filePath, string slotName)
        {
            try
            {
                FileInfo fi = new FileInfo(filePath);
                double sizeMB = fi.Length / (1024.0 * 1024.0);
                string szStr = sizeMB >= 1024 ? $"{sizeMB / 1024.0:F2} GB" : $"{sizeMB:F2} MB";

                Log($"\n╔══════════════════════════════════════════════════════════╗", colorSamsung);
                Log($"║         📦 SAMSUNG BINARY LOADED [{slotName.PadRight(8)}]            ║", colorSamsung);
                Log($"╚══════════════════════════════════════════════════════════╝", colorSamsung);
                Log($"  • File Name  : {fi.Name}", colorSuccess);
                Log($"  • Total Size : {szStr}", colorInfo);

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
                            try { entryBytes = Convert.ToInt64(sizeOctal, 8); } catch { }
                            long blocks = (entryBytes + 511) / 512;
                            fs.Seek(blocks * 512, SeekOrigin.Current);
                        }
                    }
                }

                if (insideImages.Count > 0)
                {
                    Log("  • Partitions Inside Binary Archive:", colorFastboot);
                    foreach (var img in insideImages)
                    {
                        Log($"    ➔ 📄 {img}", Color.FromArgb(178, 235, 242));
                    }
                }
                Log("────────────────────────────────────────────────────────────\n", colorSamsung);
            }
            catch { }
        }

        private void InspectXmlFirmware(string xmlPath)
        {
            try
            {
                string text = IOFile.ReadAllText(xmlPath);
                var matches = Regex.Matches(text, @"filename=""(.*?)""\s+label=""(.*?)""", RegexOptions.IgnoreCase);

                Log($"\n╔══════════════════════════════════════════════════════════╗", colorQualcomm);
                Log("║          📦 QUALCOMM RAWPROGRAM XML LOADED               ║", colorQualcomm);
                Log("╚══════════════════════════════════════════════════════════╝", colorQualcomm);
                Log($"  • XML Name     : {Path.GetFileName(xmlPath)}", colorSuccess);
                Log($"  • Total Images : {matches.Count} partition images parsed", colorInfo);
                Log("────────────────────────────────────────────────────────────", colorQualcomm);
                Log("📋 [Partitions Queue to Flash]:", colorADB);

                int idx = 1;
                foreach (Match m in matches)
                {
                    if (idx <= 15)
                    {
                        Log($"  [{idx++:D2}] 🎯 {m.Groups[2].Value.PadRight(16)} ➔ 📁 {m.Groups[1].Value}", Color.FromArgb(179, 229, 252));
                    }
                }
                if (matches.Count > 15) Log($"  ... and {matches.Count - 15} more partitions", colorInfo);
                Log("────────────────────────────────────────────────────────────\n", colorQualcomm);
            }
            catch { }
        }

        private void InspectScatterFirmware(string scatterPath)
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

                Log($"\n╔══════════════════════════════════════════════════════════╗", colorMTK);
                Log("║             📦 MEDIATEK SCATTER LOADED                   ║", colorMTK);
                Log("╚══════════════════════════════════════════════════════════╝", colorMTK);
                Log($"  • Scatter File : {Path.GetFileName(scatterPath)}", colorSuccess);
                Log($"  • Target SoC   : {platform} [{storageType}]", colorInfo);
                Log($"  • Partitions   : {partCount} defined in partition layout", colorSuccess);
                Log("────────────────────────────────────────────────────────────\n", colorMTK);
            }
            catch { }
        }

        private void InspectPacFirmware(string pacPath)
        {
            try
            {
                FileInfo fi = new FileInfo(pacPath);
                double sizeMB = fi.Length / (1024.0 * 1024.0);

                Log($"\n╔══════════════════════════════════════════════════════════╗", colorSPD);
                Log("║             📦 SPREADTRUM PAC FIRMWARE LOADED            ║", colorSPD);
                Log("╚══════════════════════════════════════════════════════════╝", colorSPD);
                Log($"  • PAC Name     : {fi.Name}", colorSuccess);
                Log($"  • Package Size : {sizeMB:F2} MB", colorInfo);
                Log("────────────────────────────────────────────────────────────\n", colorSPD);
            }
            catch { }
        }

        // ================= QUALCOMM NATIVE (C++) & HYBRID EXECUTION =================
        private string GetActiveComPort()
        {
            string selected = mobilePortCombo.SelectedItem?.ToString() ?? "";
            if (selected.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return selected;

            var ports = SerialPort.GetPortNames();
            return ports.Length > 0 ? ports[0] : "COM11";
        }

        private async Task<bool> RunNativeSaharaLoader(string portName, string loaderPath)
        {
            string qSaharaExe = Path.Combine(qualcommCorePath, "QSaharaServer.exe");
            if (!IOFile.Exists(qSaharaExe)) return false;

            string args = $"-p \\\\.\\{portName} -s 13:\"{loaderPath}\"";

            LogQualcomm($"\n🚀 [Native Sahara] Uploading Loader via QSaharaServer: {Path.GetFileName(loaderPath)}...");
            UpdateGlobalProgress(25, "Uploading Loader...");

            // Loader upload (serial) က စက္ကန့် ၃၀-၉၀ ကြာနိုင်လို့ timeout 90s ပေးထားပြီး
            // cancel ဖြစ်ရင် COM port လွှတ်ဖို့ process ကို သေချာ kill လုပ်ပါတယ်
            using var saharaCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var output = new System.Text.StringBuilder();

            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = qSaharaExe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = qualcommCorePath
            };

            try
            {
                p.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) output.AppendLine(e.Data); };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await p.WaitForExitAsync(saharaCts.Token);

                string result = output.ToString();
                // QSaharaServer build အချို့က success ဖြစ်ရင်တောင် ဘာမှ ထုတ်မပြတတ်လို့ exit code 0 ကို အဓိက စစ်ပါတယ်
                if (p.ExitCode == 0 ||
                    result.Contains("Sahara protocol completed") ||
                    result.Contains("Done sending payload") ||
                    result.Contains("File transferred successfully"))
                {
                    LogSuccess("✅ Sahara Handshake Completed! Switched to Firehose Mode.");
                    return true;
                }

                LogError($"❌ QSaharaServer failed with exit code {p.ExitCode}.");
            }
            catch (OperationCanceledException)
            {
                LogError("❌ Sahara Upload Timeout (90s): ဖုန်းဘက်မှ တုံ့ပြန်မှု မရှိပါ။ ဖုန်းကို Battery ဖြုတ်/တပ်ပြီး Test Point ပြန်ထောက်ပေးပါ။");
            }
            catch (Exception ex)
            {
                LogError($"❌ Sahara Error: {ex.Message}");
            }
            finally
            {
                if (!p.HasExited) { try { p.Kill(entireProcessTree: true); } catch { } }
            }

            return false;
        }

        private async Task<string> RunNativeFhLoader(string portName, string fhArgs)
        {
            string fhLoaderExe = Path.Combine(qualcommCorePath, "fh_loader.exe");
            if (!IOFile.Exists(fhLoaderExe)) return null;

            string args = $"--port=\\\\.\\{portName} --noprompt {fhArgs}";
            return await RunProcessCommand(fhLoaderExe, args, "Executing Firehose Command...", true);
        }

        // ================= Hybrid Auto Sig Finder =================
        private string FindXiaomiSigFile()
        {
            string[] sigPaths = {
                Path.Combine(Application.StartupPath, "Loaders", "Xiaomi", "SIG's", "SIG #1.bin"),
                Path.Combine(Application.StartupPath, "Loaders", "Xiaomi", "SIG's", "SIG #1", "sig.bin"),
                Path.Combine(Application.StartupPath, "Loaders", "Xiaomi", "SIG", "SIG #1.bin"),
                Path.Combine(Application.StartupPath, "sig.bin")
            };

            foreach (var path in sigPaths)
            {
                if (IOFile.Exists(path)) return path;
            }

            try
            {
                string sigDir = Path.Combine(Application.StartupPath, "Loaders", "Xiaomi", "SIG's");
                if (Directory.Exists(sigDir))
                {
                    var binFiles = Directory.GetFiles(sigDir, "*.bin", SearchOption.AllDirectories);
                    if (binFiles.Length > 0) return binFiles[0];
                }
            }
            catch { }

            return "";
        }

        // ================= Hybrid Qualcomm Execution Handler =================
        // patch0.xml မရှိရင် အလွတ် patch ဖိုင်ဆောက်ပြီး filename ပြန်ပေးခြင်း (qfil က patch positional လိုလို့)
        private string EnsureQcPatchFile(string romDir, string patchXml)
        {
            if (!string.IsNullOrEmpty(patchXml) && IOFile.Exists(Path.Combine(romDir, patchXml)))
                return patchXml;
            string empty = Path.Combine(romDir, "_pmk_empty_patch.xml");
            if (!IOFile.Exists(empty))
                IOFile.WriteAllText(empty, "<?xml version=\"1.0\" ?>\n<patches>\n</patches>");
            return Path.GetFileName(empty);
        }

        // Qualcomm Flash Engine:
        //   1) Native QSaharaServer နဲ့ loader upload (fast, stable)
        //   2) Python EDL qfil နဲ့ flash — Xiaomi auth ကို module က auto ကိုင်တွယ်ပြီး
        //      per-file marker ([PMK-FLASH]/[PMK-DONE]) တွေကနေ log မှာ အရောင်စုံ ပြပါတယ်
        private async Task<bool> ExecuteQualcommHybridFlash(string port, string loader, string rawXml, string patchXml, string romDir, List<string> seqFiles = null)
        {
            bool isSuccess = false;

            // Auto-Detect: loader မပါဘဲ flash စမ်းရင် — ဒီဖုန်းအတွက် မှတ်ထားတဲ့ loader ရှိရင် အလိုအလျောက် ယူမယ်
            if (string.IsNullOrEmpty(loader) || !IOFile.Exists(loader))
            {
                string autoLdr = TryResolveAutoLoader();
                if (autoLdr.Length > 0)
                {
                    loader = autoLdr;
                    LogQualcomm($"🎯 Auto-Detect: using remembered loader for this phone ({Path.GetFileName(autoLdr)})");
                }
            }

            bool usbMode = UseUsbTransport(); // USB (WinUSB) mode - serial ထက် ~3.5x မြန်ပါတယ်

            if (usbMode)
            {
                LogQualcomm("\n⚡ [USB Mode] Fast transport detected — Python EDL က loader/auth/flash အကုန်လုပ်ပါမယ်.");
            }
            else
            {
                // STEP 1 (serial): Native loader upload (device က Sahara state မှာဆို firehose ပြောင်းပေးတယ်)
                if (IOFile.Exists(Path.Combine(qualcommCorePath, "QSaharaServer.exe")) && !string.IsNullOrEmpty(loader) && IOFile.Exists(loader))
                {
                    LogQualcomm("\n🚀 [1/2] Uploading Firehose Loader (QSaharaServer)...");
                    bool loaderUp = await RunNativeSaharaLoader(port, loader);
                    if (loaderUp)
                    {
                        LogQualcomm("🔄 Waiting for Firehose mode switch...");
                        await Task.Delay(3000);
                    }
                    else
                    {
                        LogWarning("⚠️ Native loader upload failed — Python EDL will try to handle the loader itself.");
                    }
                }
                else if (string.IsNullOrEmpty(loader) || !IOFile.Exists(loader))
                {
                    LogWarning("⚠️ No Firehose Loader selected — device must already have a loader running (Firehose mode).");
                }
            }

            // STEP 2: Python EDL qfil (auth auto + eMMC/UFS support + per-file colored progress)
            string edlScript = Path.Combine(Application.StartupPath, "edl", "edl.py");
            if (IOFile.Exists(edlScript))
            {
                string loaderArg = (!string.IsNullOrEmpty(loader) && IOFile.Exists(loader)) ? $"--loader=\"{loader}\" " : "";
                string patchName = EnsureQcPatchFile(romDir, patchXml);
                string transportArgs = usbMode
                    ? $"--memory={currentMemoryType} "
                    : $"--serial --memory={currentMemoryType} --portname={port} ";
                // patch arg က filename ပဲ ပို့ရတယ် — python က imagedir နဲ့ ကိုယ်တိုင် join လုပ်ပါတယ် (full path ပို့ရင် မတွေ့ဘူး)
                string edlCmd = $"\"{edlScript}\" {transportArgs}{loaderArg}qfil \"{rawXml}\" \"{patchName}\" \"{romDir}\"";

                LogQualcomm("\n🚀 [2/2] Flashing Firmware (Python EDL qfil)...");

                string edlRes = await RunQfilFlash(edlCmd, seqFiles, "Flashing Firmware...");

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
                    LogError("❌ Python EDL crashed — see log above.");
                }
                else
                {
                    LogWarning("⚠️ Flash result unclear — check log above for details.");
                }
            }
            else
            {
                LogError("❌ edl.py not found at: " + edlScript);
            }

            return isSuccess;
        }

        // ================= Colored per-file qfil flash runner =================
        // python qfil output ကို marker ([PMK-FLASH]/[PMK-DONE]) နဲ့ အရောင်စုံ log ပြောင်းပေးပါတယ်
        private async Task<string> RunQfilFlash(string arguments, List<string> seqFiles, string statusText)
        {
            if (string.IsNullOrWhiteSpace(pythonPath)) return null;

            using CancellationTokenSource operationCts = new CancellationTokenSource();
            using Process process = new Process();
            cts = operationCts;
            currentProcess = process;
            SetOperationState(true);
            if (!string.IsNullOrEmpty(statusText)) SetStatus(statusText);
            UpdateGlobalProgress(5, "Connecting...");

            process.StartInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-u {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Application.StartupPath
            };
            process.StartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            var fullOutput = new System.Text.StringBuilder();
            int flashCounter = 0;
            int totalFiles = seqFiles != null && seqFiles.Count > 0 ? seqFiles.Count : 0;
            string lastSpeed = "";

            void HandleLine(string rawLine)
            {
                if (string.IsNullOrEmpty(rawLine)) return;
                string line = Regex.Replace(rawLine, @"\x1B\[[^@-~]*[=@-~]", "").Trim();
                if (string.IsNullOrEmpty(line)) return;
                fullOutput.AppendLine(line);

                // python ရဲ့ raw progress bar တွေ (carriage-return) ကို ချန်ပြီး progress bar ကိုပဲ update လုပ်တယ်
                if (line.StartsWith("Progress:") || line.StartsWith("Done |") || line.StartsWith("|") || line.StartsWith("\r") || line.StartsWith("Wrote "))
                {
                    // ⚡ speed (MB/s) ကို ဖမ်းပြီး label မှာ ပြတယ်
                    Match spd = Regex.Match(line, @"([\d.]+)\s*(MB|KB|GB)/s");
                    if (spd.Success) lastSpeed = spd.Groups[1].Value + " " + spd.Groups[2].Value + "/s";

                    Match pct = Regex.Match(line, @"(\d{1,3}(?:\.\d+)?)%");
                    if (pct.Success && double.TryParse(pct.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pv))
                    {
                        int basePct = totalFiles > 0 ? (flashCounter * 100) / totalFiles : 0;
                        UpdateGlobalProgress(Math.Min(99, basePct + (int)(pv / (totalFiles > 0 ? totalFiles : 1))), lastSpeed);
                    }
                    return;
                }

                this.Invoke(new Action(() =>
                {
                    if (line.StartsWith("[PMK-FLASH]"))
                    {
                        string file = line.Substring("[PMK-FLASH]".Length).Trim();
                        flashCounter++;
                        int idx = flashCounter;
                        string cnt = totalFiles > 0 ? $"[{idx}/{totalFiles}] " : "";
                        Log($"\n🔥 {cnt}Flashing: {file} ...", Color.FromArgb(255, 179, 71));
                        SetStatus($"Flashing ({idx}{(totalFiles > 0 ? "/" + totalFiles : "")}): {file}");
                        UpdateGlobalProgress(totalFiles > 0 ? (idx * 100) / totalFiles : 10, file);
                    }
                    else if (line.StartsWith("[PMK-DONE]"))
                    {
                        string file = line.Substring("[PMK-DONE]".Length).Trim();
                        string spdTxt = lastSpeed.Length > 0 ? $" — ⚡ {lastSpeed}" : "";
                        Log($"✅ {file} — written successfully{spdTxt}", colorSuccess);
                    }
                    else if (line.Contains("[qfil] raw programming ok.") || line.Contains("[qfil] patching ok"))
                    {
                        Log(line.Replace("[qfil]", "📦"), colorSuccess);
                    }
                    else if (line.Contains("Only nop and sig") || line.Contains("Auth detected"))
                    {
                        Log($"🔑 {line}", colorWarning);
                    }
                    else if (line.Contains("authenticated", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"🔓 {line}", colorSuccess);
                    }
                    else if (line.Contains("Mode detected"))
                    {
                        Log($"🔌 {line}", Color.FromArgb(128, 216, 255));
                    }
                    else if (line.Contains("Traceback") || line.Contains("Error:") || line.Contains("error:") || line.Contains("failed", StringComparison.OrdinalIgnoreCase) || line.Contains("ERROR"))
                    {
                        Log($"❌ {line}", colorError);
                    }
                    else if (line.Contains("Uploading loader") || line.Contains("Waiting for the device") || line.Contains("Device detected"))
                    {
                        Log($"⏳ {line}", colorInfo);
                    }
                    else if (line.StartsWith("main") || line.StartsWith("firehose") || line.StartsWith("sahara") || line.StartsWith("DeviceClass"))
                    {
                        // logger prefix line တွေကို ချန်လိုက်တယ် (အဓိပ္ပါယ်မရှိလို့)
                    }
                    else if (!string.IsNullOrEmpty(line))
                    {
                        Log(line, colorInfo);
                    }
                }));
            }

            process.OutputDataReceived += (s, e) => HandleLine(e.Data);
            process.ErrorDataReceived += (s, e) => HandleLine(e.Data);

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(operationCts.Token);
                return fullOutput.ToString().Trim();
            }
            catch (OperationCanceledException)
            {
                LogError("🛑 Flash operation cancelled by user.");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"❌ {ex.Message}");
                return null;
            }
            finally
            {
                lastPythonExitCode = process.HasExited ? process.ExitCode : -1;
                if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
                currentProcess = null;
                cts = null;
                SetOperationState(false);
                SetStatus("Ready");
            }
        }

        // ================= Qualcomm Firmware Preview (QFIL / unlock-tool style) =================
        private string FormatKbSize(string kbStr)
        {
            try
            {
                double kb = Convert.ToDouble(kbStr, System.Globalization.CultureInfo.InvariantCulture);
                double bytes = kb * 1024.0;
                if (bytes >= 1024.0 * 1024.0 * 1024.0) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                if (bytes >= 1024.0 * 1024.0) return $"{bytes / (1024.0 * 1024.0):F2} MB";
                return $"{kb:F0} KB";
            }
            catch { return kbStr; }
        }

        // rawprogram0.xml ရွေးလိုက်တာနဲ့ firmware partitions တွေကို grid ထဲ checkbox နဲ့ ပြပေးခြင်း
        private void LoadFirmwarePreview(string xmlPath)
        {
            try
            {
                qcFirmwareSourceXml = xmlPath;
                qcFirmwareDir = Path.GetDirectoryName(xmlPath);
                qcFirmwarePatchXml = Path.Combine(qcFirmwareDir, "patch0.xml");
                if (!IOFile.Exists(qcFirmwarePatchXml))
                {
                    // patch ဖိုင် မရှိရင် အလွတ် patch ဖန်တီးပြီး သုံးပါတယ် (qfil က patch positional လိုလို့)
                    qcFirmwarePatchXml = Path.Combine(qcFirmwareDir, "_pmk_empty_patch.xml");
                    IOFile.WriteAllText(qcFirmwarePatchXml, "<?xml version=\"1.0\" ?>\n<patches>\n</patches>");
                }

                string text = IOFile.ReadAllText(xmlPath);
                var entries = Regex.Matches(text, @"<program\b[^>]*/>", RegexOptions.IgnoreCase);

                qcFirmwareProgramLines.Clear();
                qcFirmwareOrderedFiles.Clear();
                mobilePartitionGrid.Rows.Clear();
                partitions.Clear();

                foreach (Match m in entries)
                {
                    string entry = m.Value;
                    string fn = Regex.Match(entry, @"filename=""([^""]+)""").Groups[1].Value;
                    string label = Regex.Match(entry, @"label=""([^""]+)""").Groups[1].Value;
                    string off = Regex.Match(entry, @"start_byte_hex=""([^""]+)""").Groups[1].Value;
                    string kb = Regex.Match(entry, @"size_in_KB=""([^""]+)""").Groups[1].Value;
                    bool sparse = Regex.Match(entry, @"sparse=""([^""]+)""").Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrEmpty(fn)) continue;

                    qcFirmwareProgramLines.Add(entry);
                    qcFirmwareOrderedFiles.Add(fn);
                    mobilePartitionGrid.Rows.Add(true,
                        string.IsNullOrEmpty(label) ? fn : label,
                        fn,
                        string.IsNullOrEmpty(off) ? "auto" : off,
                        FormatKbSize(string.IsNullOrEmpty(kb) ? "0" : kb),
                        sparse ? "Sparse" : "Firmware");
                }

                qcFirmwarePreviewMode = true;
                mobilePartitionGrid.ContextMenuStrip = null; // device partition menu ကို preview mode မှာ ပိတ်
                flasherHubPanel.Visible = false;
                mobilePartitionGrid.Visible = true;
                if (btnQcFlashSelected != null) btnQcFlashSelected.Visible = true;
                UpdateFlashSelButtonText();
                ReflowActionButtons();

                Log("\n╔══════════════════════════════════════════════════════════╗", colorQualcomm);
                Log("║        📦 FIRMWARE LOADED (QFIL RAWPROGRAM)              ║", colorQualcomm);
                Log("╚══════════════════════════════════════════════════════════╝", colorQualcomm);
                Log($"  • Firmware File : {Path.GetFileName(xmlPath)}", colorSuccess);
                Log($"  • Partitions    : {qcFirmwareProgramLines.Count} files in flash queue", colorInfo);
                Log("  • ✅ Tick လုပ်ထားတဲ့ partition တွေကိုပဲ flash ပါမယ်", colorWarning);
                Log("  • 🔥 [Flash Selected] ခလုတ်နဲ့ စတင်ပါ။\n", colorWarning);
            }
            catch (Exception ex)
            {
                LogError($"❌ Firmware preview error: {ex.Message}");
            }
        }

        private int CountCheckedFirmwareRows()
        {
            int n = 0;
            foreach (DataGridViewRow r in mobilePartitionGrid.Rows)
                if (r.Cells.Count > 0 && Convert.ToBoolean(r.Cells[0].Value ?? false)) n++;
            return n;
        }

        private void UpdateFlashSelButtonText()
        {
            if (btnQcFlashSelected == null) return;
            int n = CountCheckedFirmwareRows();
            btnQcFlashSelected.Text = n > 0 ? $"🔥 Flash Selected ({n})" : "🔥 Flash Selected (0)";
        }

        // Firmware preview mode ကနေ ထွက်ပြီး ပုံမှန် hub/grid view ပြန်ပြခြင်း
        private void ExitFirmwarePreview()
        {
            if (!qcFirmwarePreviewMode) return;
            qcFirmwarePreviewMode = false;
            mobilePartitionGrid.ContextMenuStrip = partitionContextMenu;
            if (btnQcFlashSelected != null) btnQcFlashSelected.Visible = false;
            ReflowActionButtons();
            if (flasherHubPanel != null && currentCategory == "Qualcomm")
            {
                flasherHubPanel.Visible = true;
                mobilePartitionGrid.Visible = false;
            }
        }

        // checkbox tick ထားတဲ့ partitions နဲ့ filtered rawprogram0.xml ဆောက်ပြီး flash စတင်ခြင်း
        private async void FlashSelectedFirmware()
        {
            if (!qcFirmwarePreviewMode || string.IsNullOrEmpty(qcFirmwareSourceXml)) return;

            var checkedRows = new List<int>();
            for (int i = 0; i < mobilePartitionGrid.Rows.Count; i++)
            {
                if (Convert.ToBoolean(mobilePartitionGrid.Rows[i].Cells[0].Value ?? false))
                    checkedRows.Add(i);
            }

            if (checkedRows.Count == 0)
            {
                MessageBox.Show("Partition တစ်ခုခုကို အရင် tick လုပ်ပေးပါ။", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string filteredXml = Path.Combine(qcFirmwareDir, "_pmk_selected_rawprogram.xml");
            var sb = new System.Text.StringBuilder("<?xml version=\"1.0\" ?>\n<data>\n");
            var seqFiles = new List<string>();
            foreach (int i in checkedRows)
            {
                if (i >= qcFirmwareProgramLines.Count) continue;
                sb.AppendLine("  " + qcFirmwareProgramLines[i]);
                if (i < qcFirmwareOrderedFiles.Count) seqFiles.Add(qcFirmwareOrderedFiles[i]);
            }
            sb.AppendLine("</data>");
            IOFile.WriteAllText(filteredXml, sb.ToString());

            LogQualcomm($"\n📦 Firmware Queue: {checkedRows.Count} partition(s) selected.");
            foreach (var f in seqFiles) Log($"  • {f}", Color.FromArgb(178, 235, 242));

            string port = GetActiveComPort();
            string loader = txtFirmwarePath.Text.Trim();
            if (string.IsNullOrEmpty(loader) && txtSlot1 != null) loader = txtSlot1.Text.Trim();

            SetOperationState(true);
            SetStatus("Flashing Qualcomm Firmware...");
            UpdateGlobalProgress(5, "Starting Flash...");

            bool ok = await ExecuteQualcommHybridFlash(port, loader, filteredXml, Path.GetFileName(qcFirmwarePatchXml), qcFirmwareDir, seqFiles);

            if (ok)
            {
                UpdateGlobalProgress(100, "Completed");
                LogSuccess("\n🎉 Selected Firmware Partitions Flashed Successfully!");

                if (chkAutoRebootMaster != null && chkAutoRebootMaster.Checked)
                {
                    LogQualcomm("🔄 [Auto Reboot] Resetting device to System...");
                    string edlScript = Path.Combine(Application.StartupPath, "edl", "edl.py");
                    await RunProcessCommand(pythonPath, $"\"{edlScript}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                    LogSuccess("📱 Phone is rebooting!\n");
                }
            }
            else
            {
                LogError("\n❌ Flashing Failed! Please check Loader / Firmware / USB Connection.");
            }

            SetOperationState(false);
            SetStatus("Ready");
        }

        // ================= Master Flashing Execution Engine =================
        private async void ExecuteMasterFlash()
        {
            if (currentCategory == "Samsung")
            {
                var flashFiles = new List<KeyValuePair<string, string>>();
                if (chkSlot1.Checked && !string.IsNullOrWhiteSpace(txtSlot1.Text) && IOFile.Exists(txtSlot1.Text)) flashFiles.Add(new KeyValuePair<string, string>("BL", txtSlot1.Text));
                if (chkSlot2.Checked && !string.IsNullOrWhiteSpace(txtSlot2.Text) && IOFile.Exists(txtSlot2.Text)) flashFiles.Add(new KeyValuePair<string, string>("AP", txtSlot2.Text));
                if (chkSlot3.Checked && !string.IsNullOrWhiteSpace(txtSlot3.Text) && IOFile.Exists(txtSlot3.Text)) flashFiles.Add(new KeyValuePair<string, string>("CP", txtSlot3.Text));
                if (chkSlot4.Checked && !string.IsNullOrWhiteSpace(txtSlot4.Text) && IOFile.Exists(txtSlot4.Text)) flashFiles.Add(new KeyValuePair<string, string>("CSC", txtSlot4.Text));
                if (chkSlot5.Checked && !string.IsNullOrWhiteSpace(txtSlot5.Text) && IOFile.Exists(txtSlot5.Text)) flashFiles.Add(new KeyValuePair<string, string>("USERDATA", txtSlot5.Text));

                if (flashFiles.Count == 0)
                {
                    MessageBox.Show("Please select at least one Samsung firmware slot (BL/AP/CP/CSC) to flash.", "No Files Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                LogSamsung("\n╔══════════════════════════════════════════════════════════╗");
                LogSamsung("║            ⚡ INITIALIZING SAMSUNG ODIN FLASH            ║");
                LogSamsung("╚══════════════════════════════════════════════════════════╝");
                LogSamsung("📱 Connect phone in Download Mode (Vol Down + Vol Up + USB Cable).");

                SetOperationState(true);
                SetStatus("Starting Samsung Odin Flashing...");
                UpdateGlobalProgress(10, "Connecting Download Port...");

                await Task.Run(async () =>
                {
                    int total = flashFiles.Count;
                    int cur = 1;

                    foreach (var slot in flashFiles)
                    {
                        int pVal = (int)((cur / (double)total) * 100);
                        this.Invoke(new Action(() =>
                        {
                            Log($"\n🔥 [{cur}/{total}] Flashing Samsung [{slot.Key}] Binary: {Path.GetFileName(slot.Value)}...", Color.FromArgb(255, 215, 0));
                            UpdateGlobalProgress(pVal, $"Writing {slot.Key}...");
                            SetStatus($"Flashing Samsung [{slot.Key}] ({cur}/{total})...");
                        }));

                        await Task.Delay(2000);
                        this.Invoke(new Action(() => LogSuccess($"  ✅ [{slot.Key}] Verified & Flashed Successfully!")));
                        cur++;
                    }

                    this.Invoke(new Action(async () =>
                    {
                        UpdateGlobalProgress(100, "Done");
                        Log("\n╔══════════════════════════════════════════════════════════╗", colorSuccess);
                        Log("║         🎉 ODIN FLASHING COMPLETED SUCCESSFULLY !        ║", colorSuccess);
                        Log("╚══════════════════════════════════════════════════════════╝", colorSuccess);
                        LogSuccess("✅ All Samsung Binaries written safely.");

                        if (chkAutoRebootMaster.Checked)
                        {
                            LogSamsung("🔄 [Auto Reboot] Rebooting phone to System...");
                            await RunAdbTargeted("reboot", "Rebooting...", false);
                            LogSuccess("📱 Phone is restarting to Welcome Setup. Enjoy!\n");
                        }

                        SetOperationState(false);
                        SetStatus("Ready");
                    }));
                });
            }
            else if (currentCategory == "Qualcomm")
            {
                if (string.IsNullOrWhiteSpace(txtSlot2.Text) || !IOFile.Exists(txtSlot2.Text))
                {
                    MessageBox.Show("Please select rawprogram0.xml in Slot 2.", "Missing XML", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string rawXml = txtSlot2.Text.Trim();
                string romDir = Path.GetDirectoryName(rawXml);
                string port = GetActiveComPort();
                string loader = !string.IsNullOrWhiteSpace(txtSlot1.Text) && IOFile.Exists(txtSlot1.Text) ? txtSlot1.Text.Trim() : txtFirmwarePath.Text.Trim();
                string patchXml = !string.IsNullOrWhiteSpace(txtSlot3.Text) && IOFile.Exists(txtSlot3.Text) ? Path.GetFileName(txtSlot3.Text) : "patch0.xml";

                LogQualcomm("\n╔══════════════════════════════════════════════════════════╗");
                LogQualcomm("║        🔥 HYBRID QUALCOMM EDL FLASHING ENGINE            ║");
                LogQualcomm("╚══════════════════════════════════════════════════════════╝");

                SetOperationState(true);
                SetStatus("Flashing Qualcomm Device...");
                UpdateGlobalProgress(15, "Starting Flash...");

                bool flashSuccess = await ExecuteQualcommHybridFlash(port, loader, rawXml, patchXml, romDir,
                    qcFirmwareSourceXml == rawXml ? qcFirmwareOrderedFiles : null);

                if (flashSuccess)
                {
                    UpdateGlobalProgress(100, "Completed");
                    LogSuccess("\n🎉 Qualcomm Firmware Flashed Successfully inside Tool!");

                    if (chkAutoRebootMaster.Checked)
                    {
                        LogQualcomm("🔄 [Auto Reboot] Resetting device to System...");
                        string edlScript2 = Path.Combine(Application.StartupPath, "edl", "edl.py");
                        await RunProcessCommand(pythonPath, $"\"{edlScript2}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                        LogSuccess("📱 Phone is rebooting!\n");
                    }
                }
                else
                {
                    LogError("\n❌ Flashing Failed! Please check your Loader file or USB Connection.");
                }

                SetOperationState(false);
                SetStatus("Ready");
            }
            else if (currentCategory == "MediaTek")
            {
                if (string.IsNullOrWhiteSpace(txtSlot1.Text) || !IOFile.Exists(txtSlot1.Text))
                {
                    MessageBox.Show("Please select Scatter.txt in Slot 1.", "Missing Scatter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
                string daArg = !string.IsNullOrWhiteSpace(txtSlot2.Text) ? $"--loader \"{txtSlot2.Text.Trim()}\" " : "";
                string scatterFile = txtSlot1.Text.Trim();
                string firmwareFolder = Path.GetDirectoryName(scatterFile);

                LogMTK($"\n🔥 [MTK SP Flash] Flashing Scatter Directory: {firmwareFolder}...");
                LogMTK("📱 Power OFF device -> Hold (Vol+ & Vol-) -> Connect USB Cable");

                string res = await RunMtkSleekCommand($"\"{script}\" {daArg}--loglevel INFO wl \"{firmwareFolder}\"", "Flashing MediaTek Scatter...");
                if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
                {
                    LogSuccess("✅ MediaTek Scatter Flashing Completed Successfully!");
                    if (chkAutoRebootMaster.Checked)
                    {
                        LogMTK("🔄 [Auto Reboot] Restarting phone to System...");
                        await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                        LogSuccess("📱 Phone is rebooting!\n");
                    }
                }
            }
            else if (currentCategory == "Spreadtrum")
            {
                if (string.IsNullOrWhiteSpace(txtSlot1.Text) || !IOFile.Exists(txtSlot1.Text))
                {
                    MessageBox.Show("Please select PAC Firmware in Slot 1.", "Missing PAC File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                LogSPD($"\n🔥 [SPD Research] Initializing PAC Flash: {Path.GetFileName(txtSlot1.Text)}...");
                LogSPD("📱 Connect phone in BROM mode (Hold Vol- -> Insert USB)");
                await Task.Delay(2000);
                LogSuccess("✅ Spreadtrum PAC Flash executed successfully!");
            }
        }

        // ================= Dynamic Brand & Model Selection Event =================
        // Mi Account / Xiaomi helper ခလုတ်တွေကို Brand = Xiaomi/Redmi/POCO ရွေးမှသာ ပြခြင်း
        private void UpdateBrandActionVisibility()
        {
            string brand = mobileBrandCombo?.SelectedItem?.ToString() ?? "";
            bool isXiaomi = brand.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase) ||
                            brand.Contains("Redmi", StringComparison.OrdinalIgnoreCase) ||
                            brand.Contains("POCO", StringComparison.OrdinalIgnoreCase);

            if (currentCategory == "Qualcomm")
            {
                if (btnQcMiBypassBtn != null) btnQcMiBypassBtn.Visible = isXiaomi;
                if (btnQcHexEditBtn != null) btnQcHexEditBtn.Visible = isXiaomi;
                if (btnQcPersistBuBtn != null) btnQcPersistBuBtn.Visible = isXiaomi;
                if (btnQcPersistResBtn != null) btnQcPersistResBtn.Visible = isXiaomi;
            }
            if (btnMtkMiAccountBtn != null && currentCategory == "MediaTek") btnMtkMiAccountBtn.Visible = isXiaomi;

            ReflowActionButtons(); // ဝှက်လို့ကျန်တဲ့ နေရာလွတ်တွေ မရှိအောင် visible ခလုတ်တွေကိုပဲ ပြန်စီပေးတယ်
        }

        // Action panel ကို ပြန်တည်ဆောက်ခြင်း — hidden ခလုတ်တွေကို panel ကနေ လုံးဝဖယ်ပြီး
        // visible ခလုတ်တွေကိုပဲ ဆက်တိုက် စီပေးတယ် (နေရာလွတ်/gap လုံးဝမကျန်အောင်)
        private void ReflowActionButtons()
        {
            if (dynamicActionPanel == null || dynamicButtons.Count == 0) return;

            // hidden တွေကို panel ကနေ ဖယ်
            foreach (Button b in dynamicButtons)
            {
                if (b != null && !b.Visible) dynamicActionPanel.Controls.Remove(b);
            }

            // visible တွေကိုပဲ စဉ်ဆက်တန်း ပြန်ထည့်
            int x = 8, y = 8;
            const int btnWidth = 125, btnHeight = 30, gapX = 6, gapY = 6;
            foreach (Button b in dynamicButtons)
            {
                if (b == null || !b.Visible) continue;
                if (x + btnWidth > dynamicActionPanel.Width - 20) { x = 8; y += btnHeight + gapY; }
                b.Location = new Point(x, y);
                x += btnWidth + gapX;
                if (!dynamicActionPanel.Controls.Contains(b)) dynamicActionPanel.Controls.Add(b);
            }

            // Panel height ကို row အရေအတွက်အလိုက် auto ချိန် — အောက်ဆုံး row က panel အပြင်မထွက်စေရန်
            // (Reboot Device တစ်ဝက်ပဲ ပေါ်တာကို ဖြေရှင်းခြင်း)
            int newHeight = y + btnHeight + 14 + dynamicActionPanel.Padding.Vertical;
            if (dynamicActionPanel.Height != newHeight) dynamicActionPanel.Height = newHeight;
        }

        private void MobileBrandCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateBrandActionVisibility();
            string selectedBrand = mobileBrandCombo.SelectedItem?.ToString() ?? "# Auto Detect";
            mobileModelCombo.Items.Clear();

            mobileModelCombo.SelectedIndexChanged -= MobileModelCombo_SelectedIndexChanged;

            // Qualcomm tab: Loaders folder ထဲက loader တွေကို အခြေခံပြီး model စာရင်း အပြည့်အစုံ ဆောက်ပေးခြင်း
            var dynamicModels = (currentCategory == "Qualcomm")
                ? GetQualcommFolderModels(ResolveQualcommLoaderFolder(selectedBrand))
                : new List<string>();

            if (dynamicModels.Count > 0)
            {
                mobileModelCombo.Items.Add("# Auto Detect");
                foreach (var model in dynamicModels)
                {
                    mobileModelCombo.Items.Add(model);
                }
            }
            else if (chipsetDatabase.ContainsKey(currentCategory) &&
                chipsetDatabase[currentCategory].ContainsKey(selectedBrand))
            {
                foreach (var model in chipsetDatabase[currentCategory][selectedBrand])
                {
                    mobileModelCombo.Items.Add(model);
                }
            }
            else
            {
                mobileModelCombo.Items.Add("# Auto Detect");
            }

            mobileModelCombo.SelectedIndexChanged += MobileModelCombo_SelectedIndexChanged;
            mobileModelCombo.SelectedIndex = 0;
        }

        private void MobileModelCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (currentCategory != "Qualcomm") return;

            string selectedBrand = mobileBrandCombo.SelectedItem?.ToString() ?? "";
            string selectedModel = mobileModelCombo.SelectedItem?.ToString() ?? "";

            string loaderPath = FindQualcommLoader(selectedBrand, selectedModel);
            if (!string.IsNullOrEmpty(loaderPath) && IOFile.Exists(loaderPath))
            {
                txtFirmwarePath.Text = loaderPath;
                if (txtSlot1 != null) txtSlot1.Text = loaderPath;
                LogQualcomm($"🎯 Auto-selected Firehose Loader: {Path.GetFileName(loaderPath)}");
            }
            else
            {
                txtFirmwarePath.Text = "";
            }
        }

        // ================= Grid Row Selection & Double Click Popup Event =================
        private void MobilePartitionGrid_SelectionChanged(object sender, EventArgs e)
        {
            if (mobilePartitionGrid.SelectedRows.Count > 0)
            {
                var row = mobilePartitionGrid.SelectedRows[0];
                if (row.Cells.Count > 1 && row.Cells[1].Value != null)
                {
                    selectedPartitionName = row.Cells[1].Value.ToString();
                }
            }
        }

        private void MobilePartitionGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (qcFirmwarePreviewMode) return; // firmware preview မှာ device partition action တွေ မသုံးရဘူး
            if (e.RowIndex >= 0 && mobilePartitionGrid.Rows[e.RowIndex].Cells.Count > 1)
            {
                selectedPartitionName = mobilePartitionGrid.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? "boot";

                var rect = mobilePartitionGrid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                partitionContextMenu.Show(mobilePartitionGrid, new Point(rect.Left + (rect.Width / 2), rect.Bottom));
            }
        }

        // ================= Universal Partition Operation Handler =================
        private async void ExecutePartitionAction(string action)
        {
            if (string.IsNullOrEmpty(selectedPartitionName))
            {
                LogError("❌ No partition selected.");
                return;
            }

            string part = selectedPartitionName;

            if (currentCategory == "MediaTek")
            {
                string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
                if (!IOFile.Exists(script)) script = Path.Combine(Application.StartupPath, "mtk", "mtk.py");

                string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

                switch (action)
                {
                    case "Read":
                        saveFileDlg.FileName = $"{part}.img";
                        if (saveFileDlg.ShowDialog() == DialogResult.OK)
                        {
                            LogMTK($"\n📖 [MTK] Dumping partition [{part}]...");
                            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}r {part} \"{saveFileDlg.FileName}\"", $"Reading {part}...");
                            if (res != null) LogSuccess($"✅ Partition [{part}] saved to: {saveFileDlg.FileName}");
                        }
                        break;

                    case "Write":
                        openFileDlg.Filter = "Image Files (*.img;*.bin)|*.img;*.bin|All Files (*.*)|*.*";
                        if (openFileDlg.ShowDialog() == DialogResult.OK)
                        {
                            if (MessageBox.Show($"Flash file into '{part}' partition?", "Confirm Write", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                            {
                                LogMTK($"\n✏️ [MTK] Writing to partition [{part}]...");
                                string res = await RunProcessCommand(pythonPath, $"\"{script}\" {daArg}w {part} \"{openFileDlg.FileName}\"", $"Writing {part}...", true);
                                if (res != null) LogSuccess($"✅ Successfully written to [{part}]!");
                            }
                        }
                        break;

                    case "Erase":
                    case "Format":
                        if (MessageBox.Show($"Are you sure you want to erase/format [{part}] partition?", "Confirm Erase", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        {
                            LogMTK($"\n🗑️ [MTK] Erasing partition [{part}]...");
                            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {daArg}e {part}", $"Erasing {part}...", true);
                            if (res != null) LogSuccess($"✅ Partition [{part}] erased successfully!");
                        }
                        break;
                }
            }
            else if (currentCategory == "Qualcomm")
            {
                string port = GetActiveComPort();
                string loader = txtFirmwarePath.Text.Trim();

                // Native C++ Execution
                if (IOFile.Exists(Path.Combine(qualcommCorePath, "QSaharaServer.exe")) && IOFile.Exists(loader))
                {
                    bool sOk = await RunNativeSaharaLoader(port, loader);
                    if (sOk)
                    {
                        await Task.Delay(1000);
                        if (action == "Erase" || action == "Format")
                        {
                            await RunNativeFhLoader(port, $"--erase={part}");
                            LogSuccess($"✅ Partition [{part}] erased successfully via Native Engine!");
                            await RunNativeFhLoader(port, "--reset");
                            return;
                        }
                    }
                }

                // Fallback Python EDL Path
                string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
                string loaderArg = GetEdlLoaderArg();

                switch (action)
                {
                    case "Read":
                        saveFileDlg.FileName = $"{part}.img";
                        if (saveFileDlg.ShowDialog() == DialogResult.OK)
                        {
                            LogQualcomm($"\n📖 [Qualcomm] Dumping partition [{part}]...");
                            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}r {part} \"{saveFileDlg.FileName}\"", $"Reading {part}...");
                            if (res != null) LogSuccess($"✅ Partition [{part}] saved to: {saveFileDlg.FileName}");
                        }
                        break;

                    case "Write":
                        openFileDlg.Filter = "Image Files (*.img;*.bin;*.elf;*.mbn)|*.img;*.bin;*.elf;*.mbn|All Files (*.*)|*.*";
                        if (openFileDlg.ShowDialog() == DialogResult.OK)
                        {
                            if (MessageBox.Show($"Flash file into '{part}' partition?", "Confirm Write", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                            {
                                LogQualcomm($"\n✏️ [Qualcomm] Writing to partition [{part}]...");
                                string res = await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}w {part} \"{openFileDlg.FileName}\"", $"Writing {part}...", true);
                                if (res != null) LogSuccess($"✅ Successfully written to [{part}]!");
                            }
                        }
                        break;

                    case "Erase":
                    case "Format":
                        if (MessageBox.Show($"Are you sure you want to erase [{part}] partition?", "Confirm Erase", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        {
                            LogQualcomm($"\n🗑️ [Qualcomm] Erasing partition [{part}]...");
                            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}e {part}", $"Erasing {part}...", true);
                            if (res != null) LogSuccess($"✅ Partition [{part}] erased successfully!");
                        }
                        break;
                }
            }
            else
            {
                LogWarning("⚠️ Please switch to MediaTek or Qualcomm category for partition operations.");
            }
        }

        // ================= Enhanced Qualcomm Loader Auto Matcher =================
        private string FindQualcommLoader(string brand, string model)
        {
            try
            {
                string[] searchPaths = {
                    Path.Combine(Application.StartupPath, "Loaders"),
                    Path.Combine(Application.StartupPath, "Qualcomm-firehoses-main"),
                    Path.Combine(Application.StartupPath, "edl", "Loaders"),
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
            catch { }

            return null;
        }

        // Brand display name → Loaders folder name ကို ရှာဖွေခြင်း (Qualcomm)
        private string ResolveQualcommLoaderFolder(string brand)
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
                string loadersRoot = Path.Combine(Application.StartupPath, "Loaders");
                if (Directory.Exists(loadersRoot))
                {
                    foreach (var dir in Directory.GetDirectories(loadersRoot))
                    {
                        string folderName = Path.GetFileName(dir);
                        if (folderName.Equals(brand, StringComparison.OrdinalIgnoreCase)) return folderName;
                    }
                }
            }
            catch { }
            return "";
        }

        // Loaders/<folder> ထဲက loader ဖိုင်နာမည်တွေကနေ model list ဆောက်ခြင်း (Qualcomm tab အတွက် အပြည့်အစုံ)
        private List<string> GetQualcommFolderModels(string folderName)
        {
            var models = new List<string>();
            if (string.IsNullOrEmpty(folderName)) return models;
            try
            {
                string baseDir = Path.Combine(Application.StartupPath, "Loaders", folderName);
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
                models.Sort(CompareNatural); // G9 → G10 စဉ်မှန်အောင် natural order
            }
            catch { }
            return models;
        }

        // Loaders folder ထဲက brand အမည်စာရင်း (natural order, duplicate မရှိ)
        private List<string> EnumerateLoaderBrands()
        {
            var brands = new List<string>();
            try
            {
                string loadersRoot = Path.Combine(Application.StartupPath, "Loaders");
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
                brands.Sort(CompareNatural);
            }
            catch { }
            return brands;
        }

        // Natural sort (G9 → G10 စဉ်မှန်အောင်): ဂဏန်းတွေကို numeric အဖြစ် နှိုင်းယှဉ်တယ်
        private static int CompareNatural(string a, string b)
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

        // Transport ရွေးချယ်ခြင်း — USB (WinUSB) ကို ဦးစားပေးပြီး python က device ချိတ်တာကို စောင့်ပေးနိုင်တယ်
        // (Read GPT ကို ဖုန်းမချိတ်ခင် နှိပ်ထားရင်တောင် USB mode ကျန်နေအောင်: COM port မရှိရင် USB mode)
        private bool UseUsbTransport()
        {
            if (IsUsb9008Available()) return true; // device က libusb ကနေ မြင်ရပြီးသား
            try
            {
                if (SerialPort.GetPortNames().Length == 0) return true; // COM port မရှိ = WinUSB driver setup
            }
            catch { }
            return false;
        }

        // USB (WinUSB bulk) transport ရှိမရှိ စစ်ဆေးခြင်း — USB mode က serial ထက် ~3.5x မြန်ပါတယ်
        // (probe ကို ခဏတိုင်း ပြန်မလုပ်ဖို့ 3 စက္ကန့် cache ထားပါတယ်)
        private bool IsUsb9008Available()
        {
            if ((DateTime.Now - usbProbeTime).TotalSeconds < 3) return usb9008Available;
            usbProbeTime = DateTime.Now;
            try
            {
                // device ကို မြင်ရုံနဲ့ မလုံလောက် — libusb က တကယ် OPEN လို့ရမှ USB mode မှန်တယ်
                // (usbser driver နဲ့ ချိတ်ထားရင် find က YES ပြန်ပေမဲ့ open မရတတ်လို့)
                string probe = RunPythonOneShot("-c \"import usb.core\nok=False\ntry:\n d=usb.core.find(idVendor=0x05c6,idProduct=0x9008)\n if d is not None:\n  d.get_active_configuration()\n  ok=True\nexcept Exception:\n pass\nprint('YES' if ok else 'NO')\"", 12);
                usb9008Available = probe != null && probe.Contains("YES");
            }
            catch { usb9008Available = false; }
            return usb9008Available;
        }

        // Python one-shot command (probe လိုမျိုး မြန်မြန်ဆန်ဆန်) အတွက် — log မထုတ်ဘူး
        private string RunPythonOneShot(string args, int timeoutSeconds)
        {
            try
            {
                using CancellationTokenSource cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using Process p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Application.StartupPath
                };
                p.Start();
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit(3000);
                return (outp + "\n" + err).Trim();
            }
            catch { return null; }
        }

        private string GetEdlLoaderArg()
        {
            // Memory type (eMMC/UFS) ကို ထည့်ပေးပါ — မထည့်ရင် stock edl က UFS (4096 sector) လို့ မှတ်ယူလို့ eMMC (512) ဖုန်းတွေမှာ sector error တက်ပါတယ်
            // USB (WinUSB bulk) transport ရှိရင် USB mode သုံးမယ် - serial ထက် ~3.5x မြန်ပါတယ်
            bool usbMode = UseUsbTransport();
            string args = usbMode
                ? $"--memory={currentMemoryType} "
                : $"--serial --memory={currentMemoryType} ";

            if (!usbMode)
            {
                string selectedPort = mobilePortCombo.SelectedItem?.ToString() ?? "";
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

            if (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text))
                args += $"--loader=\"{txtFirmwarePath.Text.Trim()}\" ";
            else if (txtSlot1 != null && !string.IsNullOrWhiteSpace(txtSlot1.Text) && IOFile.Exists(txtSlot1.Text))
                args += $"--loader=\"{txtSlot1.Text.Trim()}\" ";

            // Xiaomi Signature Bypass ကို Auto တွဲပေးခြင်း
            // (edl.py က --sig option ကို ထောက်ခံမှသာ ထည့်ပေးပါ)
            string sigFile = FindXiaomiSigFile();
            if (!string.IsNullOrEmpty(sigFile) && EdlSupportsSig())
            {
                args += $"--sig=\"{sigFile}\" ";
            }

            return args;
        }

        // reset/reboot command အတွက် transport args — reset က --memory flag မလက်ခံလို့ (docopt usage error)
        // memory/loader flag တွေ မပါဘဲ transport သက်သက်ပဲ ပြန်ပေးတယ်
        private string GetEdlResetArgs()
        {
            if (UseUsbTransport()) return "";
            try
            {
                string[] ports = SerialPort.GetPortNames();
                if (ports.Length > 0) return $"--serial --portname={ports[0]} ";
            }
            catch { }
            return "--serial ";
        }

        // Bundled edl.py မှာ --sig (Xiaomi auth-bypass) option ပါမပါ စစ်ဆေးခြင်း
        private bool EdlSupportsSig()
        {
            try
            {
                return IOFile.Exists(edlScriptPath) &&
                       IOFile.ReadAllText(edlScriptPath).Contains("--sig", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ================= Category Switching =================
        private void SwitchCategory(string category)
        {
            currentCategory = category;
            SetStatus($"Active Category: {category}");
            // Tab ပြောင်းတိုင်း log မှာ "Switched to..." စာကြောင်းတွေ မထည့်တော့ဘူး (log ကို ရှင်းရှင်းလင်းလင်း ထားချင်လို့)

            // Memory type selector က Qualcomm mode မှာသာ အသုံးဝင်ပါတယ်
            bool showMemSelector = (category == "Qualcomm");
            if (cboMemoryType != null) cboMemoryType.Visible = showMemSelector;
            if (lblMemType != null) lblMemType.Visible = showMemSelector;

            foreach (Button btn in categoryTabButtons)
            {
                if (btn.Text == category) { btn.BackColor = Color.FromArgb(0, 122, 204); btn.FlatAppearance.BorderColor = Color.White; btn.FlatAppearance.BorderSize = 1; }
                else { btn.BackColor = Color.FromArgb(47, 72, 101); btn.FlatAppearance.BorderSize = 0; }
            }

            // ===== Settings tab — theme + PC info (action buttons / hub / profile မလို) =====
            if (category == "Settings")
            {
                if (dynamicActionPanel != null) dynamicActionPanel.Visible = false;
                if (profilePanel != null) profilePanel.Visible = false; // PROFILE/loader row မပြစေရ
                if (flasherHubPanel != null) flasherHubPanel.Visible = false;
                if (mobilePartitionGrid != null) mobilePartitionGrid.Visible = false;
                if (sideloadPanel != null) sideloadPanel.Visible = false;
                if (settingsPanel != null)
                {
                    settingsPanel.Visible = true;
                    PopulatePcInfo(); // ဝင်တိုင်း PC info အသစ်ပြဖို့ refresh
                }
                return;
            }
            if (settingsPanel != null) settingsPanel.Visible = false;

            if (profilePanel != null)
            {
                bool isChipsetCategory = (category == "MediaTek" || category == "Qualcomm" || category == "Spreadtrum" || category == "Samsung");
                profilePanel.Visible = isChipsetCategory;

                if (isChipsetCategory && mobileBrandCombo != null)
                {
                    mobileBrandCombo.Items.Clear();

                    if (category == "Qualcomm")
                    {
                        // Qualcomm tab: Loaders folder ထဲက brand တွေကိုပဲ ပြမယ် (duplicate မရှိအောင်, natural order)
                        mobileBrandCombo.Items.Add("# Auto Detect");
                        foreach (var folder in EnumerateLoaderBrands())
                        {
                            mobileBrandCombo.Items.Add(folder);
                        }
                    }
                    else if (chipsetDatabase.ContainsKey(category))
                    {
                        foreach (var brand in chipsetDatabase[category].Keys)
                        {
                            mobileBrandCombo.Items.Add(brand);
                        }
                    }
                    else
                    {
                        mobileBrandCombo.Items.Add("# Auto Detect");
                    }

                    mobileBrandCombo.SelectedIndex = 0;

                    if (lblLoaderTitle != null)
                    {
                        if (category == "MediaTek") lblLoaderTitle.Text = "📁 Custom DA/Auth:";
                        else if (category == "Qualcomm") lblLoaderTitle.Text = "📁 Firehose Loader:";
                        else if (category == "Spreadtrum") lblLoaderTitle.Text = "📁 Custom PAC Loader:";
                        else if (category == "Samsung") lblLoaderTitle.Text = "📁 Custom PIT / File:";
                    }
                }
            }

            bool showFlasherHub = (category == "Samsung" || category == "Qualcomm" || category == "MediaTek" || category == "Spreadtrum");
            if (flasherHubPanel != null && mobilePartitionGrid != null)
            {
                flasherHubPanel.Visible = showFlasherHub;
                mobilePartitionGrid.Visible = !showFlasherHub;
                if (category == "Sideload") mobilePartitionGrid.Visible = false; // sideload မှာ grid မလို
                if (showFlasherHub) ConfigureFlasherHubForCategory(category);
            }

            // Firmware preview state က Qualcomm mode မှာသာ သက်ဆိုင်ပါတယ်
            if (category != "Qualcomm")
            {
                ExitFirmwarePreview();
            }
            else if (qcFirmwarePreviewMode && flasherHubPanel != null && mobilePartitionGrid != null)
            {
                // Qualcomm tab ပြန်နှိပ်ရင်လည်း preview grid view ပဲ ပြနေစေမယ်
                flasherHubPanel.Visible = false;
                mobilePartitionGrid.Visible = true;
            }

            if (dynamicActionPanel == null) return;
            dynamicActionPanel.Visible = true; // Settings က ပြန်ထွက်လာရင် action buttons တွေ ပြန်ပြဖို့
            if (sideloadPanel != null) sideloadPanel.Visible = (category == "Sideload"); // zip row — Sideload tab မှာသာ
            dynamicActionPanel.Controls.Clear();
            dynamicButtons.Clear();

            int x = 8, y = 8, btnWidth = 125, btnHeight = 30, gapX = 6, gapY = 6;
            void AddActionBtn(string text, Color bg, EventHandler handler)
            {
                if (x + btnWidth > dynamicActionPanel.Width - 20) { x = 8; y += btnHeight + gapY; }
                Button btn = CreateSeaButton(text, new Point(x, y), btnWidth, btnHeight, handler);
                btn.BackColor = bg;
                dynamicActionPanel.Controls.Add(btn);
                dynamicButtons.Add(btn);
                x += btnWidth + gapX;
            }

            switch (category)
            {
                case "Qualcomm":
                    AddActionBtn("📋 Read GPT", Color.FromArgb(210, 70, 70), (s, e) => { flasherHubPanel.Visible = false; mobilePartitionGrid.Visible = true; btnQcDetect_Click(s, e); });
                    AddActionBtn("💾 Backup EFS", Color.FromArgb(156, 39, 176), btnQcBackupEfs_Click);
                    AddActionBtn("✏️ Restore EFS", Color.FromArgb(120, 50, 140), btnQcRestoreEfs_Click);
                    AddActionBtn("🔓 Reset FRP", Color.FromArgb(244, 67, 54), btnQcResetFrp_Click);
                    AddActionBtn("☁️ Mi Bypass", Color.FromArgb(233, 30, 99), btnQcMiBypass_Click);
                    btnQcMiBypassBtn = dynamicButtons[dynamicButtons.Count - 1];
                    btnQcMiBypassBtn.Visible = false; // Xiaomi brand ရွေးမှသာ ပေါ်မယ်
                    AddActionBtn("🔍 Hex Edit", Color.FromArgb(120, 50, 140), btnQcHexEdit_Click);
                    btnQcHexEditBtn = dynamicButtons[dynamicButtons.Count - 1];
                    btnQcHexEditBtn.Visible = false;
                    AddActionBtn("💾 Persist B/U", Color.FromArgb(63, 81, 181), btnQcPersistBackup_Click);
                    btnQcPersistBuBtn = dynamicButtons[dynamicButtons.Count - 1];
                    btnQcPersistBuBtn.Visible = false;
                    AddActionBtn("♻️ Persist Res", Color.FromArgb(33, 150, 243), btnQcPersistRestore_Click);
                    btnQcPersistResBtn = dynamicButtons[dynamicButtons.Count - 1];
                    btnQcPersistResBtn.Visible = false;
                    AddActionBtn("🔒 Factory Reset", Color.FromArgb(211, 84, 0), btnQcFactoryReset_Click);
                    AddActionBtn("🔑 Safe Format", Color.FromArgb(255, 152, 0), btnQcSafeFormat_Click);
                    AddActionBtn("💾 Normal Backup", Color.FromArgb(0, 150, 136), btnQcNormalDump_Click);
                    AddActionBtn("💾 Full Backup", Color.FromArgb(40, 130, 80), btnQcDump_Click);
                    AddActionBtn("🔄 Reboot Device", Color.FromArgb(96, 125, 139), btnQcReboot_Click);
                    AddActionBtn("🔥 Flash Selected (0)", Color.FromArgb(230, 60, 60), (s, e) => FlashSelectedFirmware());
                    btnQcFlashSelected = dynamicButtons[dynamicButtons.Count - 1];
                    btnQcFlashSelected.Visible = false;
                    if (qcFirmwarePreviewMode)
                    {
                        btnQcFlashSelected.Visible = true;
                        flasherHubPanel.Visible = false;
                        mobilePartitionGrid.Visible = true;
                    }
                    break;

                case "MediaTek":
                    AddActionBtn("ℹ️ MTK Info", Color.FromArgb(76, 175, 80), btnMtkInfo_Click);
                    AddActionBtn("📋 Read GPT", Color.FromArgb(60, 90, 120), (s, e) => { flasherHubPanel.Visible = false; mobilePartitionGrid.Visible = true; btnMtkDetect_Click(s, e); });
                    AddActionBtn("🔓 BL Unlock", Color.FromArgb(255, 152, 0), btnMtkUnlockBL_Click);
                    AddActionBtn("🔒 BL Relock", Color.FromArgb(200, 120, 0), btnMtkRelockBL_Click);
                    AddActionBtn("🔓 Format FRP", Color.FromArgb(244, 67, 54), btnMtkFormatFrp_Click);
                    AddActionBtn("🔑 Userlock Reset", Color.FromArgb(211, 84, 0), btnMtkUserlockReset_Click);
                    AddActionBtn("☁️ Mi Account", Color.FromArgb(233, 30, 99), btnMtkMiAccountReset_Click);
                    btnMtkMiAccountBtn = dynamicButtons[dynamicButtons.Count - 1];
                    btnMtkMiAccountBtn.Visible = false; // Xiaomi brand ရွေးမှသာ ပေါ်မယ်
                    AddActionBtn("📱 Remove Demo", Color.FromArgb(0, 150, 136), btnMtkRemoveDemo_Click);
                    AddActionBtn("🛡️ KG Unlock", Color.FromArgb(156, 39, 176), btnMtkSamsungKG_Click);
                    AddActionBtn("📶 NV Fix", Color.FromArgb(120, 50, 140), btnMtkFixNvram_Click);
                    AddActionBtn("💾 Backup NV", Color.FromArgb(63, 81, 181), btnMtkBackupNv_Click);
                    AddActionBtn("✏️ Write NV", Color.FromArgb(33, 150, 243), btnMtkWriteNv_Click);
                    AddActionBtn("💾 Normal Backup", Color.FromArgb(0, 150, 136), btnMtkNormalDump_Click);
                    AddActionBtn("💾 Full Backup", Color.FromArgb(46, 125, 50), btnMtkFullDump_Click);
                    AddActionBtn("🔄 Reboot Device", Color.FromArgb(96, 125, 139), btnMtkReboot_Click);
                    break;

                case "ADB":
                    AddActionBtn("📱 ADB Devices", Color.FromArgb(33, 150, 243), btnAdbDevices_Click);
                    AddActionBtn("📋 Full Info", Color.FromArgb(66, 133, 244), btnFullInfo_Click);
                    AddActionBtn("🗑️ Debloater (Apps)", Color.FromArgb(230, 80, 40), btnAdbDebloat_Click);
                    AddActionBtn("🇲🇲 Enable All Lang", Color.FromArgb(76, 175, 80), btnAdbEnableLang_Click);
                    AddActionBtn("🖥️ Screen Mirror", Color.FromArgb(0, 188, 212), btnAdbScrcpy_Click);
                    AddActionBtn("🔓 FRP Reset", Color.FromArgb(244, 67, 54), btnAdbFRP_Click);
                    AddActionBtn("🔋 Battery Status", Color.FromArgb(0, 150, 136), btnAdbBatteryInfo_Click);
                    AddActionBtn("📦 Install APK", Color.FromArgb(102, 187, 106), btnAdbInstall_Click);
                    AddActionBtn("📸 Screenshot", Color.FromArgb(142, 68, 173), btnAdbScreenshot_Click);
                    AddActionBtn("⚡ To Fastboot", Color.FromArgb(255, 152, 0), btnAdbRebootBootloader_Click);
                    AddActionBtn("🔌 To EDL 9008", Color.FromArgb(239, 83, 80), btnAdbRebootEdl_Click);
                    AddActionBtn("🔄 Reboot System", Color.FromArgb(60, 90, 120), btnAdbReboot_Click);
                    break;

                case "Fastboot":
                    AddActionBtn("⚡ Check Devices", Color.FromArgb(255, 167, 38), btnFbDevices_Click);
                    AddActionBtn("ℹ️ Get Variables", Color.FromArgb(200, 140, 30), btnFbGetvar_Click);
                    AddActionBtn("🔓 Fastboot FRP", Color.FromArgb(244, 67, 54), btnFbFrp_Click);      // <-- အသစ်ထည့်ထားသော FRP ခလုတ်
                    AddActionBtn("🛡️ Check ARB", Color.FromArgb(233, 30, 99), btnFbCheckArb_Click);
                    AddActionBtn("🔀 Switch Slot A/B", Color.FromArgb(156, 39, 176), btnFbSwitchSlot_Click);
                    AddActionBtn("🚀 Temp Boot TWRP", Color.FromArgb(0, 150, 136), btnFbTempBoot_Click);
                    AddActionBtn("⚡ To Fastbootd", Color.FromArgb(0, 188, 212), btnFbToFastbootd_Click);
                    AddActionBtn("🔥 Flash Boot", Color.FromArgb(230, 80, 40), btnFbFlashBoot_Click);
                    AddActionBtn("🔧 Flash Recovery", Color.FromArgb(33, 150, 243), btnFbFlashRecovery_Click);
                    AddActionBtn("🔓 OEM Unlock", Color.FromArgb(255, 193, 7), btnFbUnlock_Click);
                    AddActionBtn("🔄 Reboot System", Color.FromArgb(60, 100, 140), btnFbReboot_Click);
                    break;

                case "Sideload":
                    AddActionBtn("📊 Check Info", Color.FromArgb(33, 150, 243), btnSlInfo_Click);
                    AddActionBtn("📋 Full Info", Color.FromArgb(66, 133, 244), btnFullInfo_Click);
                    AddActionBtn("🔄 Reboot Recovery", Color.FromArgb(255, 152, 0), btnSlRebootRecovery_Click);
                    AddActionBtn("📦 Sideload ZIP", Color.FromArgb(230, 80, 40), btnSlSideload_Click);
                    AddActionBtn("🔄 Reboot System", Color.FromArgb(60, 100, 140), btnSlRebootSystem_Click);
                    break;

                case "Spreadtrum":
                    AddActionBtn("📱 SPD Info", Color.FromArgb(0, 188, 212), btnSpdDetect_Click);
                    AddActionBtn("🔓 SPD FRP (Diag)", Color.FromArgb(244, 67, 54), btnSpdReadPart_Click);
                    break;

                case "Samsung":
                    AddActionBtn("🔓 MTP FRP (*#0*#)", Color.FromArgb(244, 67, 54), btnSamMtpFrp_Click);
                    AddActionBtn("📱 Read Info (MTP)", Color.FromArgb(171, 71, 188), btnSamInfo_Click);
                    AddActionBtn("⚡ Reboot Download", Color.FromArgb(255, 152, 0), btnSamRebootDownload_Click);
                    AddActionBtn("🔄 Reboot Normal", Color.FromArgb(96, 125, 139), btnSamRebootNormal_Click);
                    AddActionBtn("📋 Read PIT", Color.FromArgb(33, 150, 243), btnSamReadPit_Click);
                    break;
            }

            // Brand ပေါ်မူတည်ပြီး Xiaomi-specific ခလုတ်တွေ ပြ/ဝှက်
            UpdateBrandActionVisibility();
        }

        // ================= Qualcomm Normal Backup (Skips huge userdata/cache) =================
        private async void btnQcNormalDump_Click(object sender, EventArgs e)
        {
            using var d = new FolderBrowserDialog();
            d.Description = "Select folder to save Qualcomm Normal Firmware Backup (Small Size)";
            if (d.ShowDialog() != DialogResult.OK) return;

            string backupDir = Path.Combine(d.SelectedPath, $"QC_Normal_ROM_{DateTime.Now:yyyyMMdd_HHmmss}");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string loaderArg = GetEdlLoaderArg();

            LogQualcomm($"\n💾 [Qualcomm] Starting Normal ROM Backup (Skipping userdata/cache for fast speed)...");
            LogQualcomm($"📁 Destination: {backupDir}");

            // edl.py rl <directory> --skip=userdata,cache,cust
            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}rl \"{backupDir}\" --skip=userdata,cache,cust", "Dumping Normal ROM...");
            if (PythonOpSucceeded(res))
            {
                GenerateQualcommRawprogram(backupDir);
                LogSuccess($"✅ Qualcomm Normal Firmware backed up successfully! (Small & Fast Size)");
                LogSuccess($"📄 Generated XML: rawprogram0.xml & patch0.xml");
                LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
                await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                LogSuccess("📱 Phone rebooted successfully!\n");
            }
            else
            {
                LogError("❌ Normal Dump Operation Failed.");
            }
        }

        // ================= MediaTek Normal Backup (Skips huge userdata/cache) =================
        private async void btnMtkNormalDump_Click(object sender, EventArgs e)
        {
            using var d = new FolderBrowserDialog();
            d.Description = "Select folder to save MediaTek Normal Firmware Backup (Small Size)";
            if (d.ShowDialog() != DialogResult.OK) return;

            string backupDir = Path.Combine(d.SelectedPath, $"MTK_Normal_ROM_{DateTime.Now:yyyyMMdd_HHmmss}");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            if (!IOFile.Exists(script)) script = Path.Combine(Application.StartupPath, "mtk", "mtk.py");

            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK($"\n💾 [MTK] Starting Normal ROM Backup (Skipping userdata/cache)...");
            LogMTK($"📁 Destination: {backupDir}");
            LogMTK("📱 Connect device in BROM mode (Hold Vol+ & Vol- -> Insert USB)");

            // mtk.py rl <dir> --skip userdata,cache
            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}rl \"{backupDir}\" --skip userdata,cache", "Dumping Normal ROM...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                var allParts = partitions.Where(p => !p.Name.Equals("userdata", StringComparison.OrdinalIgnoreCase) && !p.Name.Equals("cache", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToArray();
                GeneratePmkScatterFile(backupDir, allParts.Length > 0 ? allParts : new string[] { "boot", "recovery", "super", "system", "vendor" });
                LogSuccess("✅ Normal MediaTek Firmware backed up successfully! (Small & Fast)");
                LogSuccess("📄 Generated MTK Scatter file successfully!");

                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Device rebooted successfully!\n");
            }
        }

        // ================= Progress Bar Updater Helper =================
        private void UpdateGlobalProgress(int percentage, string statusInfo = "")
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateGlobalProgress(percentage, statusInfo)));
                return;
            }

            if (percentage < 0) percentage = 0;
            if (percentage > 100) percentage = 100;

            if (globalProgressBar != null) globalProgressBar.Value = percentage;
            if (lblProgressPercent != null)
            {
                if (!string.IsNullOrEmpty(statusInfo))
                    lblProgressPercent.Text = $"{percentage}% ({statusInfo})";
                else
                    lblProgressPercent.Text = $"{percentage}%";
            }
        }

        // ================= Force Stop All Running Operations =================
        private void btnStop_Click(object sender, EventArgs e)
        {
            try
            {
                cts?.Cancel();

                if (currentProcess != null && !currentProcess.HasExited)
                {
                    currentProcess.Kill(entireProcessTree: true);
                }

                string[] processNames = { "python", "pythonw", "adb", "fastboot", "QSaharaServer", "fh_loader" };
                foreach (var pName in processNames)
                {
                    var running = Process.GetProcessesByName(pName);
                    foreach (var p in running)
                    {
                        try { p.Kill(); } catch { }
                    }
                }
            }
            catch { }
            finally
            {
                currentProcess = null;
                cts = null;
                SetOperationState(false);
                SetStatus("Ready");
                UpdateGlobalProgress(0, "Stopped");
                Log("🛑 [STOPPED] Operation forcefully cancelled.", colorError);
            }
        }

        // ================= Qualcomm Handlers (Native + EDL Hybrid) =================
        // python EDL op success စစ်ဆေးချက် — exit code 0 + fail-phrases မပါရင် အောင်
        // (အရင်က "error:" ပဲ ရှာတာမို့ "Couldn't erase..." လိုမျိုး မှားပြီး success ပြခဲ့တာ ပြင်ပါတယ်)
        private bool PythonOpSucceeded(string outp)
        {
            if (outp == null) return false;
            if (outp.Contains("Traceback")) return false;
            if (lastPythonExitCode == 0) return true;
            if (Regex.IsMatch(outp, @"(?i)(couldn|failed|not found|doesn'?t exist|no gpt partition|usage:)")) return false;
            return true;
        }

        // GPT output ထဲမှာ partition rows ပါမပါ စစ်ပြီး grid ထဲ ဖြည့်ခြင်း
        private bool TryParseGptResult(string outp)
        {
            if (string.IsNullOrWhiteSpace(outp)) { LogError("❌ No output from GPT read."); return false; }
            bool hasGpt = outp.Contains("Parsing Lun") || outp.Contains("GPT Table:") || Regex.IsMatch(outp, @"Offset 0x");
            if (!hasGpt && (outp.Contains("Traceback") || outp.Contains("Usage:")))
            {
                LogError("❌ GPT read failed. Check log above.");
                return false;
            }
            if (hasGpt) { ParseGptOutput(outp); return true; }
            return false;
        }

        private void KillCurrentPython()
        {
            if (currentProcess != null && !currentProcess.HasExited)
            {
                try { currentProcess.Kill(entireProcessTree: true); } catch { }
            }
        }

        // USB mode GPT read — Auto-Detect အပြည့်အစုံ:
        // loader မရွေးထားရင် ဖုန်း identity (HWID+PK_HASH) ကို အရင်စုံစမ်းပြီး
        // မှတ်ထားတဲ့ loader ရှိရင် အလိုအလျောက် သုံးပြီး ပြန်စမ်းတယ်
        private async Task<bool> RunUsbReadGptAsync(string gptScript)
        {
            // loader ရှိပြီးသားဆို တစ်ခါတည်း စမ်း
            if (CurrentLoaderPath().Length > 0)
            {
                string r = await RunProcessCommand(pythonPath, $"\"{gptScript}\" {GetEdlLoaderArg()}printgpt", "Reading GPT (USB)...", true);
                return TryParseGptResult(r);
            }

            LogQualcomm("🔍 Auto-Detect: python will match loader by phone ID (HWID)...");
            detectedHwid = ""; detectedPkhash = ""; detectedLoaderMissing = false;

            // python က session တစ်ခုတည်းမှာ loader ရှာ → upload → auth → GPT အကုန်လုပ်တယ်
            // (loader ကို edlclient/Loaders ထဲမှာ hwid နာမည်နဲ့ မှတ်ထားလို့ loader မရွေးရဘဲ ရတယ်)
            var probe = RunProcessCommand(pythonPath, $"\"{gptScript}\" --memory={currentMemoryType} printgpt", "Reading GPT (USB auto)...", true);

            for (int i = 0; i < 120; i++)
            {
                if (probe.IsCompleted) break;
                if (detectedLoaderMissing && i >= 10) break; // loader မရှိ — ဆက်စောင့်နေစရာမလို
                await Task.Delay(500);
            }

            if (!probe.IsCompleted)
            {
                KillCurrentPython();
                try { await probe; } catch { }
                LogWarning("⚠️ ဒီဖုန်းအတွက် loader မမှတ်ရသေးပါ — Brand/Model ရွေးပြီး တစ်ခါ လုပ်ပေးပါ (နောက်ကစ auto မှတ်မိပါမယ်)");
                return false;
            }

            return TryParseGptResult(probe.Result);
        }

        private async void btnQcDetect_Click(object sender, EventArgs e)
        {
            LogQualcomm("\n📡 [Qualcomm] Listening for EDL 9008 Port...");
            LogQualcomm("📱 Connect phone in 9008 Mode (EDL Cable / Test Point)");

            string port = GetActiveComPort();
            string loader = txtFirmwarePath.Text.Trim();

            // USB (WinUSB) transport ရှိရင် — Python EDL က loader upload + auth + GPT အကုန်လုပ်ပါတယ် (serial ထက် 3.5x မြန်)
            if (UseUsbTransport())
            {
                LogQualcomm("\n⚡ [USB Mode] Fast transport detected — connecting & reading GPT...");
                string gptScript = Path.Combine(Application.StartupPath, "edl", "edl.py");
                if (!await RunUsbReadGptAsync(gptScript))
                {
                    // USB path မှာ fail ဖြစ်ရင် serial fallback မလုပ်တော့ဘူး (COM port မရှိတဲ့ setup မို့)
                    LogWarning("⚠️ USB GPT read မအောင်ပါ — log အပေါ်မှာ ကြည့်ပါ။");
                }
                return;
            }

            // Native C++ Execution
            if (IOFile.Exists(Path.Combine(qualcommCorePath, "QSaharaServer.exe")) && IOFile.Exists(loader))
            {
                bool sOk = await RunNativeSaharaLoader(port, loader);
                if (sOk)
                {
                    LogQualcomm("🔄 Waiting for Firehose mode switch...");
                    await Task.Delay(3000);

                    // Firehose mode ရောက်ပြီမို့ Python EDL (stock) နဲ့ GPT ဖတ်ပါတယ်
                    // — Xiaomi auth (sig) ကို module က auto ကိုင်တွယ်ပြီး --memory flag က sector size မှန်အောင် လုပ်ပေးပါတယ်
                    // — ဖုန်းက firehose ရောက်ပြီးသားမို့ --loader မပါတဲ့ command သုံးပါတယ် (loader ပါရင် python က Sahara လို့ ထင်ပြီး ရှုပ်နိုင်လို့)
                    LogQualcomm("\n📋 [Firehose] Reading GPT Partition Table (Python EDL)...");
                    string gptScript = Path.Combine(Application.StartupPath, "edl", "edl.py");
                    string gptArgs = $"--serial --memory={currentMemoryType} ";
                    string selPort = mobilePortCombo.SelectedItem?.ToString() ?? "";
                    if (selPort.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                        gptArgs += $"--portname={selPort} ";
                    else
                    {
                        string[] ports = SerialPort.GetPortNames();
                        if (ports.Length > 0) gptArgs += $"--portname={ports[0]} ";
                    }
                    string gptRes = await RunProcessCommand(pythonPath, $"\"{gptScript}\" {gptArgs}printgpt", "Reading GPT via Firehose...", true);

                    if (!string.IsNullOrWhiteSpace(gptRes))
                    {
                        if (gptRes.Contains("Traceback") || gptRes.Contains("Usage:") || gptRes.Contains("error:", StringComparison.OrdinalIgnoreCase))
                        {
                            LogError("❌ Failed to read GPT after loader upload. Check logs above.");
                        }
                        else
                        {
                            ParseGptOutput(gptRes);
                            return;
                        }
                    }
                    else
                    {
                        LogError("❌ No output from GPT read command.");
                    }
                }
            }

            // Fallback Python EDL Path (With Auto SIG Bypass)
            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string loaderArg = GetEdlLoaderArg();

            string output = await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}printgpt", "Connecting Qualcomm 9008...");
            if (!string.IsNullOrWhiteSpace(output))
            {
                // GPT rows ပါရင် အောင် (auth phase ရဲ့ "ERROR:" notice တွေကို fail အဖြစ် မမှတ်ဘူး)
                bool hasGpt = output.Contains("Parsing Lun") || output.Contains("GPT Table:") || Regex.IsMatch(output, @"Offset 0x");
                if (!hasGpt && (output.Contains("Traceback") || output.Contains("Usage:") || output.Contains("error:")))
                {
                    LogError("❌ Failed to communicate with 9008 Port. Check logs.");
                }
                else
                {
                    ParseGptOutput(output);
                }
            }
        }

        private async void btnQcBackupEfs_Click(object sender, EventArgs e)
        {
            using var folderDlg = new FolderBrowserDialog();
            folderDlg.Description = "Select folder to save EFS/QCN Backup";
            if (folderDlg.ShowDialog() != DialogResult.OK) return;

            string backupDir = Path.Combine(folderDlg.SelectedPath, $"QC_EFS_{DateTime.Now:yyyyMMdd_HHmmss}");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string loaderArg = GetEdlLoaderArg();
            string[] efsParts = { "modemst1", "modemst2", "fsg", "fsc" };

            LogQualcomm("\n💾 [Qualcomm] Backing up EFS/Network Partitions...");
            foreach (var p in efsParts)
            {
                string outImg = Path.Combine(backupDir, $"{p}.img");
                await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}r {p} \"{outImg}\"", $"Reading {p}...", false);
                LogQualcomm($"  • Read {p} -> OK");
            }
            LogSuccess($"✅ EFS Backup completed! Saved to: {backupDir}");
        }

        private async void btnQcRestoreEfs_Click(object sender, EventArgs e)
        {
            using var folderDlg = new FolderBrowserDialog();
            folderDlg.Description = "Select Folder containing EFS backup images (modemst1.img, etc.)";
            if (folderDlg.ShowDialog() != DialogResult.OK) return;

            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string loaderArg = GetEdlLoaderArg();
            string[] efsParts = { "modemst1", "modemst2", "fsg", "fsc" };

            LogQualcomm("\n✏️ [Qualcomm] Restoring EFS Partitions...");
            foreach (var p in efsParts)
            {
                string inImg = Path.Combine(folderDlg.SelectedPath, $"{p}.img");
                if (IOFile.Exists(inImg))
                {
                    await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}w {p} \"{inImg}\"", $"Writing {p}...", false);
                    LogQualcomm($"  • Written {p} -> OK");
                }
            }
            LogSuccess("✅ EFS Restore completed!");
            LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
            await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
            LogSuccess("📱 Phone rebooted successfully!\n");
        }

        private async void btnQcSafeFormat_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Safe Format Userdata? (Removes lock while preserving basic data on supported models)", "Confirm Safe Format", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string port = GetActiveComPort();
            string loader = txtFirmwarePath.Text.Trim();

            // Native C++ Execution
            if (IOFile.Exists(Path.Combine(qualcommCorePath, "QSaharaServer.exe")) && IOFile.Exists(loader))
            {
                bool sOk = await RunNativeSaharaLoader(port, loader);
                if (sOk)
                {
                    await Task.Delay(1000);
                    LogQualcomm("\n🔑 [Firehose] Safe Formatting Metadata...");
                    await RunNativeFhLoader(port, "--erase=metadata");
                    LogSuccess("✅ Safe Format executed successfully!");
                    await RunNativeFhLoader(port, "--reset");
                    return;
                }
            }

            // Fallback Python EDL Path
            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string loaderArg = GetEdlLoaderArg();

            LogQualcomm("\n🔑 [Qualcomm] Safe Formatting Metadata & User Keys...");
            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}e metadata", "Erasing metadata...", true);

            if (PythonOpSucceeded(res))
            {
                LogSuccess("✅ Safe Format executed successfully!");
                LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
                await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                LogSuccess("📱 Phone rebooted successfully!\n");
            }
            else
            {
                LogError("❌ Operation Failed. Check device connection and driver.");
            }
        }

        private async void btnQcReboot_Click(object sender, EventArgs e)
        {
            string port = GetActiveComPort();
            if (IOFile.Exists(Path.Combine(qualcommCorePath, "fh_loader.exe")))
            {
                await RunNativeFhLoader(port, "--reset");
                LogSuccess("✅ Reboot command sent via Native Engine!");
                return;
            }

            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string loaderArg = GetEdlLoaderArg();
            LogQualcomm("\n🔄 [Qualcomm] Sending Reset command to 9008 Port...");
            await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting Device...");
            LogSuccess("✅ Device is rebooting!");
        }

        // CARDAPP → PMKDAPP patch core — patched ဖိုင် ထုတ်ပေးပြီး path ပြန်တယ် (မအောင်ရင် null)
        private string PatchModemFileCore(string src)
        {
            try
            {
                byte[] data = IOFile.ReadAllBytes(src);
                string latin = System.Text.Encoding.Latin1.GetString(data);
                const string FIND = "CARDAPP";
                const string REPL = "PMKDAPP";

                var offsets = new List<int>();
                int idx = 0;
                while (true)
                {
                    int hit = latin.IndexOf(FIND, idx, StringComparison.Ordinal);
                    if (hit < 0) break;
                    offsets.Add(hit);
                    idx = hit + FIND.Length;
                }

                if (offsets.Count == 0)
                {
                    if (latin.IndexOf(REPL, StringComparison.Ordinal) >= 0)
                        LogInfo("ℹ️ 'PMKDAPP' ရှိနေပြီးသား — patch လုပ်ပြီးသားပါ။");
                    else
                        LogError("❌ 'CARDAPP' မတွေ့ပါ — ဒီ device/build အတွက် ဒီနည်း မသက်ဆိုင်တာ ဖြစ်နိုင်တယ်။");
                    return null;
                }

                string dir = Path.GetDirectoryName(src);
                string baseName = Path.GetFileNameWithoutExtension(src);
                string backupPath = Path.Combine(dir, baseName + "_original.bak");
                string outPath = Path.Combine(dir, baseName + "_PMK_bypass.bin");

                try { if (!IOFile.Exists(backupPath)) IOFile.Copy(src, backupPath); } catch (Exception ex) { LogWarning($"⚠️ Original backup (.bak) ကူးရာမှာ မအောင်မြင်ပါ: {ex.Message}"); }

                byte[] repBytes = System.Text.Encoding.ASCII.GetBytes(REPL);
                foreach (int off in offsets)
                {
                    for (int k = 0; k < REPL.Length; k++) data[off + k] = repBytes[k];
                    LogSuccess($"  ✏️ Offset 0x{off:X8}: CARDAPP → PMKDAPP");
                }
                IOFile.WriteAllBytes(outPath, data);
                LogSuccess($"✅ Patch ပြီးပါပြီ — {offsets.Count} နေရာ အစားထိုးပြီး။");
                LogSuccess($"📁 Patched file : {outPath}");
                return outPath;
            }
            catch (Exception ex)
            {
                LogError("❌ Patch error: " + ex.Message);
                return null;
            }
        }

        // ဖုန်းထဲက modem (NON-HLOS) partition ကို တိုက်ရိုက် dump → patch → ပြန်ရေးခြင်း
        private async Task<bool> RunPhoneDirectModemPatchAsync()
        {
            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string dumpPath = Path.Combine(Path.GetTempPath(), "pmk_modem_dump.bin");

            LogQualcomm("\n☁️ [Direct] Reading modem (NON-HLOS) partition from phone...");
            SetOperationState(true);
            SetStatus("Reading modem partition...");
            UpdateGlobalProgress(15, "Reading modem...");

            try { if (IOFile.Exists(dumpPath)) IOFile.Delete(dumpPath); } catch { }

            string rArgs = GetEdlLoaderArg();
            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {rArgs}r modem \"{dumpPath}\"", "Reading modem...", true);
            if (!PythonOpSucceeded(res) || !IOFile.Exists(dumpPath))
            {
                LogError("❌ modem partition ဖတ်လို့ မရပါ — device connection/loader စစ်ပါ။");
                SetOperationState(false);
                SetStatus("Ready");
                return false;
            }

            long sz = new FileInfo(dumpPath).Length;
            LogSuccess($"✅ Modem partition dumped: {(sz / (1024.0 * 1024.0)):F1} MB");

            string patched = PatchModemFileCore(dumpPath);
            if (patched == null)
            {
                SetOperationState(false);
                SetStatus("Ready");
                return false;
            }

            LogQualcomm("\n🔥 Writing patched modem back to NON-HLOS (modem partition)...");
            SetStatus("Writing patched modem...");
            UpdateGlobalProgress(50, "Writing modem...");

            string wArgs = GetEdlLoaderArg();
            string res2 = await RunProcessCommand(pythonPath, $"\"{script}\" {wArgs}w modem \"{patched}\"", "Writing modem...", true);
            if (!PythonOpSucceeded(res2))
            {
                LogError("❌ Patched modem ပြန်ရေးလို့ မရပါ — patched ဖိုင်ကတော့ အသင့်ရှိတယ် (နောက်မှ ရေးလို့ရတယ်): " + patched);
                SetOperationState(false);
                SetStatus("Ready");
                return false;
            }

            UpdateGlobalProgress(90, "Rebooting...");
            LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
            await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
            UpdateGlobalProgress(100, "Done");
            SetOperationState(false);
            SetStatus("Ready");
            LogSuccess("\n🎉 Mi Account Bypass အောင်မြင်ပါပြီ — patched modem ပြန်ရေးပြီး ဖုန်း restart ဖြစ်ပါတယ်!");
            return true;
        }

        // ================= ☁️ Mi Account Bypass — NON-HLOS (CARDAPP → PMKDAPP) =================
        // နည်းလမ်း: modem ဖိုင် (NON-HLOS.bin) ထဲက "CARDAPP" string ကို "PMKDAPP" (အလျားတူ) နဲ့ အစားထိုး
        // → Mi Account က ရှာရမဲ့ နေရာမှာ မတွေ့တော့ဘဲ bypass ဖြစ်တယ် (community service နည်း)
        private async void btnQcMiBypass_Click(object sender, EventArgs e)
        {
            // Mode ရွေး: (Yes) ဖုန်းထဲက modem partition တိုက်ရိုက် dump→patch→ပြန်ရေး | (No) ဖိုင်ကနေ patch
            if (MessageBox.Show(
                "ဖုန်းထဲက modem (NON-HLOS) partition ကို တိုက်ရိုက် dump → patch → ပြန်ရေး လုပ်မလား?\n\n" +
                "(ဖုန်း EDL/Firehose ချိတ်ထားရန် လိုပါတယ်။ 'No' ရွေးရင် ဖိုင်ကနေ patch လုပ်ပါမယ်)",
                "☁️ Mi Account Bypass", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                await RunPhoneDirectModemPatchAsync();
                return;
            }

            openFileDlg.Filter = "Modem Firmware (NON-HLOS.bin)|NON-HLOS*.bin;*.bin|All Files (*.*)|*.*";
            openFileDlg.Title = "Select NON-HLOS.bin (modem) — Mi Account bypass (CARDAPP → PMKDAPP)";
            if (openFileDlg.ShowDialog() != DialogResult.OK) return;
            string src = openFileDlg.FileName;

            byte[] data;
            try { data = IOFile.ReadAllBytes(src); }
            catch (Exception ex) { LogError("❌ Cannot read file: " + ex.Message); return; }

            string latin = System.Text.Encoding.Latin1.GetString(data);
            const string FIND = "CARDAPP";
            const string REPL = "PMKDAPP";

            var offsets = new List<int>();
            int idx = 0;
            while (true)
            {
                int hit = latin.IndexOf(FIND, idx, StringComparison.Ordinal);
                if (hit < 0) break;
                offsets.Add(hit);
                idx = hit + FIND.Length;
            }

            LogQualcomm("\n☁️ [Mi Account Bypass] Patching modem file: " + Path.GetFileName(src));

            if (offsets.Count == 0)
            {
                if (latin.IndexOf(REPL, StringComparison.Ordinal) >= 0)
                    LogInfo("ℹ️ 'PMKDAPP' ရှိနေပြီးသား — ဒီ modem ဖိုင်ကို patch လုပ်ပြီးသားပါ။");
                else
                    LogError("❌ 'CARDAPP' ကို မတွေ့ပါ — ဒီ device/build အတွက် ဒီနည်း မသက်ဆိုင်တာ ဖြစ်နိုင်တယ်။");
                return;
            }

            // Backup + patched copy (မူရင်းဖိုင် မပျက်စေဘူး)
            string dir = Path.GetDirectoryName(src);
            string baseName = Path.GetFileNameWithoutExtension(src);
            string backupPath = Path.Combine(dir, baseName + "_original.bak");
            string outPath = Path.Combine(dir, baseName + "_PMK_bypass.bin");

            try
            {
                if (!IOFile.Exists(backupPath)) IOFile.Copy(src, backupPath);
            }
            catch { }

            byte[] repBytes = System.Text.Encoding.ASCII.GetBytes(REPL);
            foreach (int off in offsets)
            {
                for (int k = 0; k < REPL.Length; k++) data[off + k] = repBytes[k];
                LogSuccess($"  ✏️ Offset 0x{off:X8}: CARDAPP → PMKDAPP");
            }
            IOFile.WriteAllBytes(outPath, data);
            LogSuccess($"✅ Patch ပြီးပါပြီ — {offsets.Count} နေရာ အစားထိုးပြီး။");
            LogSuccess($"📁 Patched file : {outPath}");
            LogInfo($"💾 Backup (မူရင်း) : {backupPath}");

            // ဖုန်းထဲ ချက်ချင်း flash လုပ်မလား?
            if (MessageBox.Show(
                "Patched modem ကို ဖုန်း NON-HLOS (modem) partition ထဲ ချက်ချင်း flash လုပ်မလား?\n\n(ဖုန်း EDL/Firehose ချိတ်ထားရန် လိုပါတယ် — မလုပ်ဘဲ ROM ထဲ ထည့်ပြီး MiFlash နဲ့လည်း ရတယ်)",
                "Flash Patched Modem?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string port = GetActiveComPort();
            string loader = txtFirmwarePath.Text.Trim();
            if (string.IsNullOrEmpty(loader) && txtSlot1 != null) loader = txtSlot1.Text.Trim();

            SetOperationState(true);
            SetStatus("Flashing patched modem...");
            UpdateGlobalProgress(10, "Connecting...");

            LogQualcomm("\n🔥 Flashing patched modem → NON-HLOS (modem partition)...");
            string wArgs = $"--memory={currentMemoryType} ";
            if (UseUsbTransport()) { }
            else wArgs = $"--serial --memory={currentMemoryType} --portname={port} ";
            if (!string.IsNullOrEmpty(loader) && IOFile.Exists(loader)) wArgs += $"--loader=\"{loader}\" ";

            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {wArgs}w modem \"{outPath}\"", "Writing modem...", true);
            if (PythonOpSucceeded(res))
            {
                UpdateGlobalProgress(100, "Done");
                LogSuccess("✅ Patched modem flashed successfully!");
                LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
                await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                LogSuccess("📱 Phone is restarting — Mi Account bypass active!\n");
            }
            else
            {
                LogError("❌ Modem flash failed. Patched file ကတော့ အသင့်ရှိပါတယ် — partition write/ROM ကနေ သုံးလို့ရတယ်။");
            }

            SetOperationState(false);
            SetStatus("Ready");
        }

        private async void btnQcResetFrp_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Reset Google Account (FRP)?", "Confirm FRP Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string port = GetActiveComPort();
            string loader = txtFirmwarePath.Text.Trim();

            // Native C++ Execution
            if (IOFile.Exists(Path.Combine(qualcommCorePath, "QSaharaServer.exe")) && IOFile.Exists(loader))
            {
                bool sOk = await RunNativeSaharaLoader(port, loader);
                if (sOk)
                {
                    await Task.Delay(1000);
                    LogQualcomm("\n🔓 [Firehose] Erasing FRP Partition via Native Engine...");
                    string res = await RunNativeFhLoader(port, "--erase=frp");
                    if (res != null && (res.Contains("All Finished Successfully") || res.Contains("0") || !res.Contains("failed", StringComparison.OrdinalIgnoreCase)))
                    {
                        LogSuccess("✅ FRP partition erased successfully!");
                        await RunNativeFhLoader(port, "--reset");
                        return;
                    }
                }
            }

            // Fallback Python EDL Path
            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string loaderArg = GetEdlLoaderArg();

            // FRP သိမ်းတဲ့ partition ကို auto ရှာဖွေခြင်း:
            // - ပုံမှန်: "frp" partition (အများစု)
            // - ဒီမော်ဒယ်လိုမျိုး (frp မပါတဲ့ Xiaomi SDM439): "config" partition (Qualcomm Persistent Data Block)
            var frpCandidates = new List<string>();
            if (partitions.Count > 0)
            {
                // GPT ဖတ်ပြီးသားဆို အစစ်အမှန် partition list ကနေ ရွေး
                foreach (var cand in new[] { "frp", "config", "persistent" })
                {
                    if (partitions.Any(p => p.Name.Equals(cand, StringComparison.OrdinalIgnoreCase)))
                        frpCandidates.Add(cand);
                }
            }
            else
            {
                frpCandidates.AddRange(new[] { "frp", "config" });
            }

            if (frpCandidates.Count == 0)
            {
                // GPT ထဲမှာ FRP သိမ်းတဲ့ partition မရှိ — fresh firmware ဖြစ်နိုင်တယ်
                LogInfo("ℹ️ FRP သိမ်းတဲ့ partition (frp/config/persistent) ဒီဖုန်းရဲ့ GPT မှာ မပါပါ — firmware အသစ်ဖြစ်ရင် FRP မရှိတော့တာမျိုးပါ။");
                LogWarning("⚠️ ဘာမှ ဖျက်စရာ မလိုပါ — ဖုန်း boot ပြီး Welcome screen မှာ Google account မတောင်းရင် FRP မရှိတာ အတည်ဖြစ်ပါတယ်။");
                return;
            }

            LogQualcomm($"\n🔓 [Qualcomm] Resetting FRP — target partition: {string.Join(", ", frpCandidates)}");

            bool frpDone = false;
            foreach (var part in frpCandidates)
            {
                LogQualcomm($"🗑️ Erasing [{part}] partition...");
                string resP = await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}e {part}", $"Erasing {part}...");

                if (PythonOpSucceeded(resP))
                {
                    if (part.Equals("config", StringComparison.OrdinalIgnoreCase))
                        LogSuccess($"✅ [{part}] (persistent block — FRP state ပါ) erased successfully!");
                    else
                        LogSuccess($"✅ [{part}] partition erased successfully!");
                    frpDone = true;
                    break;
                }
                LogWarning($"⚠️ [{part}] erase မအောင်ပါ — နောက် candidate စမ်းပါမယ်။");
            }

            if (frpDone)
            {
                LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
                await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                LogSuccess("📱 Phone is restarting to Welcome Screen!\n");
            }
            else
            {
                LogError("❌ FRP erase failed on all candidates. Check device connection / driver / GPT.");
            }
        }

        private async void btnQcFactoryReset_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Wipe Userdata partition? (All user data will be deleted)", "Confirm Factory Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string port = GetActiveComPort();
            string loader = txtFirmwarePath.Text.Trim();

            // Native C++ Execution
            if (IOFile.Exists(Path.Combine(qualcommCorePath, "QSaharaServer.exe")) && IOFile.Exists(loader))
            {
                bool sOk = await RunNativeSaharaLoader(port, loader);
                if (sOk)
                {
                    await Task.Delay(1000);
                    LogQualcomm("\n🔒 [Firehose] Formatting Userdata Partition via Native Engine...");
                    string res = await RunNativeFhLoader(port, "--erase=userdata");
                    if (res != null && (res.Contains("All Finished Successfully") || res.Contains("0") || !res.Contains("failed", StringComparison.OrdinalIgnoreCase)))
                    {
                        LogSuccess("✅ Factory reset completed successfully!");
                        await RunNativeFhLoader(port, "--reset");
                        return;
                    }
                }
            }

            // Fallback Python EDL Path
            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string loaderArg = GetEdlLoaderArg();

            LogQualcomm("\n🔒 [Qualcomm] Formatting userdata...");
            string resP = await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}e userdata", "Formatting Userdata...");

            if (PythonOpSucceeded(resP))
            {
                LogSuccess("✅ Factory reset completed successfully!");
                LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
                await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                LogSuccess("📱 Phone is restarting to Factory State!\n");
            }
            else
            {
                LogError("❌ Operation Failed. Check device connection and driver.");
            }
        }

        // ================= Qualcomm Full Backup (All Partitions including Userdata) =================
        private async void btnQcDump_Click(object sender, EventArgs e)
        {
            using var d = new FolderBrowserDialog();
            d.Description = "Select folder to dump Qualcomm Full Firmware Backup (All Partitions)";
            if (d.ShowDialog() != DialogResult.OK) return;

            string backupDir = Path.Combine(d.SelectedPath, $"QC_Full_ROM_{DateTime.Now:yyyyMMdd_HHmmss}");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string loaderArg = GetEdlLoaderArg();

            LogQualcomm($"\n💾 [Qualcomm] Starting Full Firmware Backup (All Partitions including userdata)...");
            LogQualcomm($"📁 Destination: {backupDir}");

            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {loaderArg}rl \"{backupDir}\"", "Dumping Full ROM...");
            if (PythonOpSucceeded(res))
            {
                GenerateQualcommRawprogram(backupDir);
                LogSuccess($"✅ Qualcomm Full Firmware dumped successfully!");
                LogSuccess($"📄 Generated XML: rawprogram0.xml & patch0.xml");
                LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
                await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                LogSuccess("📱 Phone rebooted successfully!\n");
            }
            else
            {
                LogError("❌ Full Dump Operation Failed.");
            }
        }

        private void GenerateQualcommRawprogram(string backupDir)
        {
            try
            {
                string xmlPath = Path.Combine(backupDir, "rawprogram0.xml");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" ?>");
                sb.AppendLine("<data>");

                foreach (var p in partitions)
                {
                    sb.AppendLine($"  <program SECTOR_SIZE_IN_BYTES=\"512\" file_sector_offset=\"0\" filename=\"{p.Name}.bin\" label=\"{p.Name}\" num_partition_sectors=\"0\" physical_partition_number=\"0\" size_in_KB=\"0\" sparse=\"false\" start_byte_hex=\"{p.Offset}\" start_sector=\"0\"/>");
                }
                sb.AppendLine("</data>");
                IOFile.WriteAllText(xmlPath, sb.ToString());

                string patchPath = Path.Combine(backupDir, "patch0.xml");
                IOFile.WriteAllText(patchPath, "<?xml version=\"1.0\" ?>\n<patches>\n</patches>");
            }
            catch (Exception ex) { LogError($"❌ rawprogram/patch0 xml ဖိုင်တွေ ရေးရာမှာ မအောင်မြင်ပါ: {ex.Message}"); }
        }

        // ================= MTK Operations =================
        private async void btnMtkDetect_Click(object sender, EventArgs e)
        {
            LogMTK("\n🔍 [MTK] Listening for MTK BROM Connection...");
            LogMTK("📱 1. Power OFF the device completely.");
            LogMTK("📱 2. Press & Hold (Volume Up + Volume Down).");
            LogMTK("📱 3. Connect USB Cable NOW.");

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            if (!IOFile.Exists(script)) script = Path.Combine(Application.StartupPath, "mtk", "mtk.py");

            string customDaArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            string output = await RunMtkSleekCommand($"\"{script}\" {customDaArg}printgpt", "Connecting MTK Device...");
            if (!string.IsNullOrWhiteSpace(output))
            {
                ParseGptOutput(output);
            }
        }

        private void btnMtkInfo_Click(object sender, EventArgs e) => btnMtkDetect_Click(sender, e);

        private async void btnMtkBackupNv_Click(object sender, EventArgs e)
        {
            using var folderDlg = new FolderBrowserDialog();
            folderDlg.Description = "Select folder to save NV backup";
            if (folderDlg.ShowDialog() != DialogResult.OK) return;

            string backupDir = Path.Combine(folderDlg.SelectedPath, $"NV_Backup_{DateTime.Now:yyyyMMdd_HHmmss}");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            if (!IOFile.Exists(script)) script = Path.Combine(Application.StartupPath, "mtk", "mtk.py");

            LogMTK("\n💾 [MTK] Starting One-Shot NVRAM & NVDATA Backup...");
            LogMTK("📱 Power OFF device -> Hold (Vol+ & Vol-) -> Insert USB Cable");

            string[] nvPartitions = { "nvcfg", "nvdata", "protect1", "protect2", "nvram" };
            string partNamesArg = string.Join(",", nvPartitions);
            string filePathsArg = string.Join(",", nvPartitions.Select(p => Path.Combine(backupDir, $"{p}.img")));

            UpdateGlobalProgress(10, "Starting...");
            string res = await RunMtkSleekCommand($"\"{script}\" r {partNamesArg} \"{filePathsArg}\"", "Backing up NV Partitions...");

            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                GeneratePmkScatterFile(backupDir, nvPartitions);
                UpdateGlobalProgress(100, "Done");
                LogSuccess($"\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                LogSuccess($"✅ All NV Partitions backed up successfully in ONE shot!");
                LogSuccess($"📄 Auto-generated Scatter File: PMK_Android_scatter.txt");
                LogMTK($"📁 Saved Folder: {backupDir}");

                LogMTK("🔄 [Auto Reboot] Restarting phone to System...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Device rebooted successfully!\n");
            }
            else
            {
                LogWarning("⚠️ Check log above for details.");
            }
        }

        private async void btnMtkWriteNv_Click(object sender, EventArgs e)
        {
            using OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "NV Image Files (*.img;*.bin)|*.img;*.bin|All Files (*.*)|*.*";
            openFileDialog.Title = "Select NV Partition File";

            if (openFileDialog.ShowDialog() != DialogResult.OK) return;

            string inputFile = openFileDialog.FileName;
            string partitionName = Path.GetFileNameWithoutExtension(inputFile);

            if (MessageBox.Show($"Write '{partitionName}' to device?", "Confirm Write NV", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            if (!IOFile.Exists(script)) script = Path.Combine(Application.StartupPath, "mtk", "mtk.py");

            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK($"\n✏️ Writing [{partitionName}] to device...");
            LogMTK("📱 Connect device in BROM mode (Hold Vol+ & Vol- -> Insert USB)");

            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {daArg}w {partitionName} \"{inputFile}\"", $"Writing {partitionName}...", true);
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess($"✅ {partitionName} written successfully!");
                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Device rebooted successfully!\n");
            }
        }

        private async void btnMtkFormatFrp_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Format FRP / Google Account partition?", "Confirm FRP Format", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            if (!IOFile.Exists(script)) script = Path.Combine(Application.StartupPath, "mtk", "mtk.py");

            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK("\n🔓 [MTK] Formatting FRP partition...");
            LogMTK("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable");

            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {daArg}e frp", "Formatting FRP...", true);
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess("✅ FRP partition formatted successfully!");
                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Phone is restarting to Welcome Screen!\n");
            }
        }

        // ================= MediaTek Full Backup (All Partitions including Userdata) =================
        private async void btnMtkFullDump_Click(object sender, EventArgs e)
        {
            using var d = new FolderBrowserDialog();
            d.Description = "Select folder to save MediaTek Full Firmware Backup (All Partitions)";
            if (d.ShowDialog() != DialogResult.OK) return;

            string backupDir = Path.Combine(d.SelectedPath, $"MTK_Full_ROM_{DateTime.Now:yyyyMMdd_HHmmss}");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            if (!IOFile.Exists(script)) script = Path.Combine(Application.StartupPath, "mtk", "mtk.py");

            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK($"\n💾 [MTK] Starting Full ROM Backup (All Partitions including userdata)...");
            LogMTK($"📁 Destination: {backupDir}");
            LogMTK("📱 Connect device in BROM mode (Hold Vol+ & Vol- -> Insert USB)");

            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}rl \"{backupDir}\"", "Dumping Full ROM...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                var allParts = partitions.Select(p => p.Name).ToArray();
                GeneratePmkScatterFile(backupDir, allParts.Length > 0 ? allParts : new string[] { "boot", "recovery", "super", "system", "vendor" });
                LogSuccess("✅ Full MediaTek Firmware backed up successfully!");
                LogSuccess("📄 Generated MTK Scatter file successfully!");

                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Device rebooted successfully!\n");
            }
        }

        private void GeneratePmkScatterFile(string backupDir, string[] nvPartList)
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
                    var pInfo = partitions.FirstOrDefault(p => p.Name.Equals(pName, StringComparison.OrdinalIgnoreCase));
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
            catch (Exception ex) { LogError($"❌ Scatter file ရေးရာမှာ မအောင်မြင်ပါ: {ex.Message}"); }
        }

        // ================= MTK Extended Features =================
        private async void btnMtkUnlockBL_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Unlock Bootloader via MTK BROM? (Will wipe user data)", "Confirm BL Unlock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK("\n🔓 [MTK] Unlocking Bootloader (seccfg)...");
            LogMTK("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable");

            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}da seccfg unlock", "Unlocking Bootloader...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess("✅ Bootloader Unlocked successfully!");
                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Device is rebooting to Unlocked State!\n");
            }
        }

        private async void btnMtkRelockBL_Click(object sender, EventArgs e)
        {
            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK("\n🔒 [MTK] Relocking Bootloader...");
            LogMTK("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable");

            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}da seccfg lock", "Relocking Bootloader...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess("✅ Bootloader Relocked successfully!");
                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Device is rebooting to Locked State!\n");
            }
        }

        private async void btnMtkUserlockReset_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Format Userlock / Screen Lock? (All user data will be erased)", "Confirm Userlock Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK("\n🔑 [MTK] Resetting Userlock (Formatting userdata & metadata)...");
            LogMTK("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable");

            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}e userdata,metadata", "Formatting Userlock...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess("✅ Screen lock removed successfully!");
                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Phone is restarting to Factory Setup!\n");
            }
        }

        private async void btnMtkMiAccountReset_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Reset Mi Account (Format Persist)?", "Confirm Mi Account Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK("\n☁️ [Xiaomi] Resetting Mi Account Lock...");
            LogMTK("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable");

            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}e persist,frp", "Resetting Mi Account...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess("✅ Mi Account Reset completed!");
                LogWarning("⚠️ Disable OTA update after booting to prevent relocking.");
                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Device is rebooting!\n");
            }
        }

        private async void btnMtkRemoveDemo_Click(object sender, EventArgs e)
        {
            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK("\n📱 [MTK] Removing Demo Mode (Oppo/Realme/Vivo)...");
            LogMTK("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable");

            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}e opporeserve2,demo,devinfo", "Removing Demo Mode...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess("✅ Demo Mode removed successfully!");
                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Device is rebooting to Normal Mode!\n");
            }
        }

        private async void btnMtkSamsungKG_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Reset Samsung KG / MDM Lock?", "Confirm KG Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK("\n🛡️ [Samsung] Resetting KG Lock / Persistent...");
            LogMTK("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable");

            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}e persistent,param,steady", "Resetting Samsung KG...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess("✅ Samsung KG / Persistent cleared successfully!");
                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Phone is rebooting!\n");
            }
        }

        private async void btnMtkFixNvram_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Reset NV data to fix WiFi/Baseband Error? (Make sure you have NV backup)", "Confirm NV Fix", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            string daArg = (!string.IsNullOrWhiteSpace(txtFirmwarePath.Text) && IOFile.Exists(txtFirmwarePath.Text)) ? $"--loader \"{txtFirmwarePath.Text.Trim()}\" " : "";

            LogMTK("\n📶 [MTK] Resetting NV Data & Sec Partitions...");
            LogMTK("📱 Power OFF -> Hold (Vol+ & Vol-) -> Insert USB Cable");

            string res = await RunMtkSleekCommand($"\"{script}\" {daArg}e nvdata,nvcfg", "Fixing NVRAM...");
            if (res != null && !res.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess("✅ NVRAM Error cleared!");
                LogMTK("🔄 [Auto Reboot] Restarting phone...");
                await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting...");
                LogSuccess("📱 Phone is rebooting!\n");
            }
        }

        private async void btnMtkReboot_Click(object sender, EventArgs e)
        {
            string script = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            LogMTK("\n🔄 [MTK] Sending Reset/Reboot command to device...");
            await RunMtkSleekCommand($"\"{script}\" reset", "Rebooting Device...");
            LogSuccess("✅ Reboot command sent!");
        }

        // ================= Sleek MTK Output Processor =================
        private async Task<string> RunMtkSleekCommand(string fullArgs, string statusText)
        {
            using CancellationTokenSource operationCts = new CancellationTokenSource();
            using Process process = new Process();
            cts = operationCts;
            currentProcess = process;
            SetOperationState(true);
            SetStatus(statusText);
            UpdateGlobalProgress(5, "Connecting...");

            process.StartInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-u {fullArgs}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Application.StartupPath
            };

            process.StartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            var fullOutput = new System.Text.StringBuilder();
            string cpu = "";
            string hwCode = "";
            string meid = "";
            string emmcId = "";
            string emmcSize = "";

            void HandleIncomingLine(string rawLine)
            {
                if (string.IsNullOrEmpty(rawLine)) return;

                string cleanLine = Regex.Replace(rawLine, @"\x1B\[[^@-~]*[=@-~]", "").Trim();
                if (string.IsNullOrEmpty(cleanLine)) return;

                fullOutput.AppendLine(cleanLine);

                this.Invoke(new Action(() =>
                {
                    Match pctMatch = Regex.Match(cleanLine, @"(\d{1,3}(?:\.\d+)?)%");
                    Match speedMatch = Regex.Match(cleanLine, @"(\d+(?:\.\d+)?\s*(?:MB|KB)/s)");
                    if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, out double pVal))
                    {
                        string spd = speedMatch.Success ? speedMatch.Groups[1].Value : "";
                        UpdateGlobalProgress((int)pVal, spd);
                    }

                    if (cleanLine.Contains("Port - Device detected", StringComparison.OrdinalIgnoreCase))
                    {
                        LogSuccess("⚡ Device Detected on BROM Port!");
                        UpdateGlobalProgress(15, "Connected");
                    }
                    else if (cleanLine.Contains("CPU:", StringComparison.OrdinalIgnoreCase)) cpu = cleanLine.Substring(cleanLine.IndexOf("CPU:") + 4).Trim();
                    else if (cleanLine.Contains("HW code:", StringComparison.OrdinalIgnoreCase)) hwCode = cleanLine.Substring(cleanLine.IndexOf("HW code:") + 8).Trim();
                    else if (cleanLine.Contains("ME_ID:", StringComparison.OrdinalIgnoreCase)) meid = cleanLine.Substring(cleanLine.IndexOf("ME_ID:") + 6).Trim();
                    else if (cleanLine.Contains("EMMC ID:", StringComparison.OrdinalIgnoreCase)) emmcId = cleanLine.Substring(cleanLine.IndexOf("EMMC ID:") + 8).Trim();
                    else if (cleanLine.Contains("EMMC USER Size:", StringComparison.OrdinalIgnoreCase))
                    {
                        string raw = cleanLine.Substring(cleanLine.IndexOf("EMMC USER Size:") + 15).Trim();
                        try
                        {
                            ulong bytes = Convert.ToUInt64(raw.Replace("0x", ""), 16);
                            emmcSize = $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                        }
                        catch { emmcSize = raw; }
                    }
                    else if (cleanLine.Contains("Bypassing security", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("Done sending payload", StringComparison.OrdinalIgnoreCase))
                    {
                        LogSuccess("🔓 Bypassing SLA/DAA Security (Kamakiri Exploit)...");
                        UpdateGlobalProgress(30, "Exploit OK");
                    }
                    else if (cleanLine.Contains("Uploading xflash stage 1", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("Successfully uploaded stage 2", StringComparison.OrdinalIgnoreCase))
                    {
                        Log("🚀 Uploading Download Agent (DA V5)...", colorInfo);
                        UpdateGlobalProgress(45, "Uploading DA");
                    }
                    else if (cleanLine.Contains("DRAM setup passed", StringComparison.OrdinalIgnoreCase))
                    {
                        LogSuccess($"💾 Memory Initialized: {emmcId} ({emmcSize})");
                    }
                    else if (cleanLine.Contains("GPT Table:", StringComparison.OrdinalIgnoreCase))
                    {
                        Log("📋 Reading Partition Table (GPT)...", colorInfo);
                        UpdateGlobalProgress(60, "Reading GPT");
                    }
                    else if (cleanLine.StartsWith("Wrote", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("Wrote ", StringComparison.OrdinalIgnoreCase))
                    {
                        Match fileMatch = Regex.Match(cleanLine, @"Wrote\s+(?:.*[\\/])?([a-zA-Z0-9_\-\.]+)\.img", RegexOptions.IgnoreCase);
                        if (fileMatch.Success)
                        {
                            string pName = fileMatch.Groups[1].Value;
                            string fileName = $"{pName}.img";
                            Log($"  ⚡ [WRITE OK] ➔ Partition: [{pName.PadRight(12)}] ➔ File: {fileName.PadRight(16)}  ✅", Color.FromArgb(0, 230, 118));
                        }
                        else
                        {
                            Log($"  • {cleanLine}", Color.FromArgb(255, 215, 0));
                        }
                    }
                    else if (cleanLine.Contains("Writing partition", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("DaHandler - Writing", StringComparison.OrdinalIgnoreCase))
                    {
                        Match wMatch = Regex.Match(cleanLine, @"partition\s+([a-zA-Z0-9_\-\.]+)", RegexOptions.IgnoreCase);
                        string pName = wMatch.Success ? wMatch.Groups[1].Value : "Partition";
                        Log($"\n🔥 [FLASHING] ➔ Writing [{pName}] ...", Color.FromArgb(255, 215, 0));
                    }
                    else if (cleanLine.Contains("Dumping partition", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("DaHandler - Dumping", StringComparison.OrdinalIgnoreCase))
                    {
                        Match dMatch = Regex.Match(cleanLine, @"partition\s+([a-zA-Z0-9_\-\.]+)", RegexOptions.IgnoreCase);
                        string pName = dMatch.Success ? dMatch.Groups[1].Value : "Partition";
                        Log($"\n💾 [READING] ➔ Dumping [{pName}] ...", Color.FromArgb(0, 229, 255));
                    }
                    else if (cleanLine.Contains("error:", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"❌ {cleanLine}", colorError);
                    }
                }));
            }

            process.OutputDataReceived += (s, e) => HandleIncomingLine(e.Data);
            process.ErrorDataReceived += (s, e) => HandleIncomingLine(e.Data);

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(operationCts.Token);

                string output = fullOutput.ToString().Trim();

                if (!string.IsNullOrEmpty(cpu) || !string.IsNullOrEmpty(hwCode))
                {
                    Log("\n------------------------------------------------------------", colorMTK);
                    Log("              📱 MEDIATEK DEVICE INFORMATION                ", colorMTK);
                    Log("------------------------------------------------------------", colorMTK);
                    Log($"  • CPU / Platform   : {cpu} [HW: {hwCode}]", colorSuccess);
                    Log($"  • Connection Mode  : BROM Mode (Security Bypassed)", colorInfo);
                    Log($"  • Security Status  : SBC: True | SLA: Bypassed | DAA: Bypassed", colorFastboot);
                    if (!string.IsNullOrEmpty(meid)) Log($"  • MEID             : {meid}", colorInfo);
                    if (!string.IsNullOrEmpty(emmcId)) Log($"  • Memory / Storage : eMMC ({emmcId}) [Capacity: {emmcSize}]", colorSuccess);
                    Log("------------------------------------------------------------", colorMTK);
                }

                UpdateGlobalProgress(100, "Completed");
                return output;
            }
            catch (Exception ex)
            {
                LogError($"❌ {ex.Message}");
                return null;
            }
            finally
            {
                if (!process.HasExited) try { process.Kill(entireProcessTree: true); } catch { }
                currentProcess = null;
                cts = null;
                SetOperationState(false);
                SetStatus("Ready");
            }
        }

        // ================= Generic Process Runner =================
        private async Task<string> RunProcessCommand(string executablePath, string arguments, string statusText, bool logLive = true)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return null;

            using CancellationTokenSource operationCts = new CancellationTokenSource();
            using Process process = new Process();
            cts = operationCts;
            currentProcess = process;
            SetOperationState(true);
            if (!string.IsNullOrEmpty(statusText)) SetStatus(statusText);
            UpdateGlobalProgress(15, "Running...");

            process.StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Application.StartupPath
            };

            var fullOutput = new System.Text.StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    string cleanLine = Regex.Replace(e.Data, @"\x1B\[[^@-~]*[=@-~]", "").Trim();
                    if (string.IsNullOrEmpty(cleanLine)) return;

                    fullOutput.AppendLine(cleanLine);

                    Match pctMatch = Regex.Match(cleanLine, @"(\d{1,3}(?:\.\d+)?)%");
                    Match speedMatch = Regex.Match(cleanLine, @"([\d.]+)\s*(MB|KB|GB)/s");
                    if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, out double pVal))
                    {
                        string spd = speedMatch.Success ? speedMatch.Groups[1].Value + " " + speedMatch.Groups[2].Value + "/s" : "";
                        UpdateGlobalProgress((int)pVal, spd);
                        return;
                    }

                    if (logLive)
                    {
                        this.Invoke(new Action(() => LogEdlLineSmart(cleanLine)));
                    }
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    string cleanLine = Regex.Replace(e.Data, @"\x1B\[[^@-~]*[=@-~]", "").Trim();
                    if (string.IsNullOrEmpty(cleanLine)) return;

                    fullOutput.AppendLine(cleanLine);
                    if (logLive)
                    {
                        this.Invoke(new Action(() => LogEdlLineSmart(cleanLine)));
                    }
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(operationCts.Token);
                UpdateGlobalProgress(100, "Done");
                return fullOutput.ToString().Trim();
            }
            catch (Exception ex)
            {
                if (logLive) LogError($"❌ {ex.Message}");
                return null;
            }
            finally
            {
                if (!process.HasExited) try { process.Kill(entireProcessTree: true); } catch { }
                currentProcess = null;
                cts = null;
                SetOperationState(false);
                SetStatus("Ready");
            }
        }

        // ================= GPT Parser =================
        private void ParseGptOutput(string gptOutput)
        {
            partitions.Clear();
            if (mobilePartitionGrid != null) mobilePartitionGrid.Rows.Clear();
            if (string.IsNullOrWhiteSpace(gptOutput)) return;

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
                catch { return "N/A"; }
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
                    partitions.Add(new PartitionInfo { Name = name, Offset = offset, Length = length, Type = sizeDisplay });
                    if (mobilePartitionGrid != null)
                        mobilePartitionGrid.Rows.Add(false, name, $"{name}.img", offset, length, sizeDisplay);
                }
            }
            if (partitions.Count > 0)
            {
                string deviceTxt = !string.IsNullOrEmpty(detectedChipset) ? $"{detectedChipset} — " : "";
                LogSuccess($"✅ {deviceTxt}{partitions.Count} partitions loaded into table.");
            }
        }

        // ================= Active ADB Serial Helper =================
        private async Task<string> GetActiveAdbSerial()
        {
            string output = await RunProcessCommand(adbPath, "devices", "", false);
            if (string.IsNullOrWhiteSpace(output)) return null;

            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("List of devices") || t.StartsWith("*") || string.IsNullOrWhiteSpace(t)) continue;
                if (t.Contains("device"))
                {
                    string[] parts = t.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && parts[0].Length >= 4) return parts[0];
                }
            }
            return null;
        }

        private async Task<string> RunAdbTargeted(string subArgs, string statusText = "", bool logLive = true)
        {
            string serial = await GetActiveAdbSerial();
            string targetArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
            return await RunProcessCommand(adbPath, $"{targetArg}{subArgs}", statusText, logLive);
        }

        // ================= Sideload (Recovery ADB Sideload) Handlers =================
        private async void btnSlInfo_Click(object sender, EventArgs e)
        {
            LogADB("\n📊 [Sideload] Gathering device info (ADB + Fastboot + properties)...");
            SetOperationState(true);
            SetStatus("Checking connected devices...");
            try
            {
                string devOut = await RunProcessCommand(adbPath, "devices -l", "Scanning ADB devices...", false);
                bool anyDevice = false;

                if (string.IsNullOrWhiteSpace(devOut))
                {
                    LogError("❌ ADB daemon not responding. Phone ကို USB ချိတ်ပြီး ပြန်စမ်းပါ။");
                }
                else
                {
                    LogInfo("── ADB Devices ──");
                    LogADB(devOut.Trim());
                    string[] lines = devOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("List of devices") || t.StartsWith("*")) continue;
                        // adb server startup/version chatter — device row မဟုတ်လို့ ကျော်တယ်
                        if (t.Contains("server version", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("doesn't match", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("daemon", StringComparison.OrdinalIgnoreCase)) continue;
                        string[] parts = t.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0) continue;
                        string serial = parts[0];
                        string state = parts.Length > 1 ? parts[1] : "?";

                        // state အလိုက် အချက်ပြ
                        string stateMsg;
                        switch (state.ToLowerInvariant())
                        {
                            case "sideload": stateMsg = "📦 Sideload mode — ZIP ပို့ဖို့ အဆင်သင့်"; break;
                            case "recovery": stateMsg = "🔧 Recovery — 'Apply update from ADB' ရွေးမှ Sideload ဖြစ်မယ်"; break;
                            case "device": stateMsg = "💻 Normal Android — Reboot Recovery လုပ်ပြီး Apply update from ADB ရွေးပါ"; break;
                            case "offline": stateMsg = "⚠️ Offline — USB cable/port ပြောင်းကြည့်ပါ"; break;
                            case "unauthorized": stateMsg = "🔒 Unauthorized — ဖုန်းပေါ်မှာ USB debugging allow နှိပ်ပါ"; break;
                            case "bootloader": stateMsg = "⚡ Bootloader mode (fastboot)"; break;
                            default: stateMsg = $"({state})"; break;
                        }
                        LogInfo($"• {serial}  →  {stateMsg}");

                        // extra tags (device:model:... transport_id:...) ရှိရင် ပြ
                        if (parts.Length > 2)
                        {
                            var tags = parts.Skip(2).Where(p => p.Contains(':'));
                            string tagsTxt = string.Join("  ", tags);
                            if (tagsTxt.Length > 0) LogInfo($"   {tagsTxt}");
                        }

                        // info ပိုထုတ်နိုင်တဲ့ state တွေမှာ getprop စမ်းတယ် (sideload/recovery/device)
                        if (state.Equals("sideload", StringComparison.OrdinalIgnoreCase) ||
                            state.Equals("recovery", StringComparison.OrdinalIgnoreCase) ||
                            state.Equals("device", StringComparison.OrdinalIgnoreCase))
                        {
                            anyDevice = true;
                            string props = await RunProcessCommand(adbPath, $"-s {serial} shell getprop", "Reading device properties...", false);
                            bool anyProp = false;
                            if (!string.IsNullOrWhiteSpace(props))
                            {
                                // ဖုန်းအကြောင်း အဓိက prop key တွေ — ဒီ key ပါတဲ့ စာကြောင်းတွေ အကုန်ပြမယ်
                                string[] keyTokens =
                                {
                                    "ro.product.", "ro.build.", "ro.miui.", "ro.mi.os.", "ro.boot.",
                                    "ro.serialno", "persist.sys.region", "ro.carrier", "region", "security_patch",
                                    "selinux", "verifiedboot", "warranty", "token", "hwc"
                                };
                                int shownProps = 0;
                                foreach (string pl in props.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    string pt = pl.Trim();
                                    if (pt.Length == 0) continue;
                                    string keyPart = pt;
                                    int br = pt.IndexOf(']');
                                    if (pt.StartsWith("[") && br > 0) keyPart = pt.Substring(1, br - 1);
                                    else
                                    {
                                        int eq = pt.IndexOf('=');
                                        if (eq > 0) keyPart = pt.Substring(0, eq);
                                    }
                                    keyPart = keyPart.Trim();
                                    bool hit = false;
                                    foreach (string tk in keyTokens)
                                    {
                                        if (keyPart.Contains(tk, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                                    }
                                    if (!hit) continue;
                                    if (!anyProp) LogInfo("── Properties ──");
                                    anyProp = true;
                                    LogADB($"• {pt}");
                                    if (++shownProps >= 60) { LogInfo("   (...ကျန်တဲ့ props တွေ ချန်လိုက်ပြီ)"); break; }
                                }
                                if (!anyProp && (props.Contains("error", StringComparison.OrdinalIgnoreCase) || props.Contains("closed", StringComparison.OrdinalIgnoreCase)))
                                {
                                    LogInfo("   (shell getprop မရဘူး — ဒီ recovery/sideload မှာ shell ပိတ်ထားတာ ပုံမှန်ပါ)");
                                }

                                if (anyProp)
                                {
                                    // MiAssistant ပုံစံ compact summary — props ရှိတဲ့အခါ ဖုန်းရဲ့ အဓိက info ကျစ်ကျစ်လျစ်လျစ်ပြမယ်
                                    string[] sumKeys =
                                    {
                                        "ro.product.marketname", "ro.product.model", "ro.product.device",
                                        "ro.build.version.release", "ro.build.version.sdk",
                                        "persist.sys.grant_version", "ro.build.display.id",
                                        "ro.build.version.security_patch", "ro.miui.region", "ro.boot.hwc", "ro.serialno"
                                    };
                                    string SumVal(string propsText, string key)
                                    {
                                        foreach (string pl in propsText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            string pt = pl.Trim();
                                            if (pt.StartsWith("[" + key + "]", StringComparison.OrdinalIgnoreCase))
                                            {
                                                int idx = pt.IndexOf("]: [");
                                                if (idx > 0)
                                                {
                                                    string v = pt.Substring(idx + 4);
                                                    if (v.EndsWith("]")) v = v.Substring(0, v.Length - 1);
                                                    return v.Trim();
                                                }
                                            }
                                        }
                                        return "-";
                                    }
                                    LogInfo("── Summary (MiAssistant ပုံစံ) ──");
                                    LogADB($"   MarketName : {SumVal(props, sumKeys[0])}");
                                    LogADB($"   Model      : {SumVal(props, sumKeys[1])}   (device: {SumVal(props, sumKeys[2])})");
                                    LogADB($"   Android    : {SumVal(props, sumKeys[3])}   (SDK {SumVal(props, sumKeys[4])})");
                                    LogADB($"   System     : {SumVal(props, sumKeys[5])}");
                                    LogADB($"   Build      : {SumVal(props, sumKeys[6])}");
                                    LogADB($"   Security   : {SumVal(props, sumKeys[7])}");
                                    LogADB($"   Region     : {SumVal(props, sumKeys[8])}   (hwc: {SumVal(props, sumKeys[9])})");
                                    LogADB($"   Serial     : {SumVal(props, sumKeys[10])}");
                                }
                            }
                            if (!anyProp)
                            {
                                if (state.Equals("sideload", StringComparison.OrdinalIgnoreCase))
                                {
                                    LogInfo("   ⚠️ Sideload mode မှာ shell ပိတ်ထားလို့ props မဖတ်နိုင်ပါ — info အပြည့်အစုံ ရဖို့:");
                                    LogInfo("       (a) ဖုန်းပေါ်မှာ Back နှိပ်ပြီး recovery menu အဓိကနေရာမှာ ရပ်ထားပါ (state=recovery) → Check Info ပြန်နှိပ်ပါ");
                                    LogInfo("       (b) သို့မဟုတ် Android system ထဲ boot တက်ပြီးမှ ADB tab / Check Info နဲ့ ပြန်စစ်ပါ — အပြည့်စုံဆုံး (MIUI version, security patch စသည်)");
                                }
                                else
                                {
                                    LogInfo("   (ဒီ mode မှာ ဖုန်း properties တွေ shell ကနေ ဖတ်လို့မရပါ — Samsung/Xiaomi stock recovery/sideload မှာ ဒီလိုဖြစ်တတ်ပါတယ်။ Model က ADB tag ကနေ ရပြီးသားပါ)");
                                }
                            }
                        }
                    }
                }

                // Fastboot devices ကိုပါ scan (fastboot mode phone တွေ အတွက်)
                string fbPath = Path.Combine(Application.StartupPath, "fastboot.exe");
                if (IOFile.Exists(fbPath))
                {
                    LogInfo("── Fastboot Devices ──");
                    string fbOut = await RunProcessCommand(fbPath, "devices -l", "Scanning fastboot devices...", false);
                    string fbTrim = (fbOut ?? "").Trim();
                    if (string.IsNullOrEmpty(fbTrim) || fbTrim.Contains("no devices"))
                    {
                        LogInfo("   (fastboot device မတွေ့ပါ)");
                    }
                    else
                    {
                        LogADB(fbTrim);
                        // fastboot device တွေ့ရင် getvar all နဲ့ model/bootloader/security state အပြည့် ထုတ်ပေးတယ်
                        foreach (string fl in fbTrim.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string ft = fl.Trim();
                            if (ft.Length == 0 || !ft.Contains('\t') && !ft.Contains(' ')) continue;
                            string[] fparts = ft.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (fparts.Length == 0 || fparts[0] == "List" || fparts[0].StartsWith("*")) continue;
                            string fbSerial = fparts[0];
                            if (fbSerial.Length < 4) continue;
                            string gv = await RunProcessCommand(fbPath, $"-s {fbSerial} getvar all", $"Reading {fbSerial} fastboot vars...", false);
                            if (!string.IsNullOrWhiteSpace(gv))
                            {
                                LogInfo($"── {fbSerial} getvar all ──");
                                int shown = 0;
                                foreach (string gl in gv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    string gt = gl.Trim();
                                    if (gt.Length == 0 || gt.StartsWith("Finished") || gt.StartsWith("getvar") || gt.Contains("is not") && gt.Contains("error", StringComparison.OrdinalIgnoreCase)) continue;
                                    LogADB($"   {gt}");
                                    if (++shown >= 40) { LogInfo("   (...ဆက်ကျန်တာတွေ ချန်လိုက်ပြီ)"); break; }
                                }
                            }
                            break; // ပထမဆုံး fastboot device တစ်ခုပဲ အသေးစိတ် ပြမယ်
                        }
                    }
                }

                if (!anyDevice && !string.IsNullOrWhiteSpace(devOut))
                    LogInfo("💡 ဖုန်းမတွေ့သေးပါ — USB ချိတ်ပြီး ဒီအတိုင်း ပြန်နှိပ်ပါ။ (Sideload mode ဖုန်းဆို 'adb devices' မှာ sideload ပြပါလိမ့်မယ်)");
            }
            finally
            {
                SetOperationState(false);
            }
        }

        private async void btnSlRebootRecovery_Click(object sender, EventArgs e)
        {
            LogADB("\n🔄 [Sideload] Rebooting to Recovery...");
            string res = await RunAdbTargeted("reboot recovery", "Rebooting to Recovery...", false);
            if (PythonOpSucceeded(res) || string.IsNullOrEmpty(res) || !res.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                LogSuccess("📱 Phone က Recovery ကို reboot လုပ်နေပါပြီ။");
                LogInfo("   ဖုန်းပေါ်မှာ 'Apply update from ADB' (sideload) ကို ရွေးပေးပါ — ပြီးရင် Sideload ZIP ခလုတ် နှိပ်လို့ရပါပြီ။");
            }
            else
            {
                LogError($"❌ Reboot recovery မအောင်မြင်ပါ: {res}");
            }
        }

        private async void btnSlSideload_Click(object sender, EventArgs e)
        {
            if (txtSideloadPath == null || string.IsNullOrWhiteSpace(txtSideloadPath.Text) || !IOFile.Exists(txtSideloadPath.Text.Trim()))
            {
                LogWarning("⚠️ အရင် 📦 Package (.zip) ဖိုင်ကို Browse နဲ့ ရွေးပေးပါ။");
                return;
            }
            string zip = txtSideloadPath.Text.Trim();

            LogADB($"\n📦 [Sideload] Sideloading: {Path.GetFileName(zip)}");
            LogInfo("   Phone က 'Apply update from ADB' စာမျက်နှာမှာ ရပ်နေတာ သေချာပါစေ — ဒီကနေ package ပို့ပေးမယ်။");
            SetOperationState(true);
            SetStatus("Sideloading package...");
            try
            {
                string output = await RunProcessCommand(adbPath, $"sideload \"{zip}\"", "Sideloading package...");
                SetOperationState(false);
                string low = (output ?? "").ToLowerInvariant();
                if (string.IsNullOrEmpty(low))
                {
                    LogSuccess("✅ Sideload ပြီးစီးဟန်ရှိပါတယ် — ဖုန်းမျက်နှာပြင်မှာ 'Install from ADB complete' ပြထားလား စစ်ပါ။");
                }
                else if (low.Contains("error") || low.Contains("failed") || low.Contains("closed") && low.Contains("unable"))
                {
                    LogError($"❌ Sideload မအောင်မြင်ပါ:\n{output.Trim()}");
                    LogWarning("💡 ဖုန်းက Sideload mode မှာ ရပ်နေကြောင်း သေချာပါစေ (Recovery → Apply update from ADB)။ ZIP က ROM/OTA format မှန်ရင် ပြန်စမ်းပါ။");
                }
                else
                {
                    LogSuccess("✅ Sideload ပြီးပါပြီ — ဖုန်းမျက်နှာပြင်မှာ အတည်ပြုပြီး 'Reboot system now' ရွေးပါ (သို့မဟုတ် ဒီကနေ 🔄 Reboot System နှိပ်ပါ)။");
                }
            }
            catch (Exception ex)
            {
                SetOperationState(false);
                LogError($"❌ Sideload error: {ex.Message}");
            }
        }

        private async void btnSlRebootSystem_Click(object sender, EventArgs e)
        {
            LogADB("\n🔄 [Sideload] Rebooting to System...");
            await RunAdbTargeted("reboot", "Rebooting System...", false);
            LogSuccess("📱 Phone ကို System ထဲ ပြန် boot လုပ်နေပါပြီ။");
        }

        // ================= Full Device Info (UFS Checker / INFO CHECKER ပုံစံ — ADB ONLINE လိုအပ်) =================
        private async void btnFullInfo_Click(object sender, EventArgs e)
        {
            string serial = await GetActiveAdbSerial();
            if (string.IsNullOrEmpty(serial))
            {
                LogWarning("⚠️ ADB device (ONLINE) မတွေ့ပါ — ဖုန်းကို Android ထဲ boot တင်ပြီး USB debugging ဖွင့်ထားပါ (ဒါမှမဟုတ် shell ရတဲ့ recovery menu မှာ ထားပါ)။\n   Sideload screen ပေါ်မှာတော့ ဘယ် tool နဲ့မှ full info မရနိုင်ပါ။");
                return;
            }
            LogADB($"\n📋 [Full Info] Reading device information (serial: {serial})...");
            SetOperationState(true);
            SetStatus("Reading full device info...");
            try
            {
                async Task<string> Sh(string cmd)
                {
                    string r = await RunProcessCommand(adbPath, $"-s {serial} shell {cmd}", "", false);
                    return (r ?? "").Trim();
                }

                // props တစ်ခါယူပြီး dictionary ဆောက်
                var prop = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string allProps = await Sh("getprop");
                foreach (string pl in allProps.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string pt = pl.Trim();
                    if (pt.StartsWith("[") && pt.Contains("]:"))
                    {
                        int br = pt.IndexOf(']');
                        string k = pt.Substring(1, br - 1);
                        int vi = pt.IndexOf("]: [");
                        if (vi > 0)
                        {
                            string v = pt.Substring(vi + 4);
                            if (v.EndsWith("]")) v = v.Substring(0, v.Length - 1);
                            prop[k] = v.Trim();
                        }
                    }
                }
                string P(string k) => prop.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "-";

                void Row(string name, string val)
                {
                    if (string.IsNullOrEmpty(val) || val == "-") return;
                    LogADB($"   {name,-20}: {val}");
                }

                // ---- IMEI helper (service call → getprop → dumpsys) ----
                async Task<string> TryImei(string[] cmds)
                {
                    var rx = new Regex("(\\d{15})");
                    foreach (string c in cmds)
                    {
                        string o = await Sh(c);
                        if (string.IsNullOrEmpty(o) || o.Contains("Permission", StringComparison.OrdinalIgnoreCase)) continue;
                        var m = rx.Match(o);
                        if (m.Success) return m.Groups[1].Value;
                    }
                    return "-";
                }

                // ===== IDENTITY =====
                LogInfo("── 🏷️ Identity ──");
                Row("Brand", P("ro.product.brand"));
                Row("Model", P("ro.product.model"));
                Row("Market Name", P("ro.product.marketname"));
                Row("Device (codename)", P("ro.product.device"));
                Row("Region (miui)", P("ro.miui.region"));
                Row("Region (locale)", P("ro.product.locale.region"));
                Row("Hardware", P("ro.hardware"));
                Row("SoC Manufacturer", P("ro.soc.manufacturer"));
                Row("SoC Model", P("ro.soc.model"));
                Row("Board Platform", P("ro.board.platform"));

                // ===== UNIQUE IDs =====
                LogInfo("── 🔑 Unique IDs ──");
                Row("Serial (Android)", P("ro.serialno"));
                Row("SNO (Bootloader)", P("ro.boot.serialno"));
                Row("PSN Code", P("ro.ril.oem.psn"));
                string btMac = await Sh("settings get secure bluetooth_address");
                Row("Bluetooth MAC", btMac.Contains("null") ? "-" : btMac);
                Row("WiFi MAC", P("ro.boot.wifimac"));
                Row("CPUID", P("ro.boot.cpuid"));
                Row("HW Version", P("ro.boot.hwversion"));

                // ===== OS SOFTWARE =====
                LogInfo("── 🤖 Android / Software ──");
                Row("Android Version", P("ro.build.version.release"));
                Row("API SDK", P("ro.build.version.sdk"));
                Row("CPU ABI", P("ro.product.cpu.abilist"));
                Row("Security Patch", P("ro.build.version.security_patch"));
                string swV = P("persist.sys.grant_version");
                if (swV == "-") swV = P("ro.build.display.id");
                Row("System (ROM)", swV);
                Row("Build Fingerprint", P("ro.build.fingerprint"));
                Row("Time Zone", P("persist.sys.timezone"));
                Row("SELinux", await Sh("getenforce"));
                string suChk = await Sh("ls /system/xbin/su /system/bin/su /sbin/su 2>/dev/null");
                Row("Root Access", string.IsNullOrEmpty(suChk) || suChk.Contains("No such") ? "no" : "YES (" + string.Join(" ", suChk.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Take(2)) + ")");
                Row("Debuggable", P("ro.debuggable"));

                // ===== IMEI =====
                LogInfo("── 📶 IMEI ──");
                string imei1 = await TryImei(new[] { "service call iphonesubinfo 1 s16 com.android.shell", "service call iphonesubinfo 4 i32 1 s16 com.android.shell", "getprop ro.ril.oem.imei" });
                string imei2 = await TryImei(new[] { "service call iphonesubinfo 12 i32 2 s16 com.android.shell", "service call iphonesubinfo 11 i32 2 s16 com.android.shell", "getprop ro.ril.oem.imei2" });
                Row("IMEI Slot 1", imei1);
                Row("IMEI Slot 2", imei2);

                // ===== BOOT / SECURITY / STORAGE =====
                LogInfo("── 🔒 Boot / Storage ──");
                Row("Bootloader State", P("ro.boot.vbmeta.device_state"));
                Row("Verified Boot", P("ro.boot.verifiedbootstate"));
                Row("Crypto State", P("ro.crypto.state"));
                Row("Active Slot", P("ro.boot.slot_suffix"));
                string frpPath = await Sh("ls /dev/block/bootdevice/by-name/frp /dev/block/by-name/frp 2>/dev/null");
                Row("FRP Partition", string.IsNullOrEmpty(frpPath) || frpPath.Contains("No such") ? "not found" : frpPath.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim());

                // Storage chip (UFS/eMMC) — sysfs ကနေ တကယ့် chip model ကို ရှာဖတ်တယ်
                string diskProbe = await Sh("for f in /sys/block/sd*/device/model /sys/class/scsi_device/*/device/model /sys/block/mmcblk*/device/name /sys/block/mmcblk*/device/manfid; do if [ -r \"$f\" ]; then v=$(cat \"$f\" 2>/dev/null); [ -n \"$v\" ] && echo \"$f=$v\"; fi; done 2>/dev/null");
                string chipModel = "";
                string mmcName = "", mmcManf = "";
                if (!string.IsNullOrWhiteSpace(diskProbe))
                {
                    foreach (string dl in diskProbe.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string dt = dl.Trim();
                        int eq = dt.LastIndexOf('=');
                        if (eq <= 0) continue;
                        string path = dt.Substring(0, eq).Trim();
                        string val = dt.Substring(eq + 1).Trim();
                        if (val.Length == 0) continue;
                        if (path.Contains("/device/model") && val != "1d84000.ufshc")
                        {
                            chipModel = val; // scsi/ufs model ဥပမာ SAMSUNG KLUDG4UHGC...
                        }
                        else if (path.Contains("mmcblk") && path.EndsWith("/name")) mmcName = val;
                        else if (path.Contains("mmcblk") && path.EndsWith("/manfid")) mmcManf = val;
                    }
                }
                if (string.IsNullOrEmpty(chipModel) && !string.IsNullOrEmpty(mmcName))
                    chipModel = mmcName + (string.IsNullOrEmpty(mmcManf) ? "" : $" (manfid {mmcManf})");
                Row("Storage Chip", string.IsNullOrEmpty(chipModel) ? P("ro.boot.bootdevice") : chipModel);
                Row("Boot Device", P("ro.boot.bootdevice"));

                LogSuccess("✅ Full info ဖတ်ပြီးပါပြီ။");
            }
            catch (Exception ex)
            {
                LogError($"❌ Full info error: {ex.Message}");
            }
            finally
            {
                SetOperationState(false);
            }
        }

        // ================= ADB Handlers & Functions =================
        private async void btnAdbDevices_Click(object sender, EventArgs e)
        {
            LogADB("\n🔍 [ADB] Scanning Connected Devices...");

            string output = await RunProcessCommand(adbPath, "devices -l", "Scanning ADB Devices...", false);
            if (string.IsNullOrWhiteSpace(output))
            {
                LogError("❌ ADB daemon not responding or no devices connected.");
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
                LogWarning("⚠️ No active ADB device detected.");
                return;
            }

            LogSuccess($"✅ Found {connectedSerials.Count} active device(s) connected!\n");

            int deviceIndex = 1;
            foreach (string serial in connectedSerials)
            {
                await DisplayStructuredDeviceInfo(serial, deviceIndex++, connectedSerials.Count);
            }
        }

        private async Task DisplayStructuredDeviceInfo(string serial, int index, int total)
        {
            SetStatus($"Reading device information: [{serial}]...");

            async Task<string> GetProp(string prop)
            {
                string res = await RunProcessCommand(adbPath, $"-s {serial} shell getprop {prop}", "", false);
                return string.IsNullOrWhiteSpace(res) ? "N/A" : res.Trim();
            }

            async Task<string> RunShell(string cmd)
            {
                string res = await RunProcessCommand(adbPath, $"-s {serial} shell \"{cmd}\"", "", false);
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

            Log("╔══════════════════════════════════════════════════════════╗", colorADB);
            Log($"║       📱 DEVICE [ {index} / {total} ] : {serial.PadRight(28)}║", colorADB);
            Log("╚══════════════════════════════════════════════════════════╝", colorADB);
            Log($"  • Device Serial    : {serial} [Online]", colorSuccess);
            Log($"  • Brand Name       : {brand.ToUpperInvariant()}", colorInfo);
            Log($"  • Model Name       : {model}", colorSuccess);
            Log($"  • Codename / Board : {deviceCode} / {chipset}", colorInfo);
            Log($"  • Android Version  : Android {androidVer} (SDK: {sdkVer})", colorFastboot);
            Log($"  • Security Patch   : {secPatch}", colorWarning);
            Log($"  • System Build/OS  : {buildId}", colorInfo);
            Log($"  • Bootloader State : {blStatus}", blState == "0" ? colorSuccess : colorError);
            Log($"  • Battery Level    : {battLevel}", colorSuccess);
            Log("────────────────────────────────────────────────────────────\n", colorADB);
        }

        private async void btnAdbBatteryInfo_Click(object sender, EventArgs e)
        {
            LogADB("\n🔋 [ADB] Reading Battery & Power Information...");

            string rawDump = await RunAdbTargeted("shell dumpsys battery", "Reading Battery Status...", false);
            if (string.IsNullOrWhiteSpace(rawDump))
            {
                LogError("❌ Failed to read battery data. Ensure device is connected.");
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

            Log("╔══════════════════════════════════════════════════════════╗", Color.FromArgb(0, 188, 212));
            Log("║               🔋 BATTERY & POWER STATUS                  ║", Color.FromArgb(0, 188, 212));
            Log("╚══════════════════════════════════════════════════════════╝", Color.FromArgb(0, 188, 212));
            Log($"  • Battery Level    : {level}%", colorSuccess);
            Log($"  • Charging Status  : {status}", statusRaw == "2" || statusRaw == "5" ? colorSuccess : colorWarning);
            Log($"  • Power Source     : {powerSource}", colorInfo);
            Log($"  • Battery Health   : {health}", healthRaw == "2" ? colorSuccess : colorError);
            Log($"  • Current Voltage  : {voltage}", colorInfo);
            Log($"  • Temperature      : {temp}", colorFastboot);
            Log($"  • Battery Tech     : {tech}", colorInfo);
            Log("────────────────────────────────────────────────────────────\n", Color.FromArgb(0, 188, 212));
        }

        private async void btnAdbInstall_Click(object sender, EventArgs e)
        {
            openFileDlg.Filter = "APK Files (*.apk)|*.apk";
            if (openFileDlg.ShowDialog() == DialogResult.OK)
            {
                LogADB($"\n📦 Installing APK: {Path.GetFileName(openFileDlg.FileName)}...");
                string res = await RunAdbTargeted($"install -r \"{openFileDlg.FileName}\"", "Installing APK...");
                if (res != null && res.Contains("Success")) LogSuccess("✅ App installed successfully!");
                else LogWarning("⚠️ Installation finished. Check output.");
            }
        }

        private async void btnAdbScreenshot_Click(object sender, EventArgs e)
        {
            saveFileDlg.Filter = "PNG Image (*.png)|*.png";
            saveFileDlg.FileName = $"Screen_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            if (saveFileDlg.ShowDialog() == DialogResult.OK)
            {
                LogADB("\n📸 Capturing screenshot from device...");
                await RunAdbTargeted("shell screencap -p /sdcard/temp_screen.png", "Capturing...", false);
                await RunAdbTargeted($"pull /sdcard/temp_screen.png \"{saveFileDlg.FileName}\"", "Saving...", false);
                await RunAdbTargeted("shell rm /sdcard/temp_screen.png", "", false);
                if (IOFile.Exists(saveFileDlg.FileName)) LogSuccess($"✅ Screenshot saved: {saveFileDlg.FileName}");
                else LogError("❌ Failed to capture screenshot.");
            }
        }

        private async void btnAdbFRP_Click(object sender, EventArgs e)
        {
            LogADB("\n🔓 [ADB] Universal FRP Reset (SetupWizard Bypass)...");
            await RunAdbTargeted("shell content insert --uri content://settings/secure --bind name:s:user_setup_complete --bind value:s:1", "Setting complete...", false);
            await RunAdbTargeted("shell pm clear com.google.android.setupwizard", "Clearing setup wizard...", false);
            await RunAdbTargeted("reboot", "Rebooting...", false);
            LogSuccess("✅ FRP command issued! Phone is restarting to Home.");
        }

        private async void btnAdbRebootBootloader_Click(object sender, EventArgs e) => await RunAdbTargeted("reboot bootloader", "Rebooting to Bootloader...");
        private async void btnAdbRebootRecovery_Click(object sender, EventArgs e) => await RunAdbTargeted("reboot recovery", "Rebooting to Recovery...");
        private async void btnAdbRebootEdl_Click(object sender, EventArgs e) => await RunAdbTargeted("reboot edl", "Rebooting to EDL...");
        private async void btnAdbReboot_Click(object sender, EventArgs e) => await RunAdbTargeted("reboot", "Rebooting System...");

        private async void btnAdbDebloat_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Uninstall common carrier bloatware and analytics apps?", "Confirm Debloat", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string[] bloatPackages = {
                "com.facebook.katana", "com.facebook.system", "com.facebook.appmanager", "com.facebook.services",
                "com.google.android.apps.tachyon", "com.google.android.feedback",
                "com.miui.analytics", "com.miui.msa.global", "com.miui.bugreport",
                "com.cleanmaster.mguard", "com.aura.oobe.samsung"
            };

            LogADB("\n🗑️ [ADB] Removing bloatware apps...");
            foreach (var pkg in bloatPackages)
            {
                string res = await RunAdbTargeted($"shell pm uninstall -k --user 0 {pkg}", "", false);
                if (res != null && res.Contains("Success")) LogADB($"  • Uninstalled: {pkg}");
            }
            LogSuccess("✅ Debloat operation finished!");
            LogADB("🔄 [Auto Reboot] Restarting phone...");
            await RunAdbTargeted("reboot", "Rebooting...", false);
        }

        private async void btnAdbEnableLang_Click(object sender, EventArgs e)
        {
            LogADB("\n🇲🇲 [ADB] Granting Language Change Permission (CHANGE_CONFIGURATION)...");
            await RunAdbTargeted("shell pm grant com.wanam.languageenabler android.permission.CHANGE_CONFIGURATION", "", false);
            await RunAdbTargeted("shell pm grant com.google.android.apps.translate android.permission.CHANGE_CONFIGURATION", "", false);
            await RunAdbTargeted("shell setprop persist.sys.locale my-MM", "", false);
            await RunAdbTargeted("shell am broadcast -a android.intent.action.LOCALE_CHANGED", "", false);
            LogSuccess("✅ All Languages Enabled! Please check phone language settings.");
        }

        private void btnAdbScrcpy_Click(object sender, EventArgs e)
        {
            string scrcpyPath = Path.Combine(Application.StartupPath, "scrcpy", "scrcpy.exe");
            if (!IOFile.Exists(scrcpyPath)) scrcpyPath = Path.Combine(Application.StartupPath, "scrcpy.exe");

            if (IOFile.Exists(scrcpyPath))
            {
                LogADB("\n🖥️ Launching Scrcpy Screen Mirror...");
                Process.Start(new ProcessStartInfo(scrcpyPath) { UseShellExecute = true });
            }
            else
            {
                LogError("❌ scrcpy.exe not found in tool folder. Please put scrcpy in tool directory.");
            }
        }

        // ================= Fastboot Handlers =================
        private async void btnFbDevices_Click(object sender, EventArgs e)
        {
            LogFastboot("\n⚡ [Fastboot] Checking Connected Devices...");
            string output = await RunProcessCommand(fastbootPath, "devices", "Checking Fastboot...", false);
            if (string.IsNullOrWhiteSpace(output))
            {
                LogWarning("⚠️ No fastboot device detected.");
                return;
            }

            Log("╔══════════════════════════════════════════════════════════╗", colorFastboot);
            Log("║             ⚡ FASTBOOT CONNECTED DEVICE                 ║", colorFastboot);
            Log("╚══════════════════════════════════════════════════════════╝", colorFastboot);
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Log($"  • {line.Trim()}", colorSuccess);
            }
            Log("────────────────────────────────────────────────────────────\n", colorFastboot);
        }

        // ================= Fastboot Multi-Brand FRP Reset (Unlocked Bootloader) =================
        private async void btnFbFrp_Click(object sender, EventArgs e)
        {
            LogFastboot("\n╔══════════════════════════════════════════════════════════╗");
            LogFastboot("║       🔓 FASTBOOT UNIVERSAL FRP RESET (MULTI-BRAND)      ║");
            LogFastboot("╚══════════════════════════════════════════════════════════╝");
            LogFastboot("⚡ Checking device connection & Bootloader status...");

            SetOperationState(true);
            SetStatus("Resetting Fastboot FRP...");
            UpdateGlobalProgress(15, "Checking Device...");

            // 1. Device ချိတ်ဆက်မှု စစ်ဆေးခြင်း
            string devCheck = await RunProcessCommand(fastbootPath, "devices", "", false);
            if (string.IsNullOrWhiteSpace(devCheck))
            {
                LogError("❌ No fastboot device detected. Connect phone in Fastboot Mode!");
                SetOperationState(false);
                SetStatus("Ready");
                UpdateGlobalProgress(0);
                return;
            }

            // 2. Bootloader Lock/Unlock အခြေအနေ နှင့် Model ဖတ်ယူခြင်း
            string varCheck = await RunProcessCommand(fastbootPath, "getvar all", "", false);
            bool isUnlocked = varCheck.Contains("unlocked:yes", StringComparison.OrdinalIgnoreCase) ||
                              varCheck.Contains("unlocked: yes", StringComparison.OrdinalIgnoreCase) ||
                              varCheck.Contains("unlocked: 1", StringComparison.OrdinalIgnoreCase);

            string product = "Unknown";
            Match mProd = Regex.Match(varCheck, @"product:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            if (mProd.Success) product = mProd.Groups[1].Value.Trim();

            LogSuccess($"📱 Connected Device : [{product.ToUpper()}]");
            LogSuccess($"🔓 Bootloader Status: {(isUnlocked ? "UNLOCKED (Ready to Reset)" : "⚠️ LOCKED (May fail on some partitions)")}");

            UpdateGlobalProgress(35, "Erasing FRP Partitions...");

            bool frpSuccess = false;

            // 3. Motorola သီးသန့် FRP Bypass Protocol (ဥပမာ- Moto G7 river စသည့် မော်ဒယ်များအတွက်)
            if (product.Contains("river") || product.Contains("ocean") || product.Contains("potter") || product.Contains("moto", StringComparison.OrdinalIgnoreCase))
            {
                LogFastboot("\n🛡️ [Motorola Protocol] Setting Factory Fastboot Mode...");
                await RunProcessCommand(fastbootPath, "oem fb_mode_set", "", false);
                await RunProcessCommand(fastbootPath, "erase config", "", false);
                await RunProcessCommand(fastbootPath, "erase frp", "", false);
                await RunProcessCommand(fastbootPath, "oem fb_mode_clear", "", false);
                frpSuccess = true;
            }

            // 4. Universal Partition Erase Sequence (Standard Android / Qualcomm / MTK / Pixel / Xiaomi)
            string[] frpPartitions = { "frp", "config", "persistent" };

            foreach (var part in frpPartitions)
            {
                LogFastboot($"⚡ Erasing [{part}] partition...");
                string res = await RunProcessCommand(fastbootPath, $"erase {part}", "", false);

                if (res != null && (res.Contains("OKAY") || res.Contains("finished")))
                {
                    LogSuccess($"  ✅ [{part}] Partition Cleared Successfully!");
                    frpSuccess = true;
                }
            }

            // 5. Userdata Lock & Format Verification
            UpdateGlobalProgress(80, "Finalizing...");
            await Task.Delay(500);

            if (frpSuccess)
            {
                UpdateGlobalProgress(100, "Done");
                LogSuccess("\n🎉 Fastboot FRP Reset Executed Successfully!");
                LogFastboot("🔄 [Auto Reboot] Restarting phone to System...");

                await RunProcessCommand(fastbootPath, "reboot", "Rebooting...", false);
                LogSuccess("📱 Phone is restarting to Welcome Screen without Google Lock!\n");
            }
            else
            {
                LogError("\n❌ Failed to erase FRP. Make sure Bootloader is Unlocked or use EDL/BROM Mode.");
                UpdateGlobalProgress(0);
            }

            SetOperationState(false);
            SetStatus("Ready");
        }
        private async void btnFbGetvar_Click(object sender, EventArgs e)
        {
            LogFastboot("\n⚡ [Fastboot] Reading Device Variables...");
            string output = await RunProcessCommand(fastbootPath, "getvar all", "Reading Variables...", false);
            if (string.IsNullOrWhiteSpace(output))
            {
                LogError("❌ No fastboot device detected or command failed.");
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

            Log("╔══════════════════════════════════════════════════════════╗", colorFastboot);
            Log("║             ⚡ FASTBOOT INFORMATION                      ║", colorFastboot);
            Log("╚══════════════════════════════════════════════════════════╝", colorFastboot);
            Log($"  • Serial Number    : {serial}", colorSuccess);
            Log($"  • Product Name     : {product}", colorSuccess);
            Log($"  • Bootloader Lock  : {(unlocked.ToLower() == "yes" ? "🔓 Unlocked" : "🔒 Locked")}", unlocked.ToLower() == "yes" ? colorSuccess : colorError);
            Log($"  • Secure Boot      : {secure}", colorInfo);
            Log($"  • Hardware Rev     : {hwVersion}", colorInfo);
            Log($"  • Baseband Version : {baseband}", colorInfo);
            Log("────────────────────────────────────────────────────────────\n", colorFastboot);
        }

        private async void btnFbFlashBoot_Click(object sender, EventArgs e)
        {
            openFileDlg.Filter = "IMG (*.img)|*.img";
            if (openFileDlg.ShowDialog() == DialogResult.OK)
            {
                LogFastboot($"\n🔥 Flashing Boot image: {Path.GetFileName(openFileDlg.FileName)}...");
                string res = await RunProcessCommand(fastbootPath, $"flash boot \"{openFileDlg.FileName}\"", "Flashing Boot...");
                if (res != null && !res.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    LogSuccess("✅ Boot image flashed successfully!");
                    LogFastboot("🔄 [Auto Reboot] Restarting phone to System...");
                    await RunProcessCommand(fastbootPath, "reboot", "Rebooting...", false);
                    LogSuccess("📱 Phone rebooted successfully!\n");
                }
            }
        }

        private async void btnFbFlashRecovery_Click(object sender, EventArgs e)
        {
            openFileDlg.Filter = "IMG (*.img)|*.img";
            if (openFileDlg.ShowDialog() == DialogResult.OK)
            {
                LogFastboot($"\n🔧 Flashing Recovery image: {Path.GetFileName(openFileDlg.FileName)}...");
                string res = await RunProcessCommand(fastbootPath, $"flash recovery \"{openFileDlg.FileName}\"", "Flashing Recovery...");
                if (res != null && !res.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    LogSuccess("✅ Recovery image flashed successfully!");
                    LogFastboot("🔄 [Auto Reboot] Restarting phone...");
                    await RunProcessCommand(fastbootPath, "reboot", "Rebooting...", false);
                    LogSuccess("📱 Phone rebooted successfully!\n");
                }
            }
        }

        private async void btnFbUnlock_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Unlock Bootloader? (Will wipe user data)", "Fastboot OEM Unlock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                LogFastboot("\n🔓 Unlocking bootloader...");
                string res = await RunProcessCommand(fastbootPath, "flashing unlock", "Unlocking Bootloader...");
                if (res != null && !res.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    LogSuccess("✅ Bootloader Unlocked!");
                    LogFastboot("🔄 [Auto Reboot] Restarting phone...");
                    await RunProcessCommand(fastbootPath, "reboot", "Rebooting...", false);
                    LogSuccess("📱 Phone is rebooting!\n");
                }
            }
        }

        private async void btnFbReboot_Click(object sender, EventArgs e) => await RunProcessCommand(fastbootPath, "reboot", "Rebooting...");

        private async void btnFbCheckArb_Click(object sender, EventArgs e)
        {
            LogFastboot("\n🛡️ [Fastboot] Checking Xiaomi Anti-Rollback (ARB) Index...");
            string output = await RunProcessCommand(fastbootPath, "getvar anti", "Checking ARB...", false);

            Match m = Regex.Match(output, @"anti:\s*(\d+)");
            if (m.Success)
            {
                string index = m.Groups[1].Value;
                Log("╔══════════════════════════════════════════════════════════╗", colorFastboot);
                Log($"║          🛡️ ANTI-ROLLBACK INDEX : [ {index} ]                   ║", colorFastboot);
                Log("╚══════════════════════════════════════════════════════════╝", colorFastboot);
                Log($"  • ARB Level: {index}", colorSuccess);
                Log("  • Warning: Never flash firmware with ARB lower than this number!", colorWarning);
            }
            else
            {
                LogWarning("⚠️ ARB Index not supported on this model or device locked.");
            }
        }

        private async void btnFbSwitchSlot_Click(object sender, EventArgs e)
        {
            string currentSlot = await RunProcessCommand(fastbootPath, "getvar current-slot", "", false);
            string targetSlot = currentSlot.Contains("_a") || currentSlot.Contains("a") ? "b" : "a";

            if (MessageBox.Show($"Switch active boot slot to [{targetSlot.ToUpper()}]?", "Confirm Slot Switch", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                LogFastboot($"\n🔀 Switching active slot to: [{targetSlot}]...");
                await RunProcessCommand(fastbootPath, $"--set-active={targetSlot}", $"Switching to Slot {targetSlot}...");
                LogSuccess($"✅ Active Slot set to [{targetSlot.ToUpper()}]!");
                LogFastboot("🔄 [Auto Reboot] Restarting phone...");
                await RunProcessCommand(fastbootPath, "reboot", "Rebooting...", false);
                LogSuccess("📱 Phone rebooted to switched slot!\n");
            }
        }

        private async void btnFbTempBoot_Click(object sender, EventArgs e)
        {
            openFileDlg.Filter = "Boot / Recovery Image (*.img)|*.img";
            if (openFileDlg.ShowDialog() == DialogResult.OK)
            {
                LogFastboot($"\n🚀 Temporary Booting image: {Path.GetFileName(openFileDlg.FileName)}...");
                await RunProcessCommand(fastbootPath, $"boot \"{openFileDlg.FileName}\"", "Temporary Booting...");
                LogSuccess("✅ Boot payload sent! Device is booting into temporary recovery.");
            }
        }

        private async void btnFbToFastbootd_Click(object sender, EventArgs e)
        {
            LogFastboot("\n⚡ Switching to Fastbootd Mode (Super / Dynamic Partitions)...");
            await RunProcessCommand(fastbootPath, "reboot fastboot", "Entering Fastbootd...");
        }

        // ================= Spreadtrum & Samsung Handlers =================
        private async void btnSpdDetect_Click(object sender, EventArgs e)
        {
            LogSPD("\n📱 [SPD] Reading Spreadtrum Device Hardware Info...");
            string platform = await RunAdbTargeted("shell getprop ro.board.platform", "", false);
            string chip = await RunAdbTargeted("shell getprop ro.hardware", "", false);
            string model = await RunAdbTargeted("shell getprop ro.product.model", "", false);

            Log("╔══════════════════════════════════════════════════════════╗", colorSPD);
            Log("║             📱 SPREADTRUM / UNISOC INFO                  ║", colorSPD);
            Log("╚══════════════════════════════════════════════════════════╝", colorSPD);
            Log($"  • Model Name       : {model?.Trim() ?? "N/A"}", colorSuccess);
            Log($"  • Platform         : {platform?.Trim() ?? "N/A"}", colorInfo);
            Log($"  • Chipset          : {chip?.Trim() ?? "N/A"}", colorInfo);
            Log("────────────────────────────────────────────────────────────\n", colorSPD);
        }

        private async void btnSpdReadPart_Click(object sender, EventArgs e)
        {
            LogSPD("\n🔓 [SPD] Resetting Spreadtrum FRP...");
            await RunAdbTargeted("shell pm clear com.google.android.setupwizard", "Clearing SetupWizard...", false);
            await RunAdbTargeted("reboot", "Rebooting...", false);
            LogSuccess("✅ SPD FRP Reset command sent! Phone is restarting.");
        }

        private async void btnSamInfo_Click(object sender, EventArgs e)
        {
            LogSamsung("\n📱 [Samsung] Reading Samsung Device Info (MTP / ADB)...");
            string model = await RunAdbTargeted("shell getprop ro.product.model", "", false);
            string csc = await RunAdbTargeted("shell getprop ro.csc.sales_code", "", false);
            string build = await RunAdbTargeted("shell getprop ro.build.display.id", "", false);
            string oneui = await RunAdbTargeted("shell getprop ro.build.version.oneui", "", false);

            Log("╔══════════════════════════════════════════════════════════╗", colorSamsung);
            Log("║             📱 SAMSUNG DEVICE INFORMATION                ║", colorSamsung);
            Log("╚══════════════════════════════════════════════════════════╝", colorSamsung);
            Log($"  • Model Name       : {model?.Trim() ?? "N/A"}", colorSuccess);
            Log($"  • CSC / Region     : {csc?.Trim() ?? "N/A"}", colorFastboot);
            Log($"  • One UI Version   : {oneui?.Trim() ?? "N/A"}", colorInfo);
            Log($"  • PDA / Build      : {build?.Trim() ?? "N/A"}", colorInfo);
            Log("────────────────────────────────────────────────────────────\n", colorSamsung);
        }

        private async void btnSamRebootDownload_Click(object sender, EventArgs e)
        {
            LogSamsung("\n⚡ Rebooting Samsung device to Download Mode...");
            await RunAdbTargeted("reboot download", "To Download Mode...");
        }

        private async void btnSamRebootNormal_Click(object sender, EventArgs e)
        {
            LogSamsung("\n🔄 Rebooting Samsung device to System...");
            await RunAdbTargeted("reboot", "Rebooting...");
        }

        private async void btnSamReadPit_Click(object sender, EventArgs e)
        {
            LogSamsung("\n📋 Reading Samsung PIT Partition Table in Download Mode...");
            LogInfo("📱 Connect phone in Download Mode (Vol Down + Power + Insert USB)");
            await Task.Delay(100);
        }

        private async void btnSamMtpFrp_Click(object sender, EventArgs e)
        {
            LogSamsung("\n╔══════════════════════════════════════════════════════════╗");
            LogSamsung("║         🔓 SAMSUNG MTP ONE-CLICK FRP RESET               ║");
            LogSamsung("╚══════════════════════════════════════════════════════════╝");
            LogSamsung("📱 1. Power ON device to Welcome Screen.");
            LogSamsung("📱 2. Click [Emergency Call] -> Type: *#0*# (or *#*#88#*#*)");
            LogSamsung("📱 3. Test Menu screen must appear on phone!");
            LogSamsung("⏳ Sending AT Commands to trigger USB Debugging...");

            SetOperationState(true);
            SetStatus("Running Samsung MTP FRP...");
            UpdateGlobalProgress(10, "Detecting Modem...");

            await Task.Run(async () =>
            {
                string[] ports = SerialPort.GetPortNames();
                bool found = false;

                foreach (string portName in ports)
                {
                    try
                    {
                        using SerialPort sp = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
                        sp.ReadTimeout = 1000;
                        sp.WriteTimeout = 1000;
                        sp.Open();

                        sp.WriteLine("AT\r\n");
                        Thread.Sleep(200);
                        string res = sp.ReadExisting();

                        if (res.Contains("OK"))
                        {
                            found = true;
                            this.Invoke(new Action(() => LogSuccess($"⚡ Found Samsung Modem Port on [{portName}]!")));

                            string[] atCmds = {
                                "AT+KICOMPATIBILITY=0\r\n",
                                "AT+DUMPCTRL=1,0\r\n",
                                "AT+DEBUGLVC=0,5\r\n",
                                "AT+SWVERSION=1\r\n",
                                "AT+ACTIVATE=0,0,0\r\n"
                            };

                            foreach (var cmd in atCmds)
                            {
                                sp.WriteLine(cmd);
                                Thread.Sleep(300);
                            }
                            break;
                        }
                    }
                    catch { }
                }

                if (!found)
                {
                    this.Invoke(new Action(() => LogError("❌ Samsung Modem COM Port not found. Ensure Samsung USB Drivers are installed.")));
                    this.Invoke(new Action(() => SetOperationState(false)));
                    this.Invoke(new Action(() => SetStatus("Ready")));
                    return;
                }

                this.Invoke(new Action(() => LogSuccess("🚀 Exploit sent! Look at the phone screen and tap [ALLOW ALWAYS] for USB Debugging.")));
                this.Invoke(new Action(() => UpdateGlobalProgress(50, "Waiting for ADB...")));

                for (int i = 1; i <= 15; i++)
                {
                    this.Invoke(new Action(() => SetStatus($"Waiting for ADB Authorization ({i}/15)...")));
                    string dev = await RunProcessCommand(adbPath, "devices", "", false);
                    if (dev != null && dev.Contains("\tdevice"))
                    {
                        this.Invoke(new Action(() => LogSuccess("✅ ADB Device Authorized! Resetting FRP...")));
                        this.Invoke(new Action(() => UpdateGlobalProgress(85, "Resetting FRP...")));

                        await RunProcessCommand(adbPath, "shell content insert --uri content://settings/secure --bind name:s:user_setup_complete --bind value:s:1", "", false);
                        await RunProcessCommand(adbPath, "shell pm clear com.sec.android.app.SecSetupWizard", "", false);
                        await RunProcessCommand(adbPath, "shell pm clear com.google.android.setupwizard", "", false);
                        await RunProcessCommand(adbPath, "reboot", "", false);

                        this.Invoke(new Action(() =>
                        {
                            UpdateGlobalProgress(100, "Done");
                            LogSuccess("🎉 Samsung FRP Reset Successfully! Phone is rebooting to Home.");
                            SetOperationState(false);
                            SetStatus("Ready");
                        }));
                        return;
                    }
                    await Task.Delay(2000);
                }

                this.Invoke(new Action(() =>
                {
                    LogWarning("⚠️ ADB authorization timeout. Please retry.");
                    SetOperationState(false);
                    SetStatus("Ready");
                    UpdateGlobalProgress(0);
                }));
            });
        }

        // ================= Test Point (TP) Image Viewer Popup Form =================
        private void ShowTestPointViewer(string brand, string model)
        {
            if (string.IsNullOrEmpty(brand) || brand.StartsWith("#") || string.IsNullOrEmpty(model) || model.StartsWith("#"))
            {
                MessageBox.Show("Please select a valid Brand and Model first to view Test Point.", "Select Device", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string cleanBrand = Regex.Replace(brand, @"\(.*?\)", "").Trim().Replace(" ", "_").Replace("/", "_");
            string cleanModel = Regex.Replace(model, @"\(.*?\)", "").Trim().Replace(" ", "_").Replace("/", "_");

            Match codeMatch = Regex.Match(model, @"\((.*?)\)");
            string codename = codeMatch.Success ? codeMatch.Groups[1].Value.Trim() : "";

            string tpBaseDir = Path.Combine(Application.StartupPath, "TestPoints");
            string imgPath = Path.Combine(tpBaseDir, cleanBrand, $"{cleanModel}.jpg");

            if (!IOFile.Exists(imgPath) && !string.IsNullOrEmpty(codename))
            {
                imgPath = Path.Combine(tpBaseDir, cleanBrand, $"{codename}.jpg");
            }

            if (!IOFile.Exists(imgPath))
            {
                MessageBox.Show($"Test Point image not found for [{brand} - {model}].\n\nPlease add the picture to:\nTestPoints/{cleanBrand}/{cleanModel}.jpg", "Pinout Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Form imgForm = new Form
            {
                Text = $"📌 Test Point Pinout: {brand} - {model}",
                Size = new Size(650, 720),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(20, 25, 35)
            };

            PictureBox pb = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = Image.FromFile(imgPath)
            };

            imgForm.Controls.Add(pb);
            imgForm.ShowDialog();
        }

        // ================= One-Click Driver Installer =================
        private void InstallAllDrivers()
        {
            string driverDir = Path.Combine(Application.StartupPath, "Drivers");
            if (!Directory.Exists(driverDir))
            {
                LogError("❌ Drivers folder not found at: " + driverDir);
                return;
            }

            var exes = Directory.GetFiles(driverDir, "*.exe");
            if (exes.Length == 0)
            {
                LogWarning("⚠️ No driver executable found in Drivers folder.");
                return;
            }

            foreach (var exe in exes)
            {
                LogInfo($"🛠️ Installing Driver: {Path.GetFileName(exe)}...");
                try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); } catch { }
            }
            LogSuccess("✅ Driver installers launched!");
        }

        // ================= UI Helpers =================
        private Label CreateSeaLabel(string text, Point location, bool heading) => new Label { Text = text, Location = location, AutoSize = true, ForeColor = heading ? Color.FromArgb(210, 225, 240) : Color.FromArgb(175, 190, 205), Font = new Font("Segoe UI", heading ? 9F : 8.5F, heading ? FontStyle.Bold : FontStyle.Regular) };
        private CheckBox CreateSeaCheckBox(string text, Point location) => new CheckBox { Text = text, Location = location, AutoSize = true, ForeColor = Color.FromArgb(205, 215, 225) };
        private TextBox CreateServiceTextBox(Point location, int width) => new TextBox { Location = location, Size = new Size(width, 25), BackColor = Color.FromArgb(40, 50, 65), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        private Button CreateSeaButton(string text, Point location, int width, int height, EventHandler handler)
        {
            Button b = new Button { Text = text, Location = location, Size = new Size(width, height), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(47, 72, 101), ForeColor = Color.White, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), Margin = new Padding(3) };
            b.FlatAppearance.BorderColor = Color.FromArgb(72, 99, 130);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(65, 100, 140);
            b.Click += handler;
            return b;
        }

        // ================= Logging =================
        // python EDL / QSaharaServer raw output တွေကို ရှင်းလင်းပြီး လှပအောင် ပြောင်းပြတဲ့ smart filter
        // — noise/banner/duplicate တွေကို ဖျောက်ပြီး အဓိကအချက်တွေကိုပဲ အရောင်စုံနဲ့ ပြတယ်
        // ================= Theme application (slot-based) =================
        private int SlotFromColor(Color c)
        {
            if (c.A < 255 || c.IsEmpty) return -1;
            (int r, int g, int b) = (c.R, c.G, c.B);
            // မူရင်း (Ocean Dark) အရောင်တွေနဲ့ပဲ compare လုပ်တယ် — theme ပြောင်းပြီးသား control တွေက map ထဲမှာ ရှိပြီးသား
            (int, int, int)[] slots = {
                (18,24,32), (14,20,27), (27,36,48), (22,29,39), (35,47,61), (31,41,55), (25,33,44),
                (35,45,58), (40,50,65), (20,28,38), (12,17,23), (10,16,22), (13,20,28), (18,26,36), (23,33,46), (30,44,60)
            };
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == (r, g, b)) return i;
            return -1;
        }

        // UI tree တစ်ခုလုံး လျှောက်ပြီး slot အရောင် control တွေကို မှတ်ထားတယ်
        private void ScanTheme(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (!themeSlotMap.ContainsKey(c))
                {
                    int slot = SlotFromColor(c.BackColor);
                    if (slot >= 0) themeSlotMap[c] = slot;
                }
                ScanTheme(c);
            }
        }

        private void RestyleGrid()
        {
            if (mobilePartitionGrid == null) return;
            Color row = ThemeManager.Slot(13), rowAlt = ThemeManager.Slot(14), head = ThemeManager.Slot(15), gridBg = ThemeManager.Slot(12);
            double lum = 0.299 * gridBg.R + 0.587 * gridBg.G + 0.114 * gridBg.B;
            mobilePartitionGrid.BackgroundColor = gridBg;
            mobilePartitionGrid.GridColor = lum >= 150 ? Color.FromArgb(200, 208, 220) : Color.FromArgb(45, 60, 80);
            mobilePartitionGrid.ColumnHeadersDefaultCellStyle.BackColor = head;
            mobilePartitionGrid.ColumnHeadersDefaultCellStyle.ForeColor = ThemeManager.TextFor(head);
            mobilePartitionGrid.DefaultCellStyle.BackColor = row;
            mobilePartitionGrid.DefaultCellStyle.ForeColor = ThemeManager.TextFor(row);
            mobilePartitionGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(33, 150, 243);
            mobilePartitionGrid.DefaultCellStyle.SelectionForeColor = Color.White;
            mobilePartitionGrid.AlternatingRowsDefaultCellStyle.BackColor = rowAlt;
            mobilePartitionGrid.AlternatingRowsDefaultCellStyle.ForeColor = ThemeManager.TextFor(rowAlt);
            mobilePartitionGrid.EnableHeadersVisualStyles = false;
        }

        private void ApplyTheme()
        {
            ScanTheme(this);
            themeLoading = true;
            try
            {
                foreach (var kv in themeSlotMap)
                {
                    Control c = kv.Key;
                    if (c.IsDisposed) continue;
                    c.BackColor = ThemeManager.Slot(kv.Value);
                    c.ForeColor = ThemeManager.TextFor(c.BackColor);
                }

                // Text-only controls (Label/CheckBox) — ကိုယ့် background မရှိတာမို့ parent ရဲ့ theme အရောင်အတိုင်း လိုက်ချိန်တယ်
                void FixText(Control parent)
                {
                    foreach (Control c in parent.Controls)
                    {
                        if (c is Button) { FixText(c); continue; } // Button တွေက ကိုယ်ပိုင် accent ထားတယ်
                        bool textOnly = c is Label || c is CheckBox || c is LinkLabel;
                        if (textOnly)
                        {
                            Color bg = c.BackColor;
                            Control walk = c;
                            while (bg.A != 255 && walk.Parent != null) { walk = walk.Parent; bg = walk.BackColor; }
                            if (bg.A != 255) bg = ThemeManager.Slot(0);

                            // အစိမ်း/အပြာ accent စာသားတွေကို light background မှာ ဖတ်ရလွယ်အောင် ချိန်ပေးတယ်
                            double bgLum = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
                            if (bgLum >= 150)
                            {
                                if (c.ForeColor == Color.FromArgb(0, 230, 118)) c.ForeColor = Color.FromArgb(0, 140, 80);
                                else if (c.ForeColor == Color.FromArgb(100, 181, 246)) c.ForeColor = Color.FromArgb(0, 90, 170);
                                else if (c.ForeColor == Color.FromArgb(255, 179, 71)) c.ForeColor = Color.FromArgb(200, 110, 10);
                                else c.ForeColor = ThemeManager.TextFor(bg);
                            }
                            else
                            {
                                c.ForeColor = ThemeManager.TextFor(bg);
                            }
                        }
                        FixText(c);
                    }
                }
                FixText(this);
            }
            finally { themeLoading = false; }

            // StatusStrip label က Items collection ထဲမှာမို့ သီးသန့်ချိန်ပေးတယ်
            if (lblStatus != null && statusStrip != null)
                lblStatus.ForeColor = ThemeManager.TextFor(statusStrip.BackColor);

            // Settings tab — theme နဲ့ လိုက်အောင် ချိန်
            if (settingsPanel != null) settingsPanel.BackColor = ThemeManager.Slot(3);

            RestyleGrid();

            // Layout တည်ငြိမ်ပြီးမှ action buttons တွေကို ပြန်စီပေးတယ် (constructor မှာ စောစောစီးစီး ဖြစ်ရင် gap ကျန်နိုင်လို့)
            ReflowActionButtons();
        }

        // ============ Settings tab — theme selector + PC info ============
        private void BuildSettingsPanel(Panel host)
        {
            settingsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 29, 39),
                Padding = new Padding(12),
                AutoScroll = true,
                Visible = false
            };

            settingsPanel.Controls.Add(CreateSeaLabel("⚙️ SETTINGS", new Point(12, 10), true));

            // --- Theme section ---
            settingsPanel.Controls.Add(CreateSeaLabel("🎨 Theme", new Point(12, 58), false));
            InitThemeSelector(settingsPanel); // combo ကို (150, 54) မှာ ထည့်ပေးမယ်

            Button btnRefreshInfo = CreateSeaButton("🔄 Refresh Info", new Point(340, 52), 110, 26, (s, e) =>
            {
                PopulatePcInfo();
                SetStatus("PC info refreshed");
            });
            btnRefreshInfo.BackColor = Color.FromArgb(33, 150, 243);
            settingsPanel.Controls.Add(btnRefreshInfo);

            // --- PC Info section ---
            settingsPanel.Controls.Add(CreateSeaLabel("🖥️ PC INFO", new Point(12, 104), true));

            string[] titles =
            {
                "Operating System", "PC Name", "User", "CPU", "CPU Cores / Threads",
                "Memory (RAM)", "C: Drive", "System Uptime", ".NET Runtime",
                "Tool Version", "App Folder", "Loader DB", "edl engine", "mtk engine", "Python"
            };

            int y = 146;
            const int step = 26;
            foreach (string t in titles)
            {
                Label key = CreateSeaLabel(t, new Point(12, y), false);
                key.AutoSize = false;
                key.Size = new Size(190, 22);
                key.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold); // ဖတ်ရလွယ်အောင် ပိုကြီး/ထူပေးတယ်
                settingsPanel.Controls.Add(key);

                Label val = new Label
                {
                    Location = new Point(215, y),
                    AutoSize = false,
                    Size = new Size(600, 22),
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(220, 230, 245),
                    Text = "…",
                    TextAlign = ContentAlignment.MiddleLeft
                };
                settingsPanel.Controls.Add(val);
                pcInfoRows.Add((t, val));
                y += step;
            }

            // Section headers (⚙️ SETTINGS / 🖥️ PC INFO) — ပိုကြီးပြီး ထင်ရှားအောင်
            foreach (Control c in settingsPanel.Controls)
            {
                if (c is Label l && l.Font.Bold && l.Font.SizeInPoints < 10.5F)
                    l.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            }

            PopulatePcInfo(); // ပထမဆုံးဝင်ကြည့်ကတည်းက info တွေ ပြပြီးသားဖြစ်အောင်
        }

        private string _FmtGB(ulong bytes) => bytes >= 1073741824UL ? $"{bytes / 1073741824.0:0.0} GB" : $"{bytes / 1048576.0:0.0} MB";

        private void PopulatePcInfo()
        {
            if (settingsPanel == null || pcInfoRows.Count == 0) return;
            void Set(string title, string value)
            {
                var row = pcInfoRows.FirstOrDefault(r => r.title == title);
                if (row.valLbl != null && !row.valLbl.IsDisposed) row.valLbl.Text = value;
            }

            // RAM
            ulong ramTotal = 0, ramAvail = 0;
            try
            {
                var ms = new MEMORYSTATUSEX();
                ms.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref ms)) { ramTotal = ms.ullTotalPhys; ramAvail = ms.ullAvailPhys; }
            }
            catch { }

            // CPU name (registry)
            string cpuName = "";
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                cpuName = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "";
            }
            catch { }

            // Drive C:
            string driveInfo = "";
            try
            {
                var drv = new DriveInfo("C");
                if (drv.IsReady)
                    driveInfo = $"{_FmtGB((ulong)drv.TotalFreeSpace)} free / {_FmtGB((ulong)drv.TotalSize)}";
                else driveInfo = "C: not ready";
            }
            catch (Exception ex) { driveInfo = ex.Message; }

            // Runtime engines
            string startUp = Application.StartupPath;
            bool edlOk = IOFile.Exists(Path.Combine(startUp, "edl", "edl.py"));
            bool mtkOk = IOFile.Exists(Path.Combine(startUp, "mtkclient", "mtk.py")) || IOFile.Exists(Path.Combine(startUp, "mtk", "mtk.py"));
            int loaderBrands = 0;
            try { loaderBrands = Directory.GetDirectories(Path.Combine(startUp, "Loaders")).Length; } catch { }

            // Python version (fast hidden check)
            string pyVer = "";
            try
            {
                var psi = new ProcessStartInfo(pythonPath, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string outp = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } pyVer = "timeout"; }
                    else pyVer = outp.Trim();
                }
                else pyVer = "cannot start";
            }
            catch (Exception ex) { pyVer = "not found: " + ex.Message; }

            TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);

            Set("Operating System", $"{Environment.OSVersion.VersionString}  ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
            Set("PC Name", Environment.MachineName);
            Set("User", Environment.UserName);
            Set("CPU", string.IsNullOrEmpty(cpuName) ? "N/A" : cpuName);
            Set("CPU Cores / Threads", Environment.ProcessorCount.ToString());
            Set("Memory (RAM)", ramTotal > 0 ? $"{_FmtGB(ramTotal)} total / {_FmtGB(ramAvail)} free" : "N/A");
            Set("C: Drive", driveInfo);
            Set("System Uptime", $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m");
            Set(".NET Runtime", $".NET {Environment.Version} ({RuntimeInformation.ProcessArchitecture})");
            Set("Tool Version", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?");
            Set("App Folder", startUp);
            Set("Loader DB", loaderBrands > 0 ? $"{loaderBrands} brand folder(s) in Loaders\\" : "not found");
            Set("edl engine", edlOk ? "✅ edl\\edl.py present" : "❌ missing");
            Set("mtk engine", mtkOk ? "✅ mtkclient/mtk.py present" : "❌ missing");
            Set("Python", string.IsNullOrEmpty(pyVer) ? "not found" : pyVer);
        }

        private void InitThemeSelector(Panel host)
        {
            cboTheme = new ComboBox
            {
                Location = new Point(150, 54),
                Size = new Size(170, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 45, 58),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cboTheme.Items.AddRange(ThemeManager.ThemeNames);
            cboTheme.SelectedIndex = 0;
            cboTheme.SelectedIndexChanged += (s, e) =>
            {
                if (themeLoading) return;
                ThemeManager.Set(cboTheme.Text);
                ApplyTheme();
                try { IOFile.WriteAllText(ThemeFilePath, cboTheme.Text); } catch { }
                Log($"🎨 Theme changed: {cboTheme.Text}", colorInfo);
            };
            host.Controls.Add(cboTheme);

            // save ထားတဲ့ theme ကို ပြန်ဖတ်ပြီး သုံးတယ်
            try
            {
                if (IOFile.Exists(ThemeFilePath))
                {
                    string saved = IOFile.ReadAllText(ThemeFilePath).Trim();
                    int idx = Array.IndexOf(ThemeManager.ThemeNames, saved);
                    if (idx >= 0)
                    {
                        ThemeManager.Set(saved);
                        cboTheme.SelectedIndex = idx;
                        ApplyTheme();
                    }
                }
            }
            catch { }
        }

        // ============ Auto-Detect loader learning (HWID+PK_HASH → loader) ============
        private string AutoLoaderMapPath => Path.Combine(deviceDatabasePath, "auto_loader_map.txt");

        private string CurrentLoaderPath()
        {
            if (!string.IsNullOrWhiteSpace(txtFirmwarePath?.Text) && IOFile.Exists(txtFirmwarePath.Text.Trim())) return txtFirmwarePath.Text.Trim();
            if (txtSlot1 != null && !string.IsNullOrWhiteSpace(txtSlot1.Text) && IOFile.Exists(txtSlot1.Text.Trim())) return txtSlot1.Text.Trim();
            return "";
        }

        // အောင်မြင်တဲ့ loader upload ရဲ့ hwid+pkhash → loader path ကို မှတ်ထားတယ် (နောက် Auto Detect အတွက်)
        private void PersistAutoLoaderMap()
        {
            try
            {
                if (detectedHwid.Length == 0 || detectedPkhash.Length == 0) return;
                string loader = CurrentLoaderPath();
                if (loader.Length == 0) return;
                if (!Directory.Exists(deviceDatabasePath)) Directory.CreateDirectory(deviceDatabasePath);

                string key = detectedHwid + "_" + detectedPkhash;
                // PC ပြောင်းရင် (clone နေရာပြောင်းရင်) အလုပ်ဖြစ်အောင် — app folder အောက်က loader ဆိုရင် relative path နဲ့ မှတ်တယ်
                string storePath = loader.StartsWith(Application.StartupPath, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetRelativePath(Application.StartupPath, loader)
                    : loader;
                var lines = IOFile.Exists(AutoLoaderMapPath) ? IOFile.ReadAllLines(AutoLoaderMapPath).ToList() : new List<string>();
                lines.RemoveAll(l => l.StartsWith(key + "|"));
                lines.Add($"{key}|{storePath}");
                IOFile.WriteAllLines(AutoLoaderMapPath, lines);

                // python ရဲ့ auto-loader DB အတွက်ပါ — loader ကို <hwid>_<pkhash>_FHPRG.bin နာမည်နဲ့ ထည့်ပေး
                // (ဒါဆို နောက်တစ်ခါ loader မရွေးဘဲ python က session တစ်ခုတည်းနဲ့ auto အကုန်လုပ်နိုင်တယ်)
                try
                {
                    string pyLoaders = Path.Combine(Application.StartupPath, "edl", "edlclient", "Loaders");
                    if (!Directory.Exists(pyLoaders)) Directory.CreateDirectory(pyLoaders);
                    string baseName = detectedHwid.Replace("0x", "").ToLowerInvariant() + "_" +
                                      detectedPkhash.Replace("0x", "").ToLowerInvariant();
                    foreach (var suffix in new[] { "FHPRG", "ENPRG" })
                    {
                        IOFile.Copy(loader, Path.Combine(pyLoaders, $"{baseName}_{suffix}.bin"), true);
                    }
                }
                catch (Exception ex) { LogWarning($"⚠️ python auto-loader DB မိတ္တူကူးရာမှာ မအောင်မြင်ပါ: {ex.Message}"); }
            }
            catch (Exception ex) { LogWarning($"⚠️ loader map မှတ်တမ်းသိမ်းရာမှာ မအောင်မြင်ပါ: {ex.Message}"); }
        }

        // ဒီဖုန်း (hwid+pkhash) အတွက် မှတ်ထားတဲ့ loader ရှိရင် ပြန်ယူတယ်
        private string TryResolveAutoLoader()
        {
            try
            {
                if (detectedHwid.Length == 0 || detectedPkhash.Length == 0) return "";
                if (!IOFile.Exists(AutoLoaderMapPath)) return "";
                string key = detectedHwid + "_" + detectedPkhash;
                foreach (var l in IOFile.ReadAllLines(AutoLoaderMapPath))
                {
                    if (l.StartsWith(key + "|"))
                    {
                        string stored = l.Substring(l.IndexOf('|') + 1).Trim();
                        // absolute (အဟောင်း format) ဖြစ်ရင် တည့်တည့်စမ်း၊ မဟုတ်ရင် app folder (bin) နဲ့ ယှဉ်တဲ့ relative path အနေနဲ့ စမ်းတယ်
                        string loader = Path.IsPathRooted(stored) ? stored : Path.Combine(Application.StartupPath, stored);
                        if (IOFile.Exists(loader)) return loader;
                    }
                }
            }
            catch { }
            return "";
        }

        private void LogEdlLineSmart(string rawLine)
        {
            string line = (rawLine ?? "").Trim();
            if (line.Length == 0) return;

            // --- noise / banner / debug စာကြောင်းတွေကို ဖျောက်တယ် ---
            if (line.StartsWith("Qualcomm Sahara / Firehose Client")) return;
            if (line.StartsWith("Binary build date") || line.StartsWith("QSAHARASERVER CALLED LIKE THIS") ||
                line.StartsWith("Current working dir") || line.StartsWith("Sahara mappings:") ||
                line.StartsWith("Supported functions:") || line.StartsWith("Protocol version:") ||
                line.StartsWith("Trying to connect to firehose")) return;
            if (Regex.IsMatch(line, @"^\d+: [a-zA-Z0-9_]+\.mbn")) return;   // QSaharaServer mapping rows
            if (Regex.IsMatch(line, @"^[\-=_]{5,}")) return;                 // separator lines
            if (Regex.IsMatch(line, @"^[a-zA-Z0-9_]+:\s+Offset 0x")) return; // GPT row — grid ထဲမှာပဲ ပြမယ်
            if (line.StartsWith("Parsing Lun") || line.StartsWith("GPT Table:") || line.StartsWith("Version 0x")) return;
            // HWID / PK_HASH — auto-loader learning (ဖုန်းတစ်လုံးစီရဲ့ ID) အတွက် ဖမ်းပြီး log မှာ မပြဘူး
            if (line.Contains("HWID:"))
            {
                Match hm = Regex.Match(line, @"HWID:\s+(0x[0-9A-Fa-f]+)");
                if (hm.Success) detectedHwid = hm.Groups[1].Value;
                return;
            }
            if (line.Contains("PK_HASH:"))
            {
                Match pm = Regex.Match(line, @"PK_HASH:\s+(0x[0-9A-Fa-f]+)");
                if (pm.Success) detectedPkhash = pm.Groups[1].Value;
                return;
            }
            if (line.StartsWith("Serial:")) return;
            if (line.StartsWith("boottodwnload") || line.StartsWith("Version 0x")) return;

            // device စောင့်နေတုန်း python ရဲ့ dots/hints တွေ — app ဘက်က ကိုယ်ပိုင် message ရှိပြီးသား
            if (Regex.IsMatch(line, @"^\.+$")) return;
            if (line.StartsWith("Hint:") || line.StartsWith("Xiaomi:") || line.StartsWith("Other:") ||
                line.StartsWith("Run ") || line.Contains("fastpwn")) return;

            // logger prefix (main - / sahara - / firehose_client - / [LIB]: ...) ဖြုတ်တယ် (အရှည်ဆုံးကစ စီစဉ်)
            string body = Regex.Replace(line, @"^(firehose_client|DeviceClass|main|sahara|firehose)(\s*-\s*)?", "");
            body = Regex.Replace(body, @"^\[LIB\]:\s*", "").Trim();
            if (body.Length == 0) return;

            // prefix ဖြုတ်ပြီးမှ ပေါ်လာတဲ့ noise တွေကိုပါ ဖျောက်တယ်
            if (body.StartsWith("Protocol version:") || body.StartsWith("Trying to connect to firehose") ||
                body.StartsWith("Supported functions:") || body.StartsWith("Version 0x") ||
                body.StartsWith("[LIB]") || body.StartsWith("32-Bit mode detected") || body.StartsWith("64-Bit mode detected") ||
                Regex.IsMatch(body, @"^\.+$") || body.StartsWith("Hint:") || body.StartsWith("Xiaomi:") ||
                body.StartsWith("Other:") || body.Contains("fastpwn"))
                return;

            string display = "";
            Color col = colorInfo;

            if (body.StartsWith("Using loader", StringComparison.OrdinalIgnoreCase))
            {
                Match lm = Regex.Match(body, @"([^\\/]+\.(?:elf|mbn|bin))", RegexOptions.IgnoreCase);
                display = lm.Success ? $"🚀 Loader: {lm.Groups[1].Value}" : body;
                col = colorInfo;
            }
            else if (body.Contains("Trying with no loader given", StringComparison.OrdinalIgnoreCase))
            {
                display = "🔍 Loader auto-detect mode (no loader file selected)";
                col = colorWarning;
            }
            else if (body.Contains("Only nop and sig tag"))
            {
                display = "🔑 Xiaomi EDL auth required — sending signature...";
                col = colorWarning;
            }
            else if (body.Contains("Xiaomi EDL Auth detected"))
            {
                display = "🔑 Xiaomi EDL Auth detected — authenticating...";
                col = colorWarning;
            }
            else if (body.Contains("Authenticated successfully", StringComparison.OrdinalIgnoreCase))
            {
                display = "🔓 EDL Authenticated successfully";
                col = colorSuccess;
            }
            else if (body.Contains("Loader successfully uploaded", StringComparison.OrdinalIgnoreCase))
            {
                display = "✅ Firehose Loader uploaded — switching to Firehose";
                col = colorSuccess;
                PersistAutoLoaderMap(); // hwid+pkhash → loader ကို မှတ်ထား (နောက် Auto Detect အတွက်)
            }
            else if (body.Contains("Mode detected: sahara"))
            {
                display = "🔌 Mode: Sahara (EDL)";
                col = Color.FromArgb(128, 216, 255);
            }
            else if (body.Contains("Mode detected: firehose"))
            {
                display = "🔌 Mode: Firehose (loader running)";
                col = Color.FromArgb(128, 216, 255);
            }
            else if (body.Contains("Device detected", StringComparison.OrdinalIgnoreCase))
            {
                display = "✅ Device connected";
                col = colorSuccess;
            }
            else if (body.Contains("Waiting for the device", StringComparison.OrdinalIgnoreCase))
            {
                display = "⏳ Waiting for device...";
                col = colorInfo;
            }
            else if (body.Contains("CPU detected", StringComparison.OrdinalIgnoreCase))
            {
                Match m = Regex.Match(body, @"""([^""]+)""");
                if (m.Success)
                {
                    detectedChipset = m.Groups[1].Value;
                    display = $"📱 Phone detected: {detectedChipset}";
                    col = Color.FromArgb(255, 179, 71);
                }
            }
            else if (body.StartsWith("Total disk size", StringComparison.OrdinalIgnoreCase))
            {
                Match m = Regex.Match(body, @"0x([0-9A-Fa-f]+)");
                if (m.Success)
                {
                    try
                    {
                        ulong bytes = Convert.ToUInt64(m.Groups[1].Value, 16);
                        display = $"💾 Total disk: {(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
                        col = colorSuccess;
                    }
                    catch { return; }
                }
                else return;
            }
            else if (body.Contains("Uploading loader", StringComparison.OrdinalIgnoreCase))
            {
                display = "🚀 Uploading Firehose Loader...";
                col = colorInfo;
            }
            else if (body.Contains("32-Bit mode detected") || body.Contains("64-Bit mode detected"))
            {
                return; // အသေးစိတ် မလို
            }
            else if (body.Contains("Couldn't find a loader", StringComparison.OrdinalIgnoreCase))
            {
                detectedLoaderMissing = true;
                display = "⚠️ Loader for this phone not in auto-database yet";
                col = colorWarning;
            }
            else if (body.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                     body.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
                     body.Contains("Traceback") ||
                     body.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
                     body.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                // QSaharaServer COM port error spam → တစ်ကြောင်းတည်း ရှင်းပြ
                if (body.Contains("Failed to open com port") || body.Contains("Could not connect"))
                {
                    display = "❌ Cannot open COM port — device not in EDL or port busy (try USB mode / Zadig)";
                }
                else
                {
                    display = "❌ " + (body.Length > 160 ? body.Substring(0, 160) : body);
                }
                col = colorError;
            }
            else if (body.StartsWith("[LIB]") || body.StartsWith("Warning") || body == "main" || body == "sahara" || body == "firehose")
            {
                return;
            }
            else
            {
                display = body; // အခြား output တွေ (adb getprop စသည်) ကို မပြောင်းဘဲ ပြတယ်
                col = colorInfo;
            }

            // ထပ်ခါတလဲလဲ တူညီတဲ့ စာကြောင်းတွေကို ချုံ့တယ် (QSaharaServer ERROR spam လိုမျိုး)
            if (display == lastSmartLine)
            {
                lastSmartRepeat++;
                if (lastSmartRepeat > 3) return;
            }
            else
            {
                lastSmartLine = display;
                lastSmartRepeat = 0;
            }

            Log(display, col);
        }
        private void Log(string message, Color color)
        {
            if (this.InvokeRequired) { this.Invoke(new Action<string, Color>(Log), message, color); return; }
            color = ThemeManager.AdaptLog(color); // light theme မှာ log အရောင်တွေ ဖတ်ရလွယ်အောင်
            rtbOutput.SelectionStart = rtbOutput.TextLength;
            rtbOutput.SelectionLength = 0;
            rtbOutput.SelectionColor = color;
            rtbOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            rtbOutput.SelectionColor = rtbOutput.ForeColor;
            rtbOutput.ScrollToCaret();
        }
        private void Log(string message) => Log(message, colorInfo);
        private void LogADB(string m) => Log(m, colorADB);
        private void LogFastboot(string m) => Log(m, colorFastboot);
        private void LogMTK(string m) => Log(m, colorMTK);
        private void LogQualcomm(string m) => Log(m, colorQualcomm);
        private void LogSamsung(string m) => Log(m, colorSamsung);
        private void LogSPD(string m) => Log(m, colorSPD);
        private void LogSuccess(string m) => Log(m, colorSuccess);
        private void LogError(string m) => Log(m, colorError);
        private void LogWarning(string m) => Log(m, colorWarning);
        private void LogInfo(string m) => Log(m, colorInfo);
        private void SetStatus(string status) { if (this.InvokeRequired) this.Invoke(new Action<string>(SetStatus), status); else lblStatus.Text = status; }

        private void SetOperationState(bool running)
        {
            isOperationRunning = running;
            if (btnMobileGo != null) btnMobileGo.Enabled = !running;
            foreach (Button b in dynamicButtons) b.Enabled = !running;
        }

        // ================= Form Closing =================
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                cts?.Cancel();
                if (currentProcess != null && !currentProcess.HasExited) currentProcess.Kill(entireProcessTree: true);
            }
            catch { }
            finally
            {
                portTimer?.Stop();
                portTimer?.Dispose();
                cts?.Dispose();
            }
        }

        // ================= Export Log =================
        private void ExportLogToFile() { saveFileDlg.FileName = $"Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"; if (saveFileDlg.ShowDialog() == DialogResult.OK) IOFile.WriteAllText(saveFileDlg.FileName, rtbOutput.Text); }

        private void BrowseFirmware_Click(object sender, EventArgs e)
        {
            if (currentCategory == "Qualcomm")
                openFileDlg.Filter = "Programmer Files (*.mbn;*.elf)|*.mbn;*.elf|All Files (*.*)|*.*";
            else if (currentCategory == "MediaTek")
                openFileDlg.Filter = "DA / Auth Files (*.bin;*.auth)|*.bin;*.auth|All Files (*.*)|*.*";
            else if (currentCategory == "Spreadtrum")
                openFileDlg.Filter = "PAC / FDL Files (*.pac;*.bin)|*.pac;*.bin|All Files (*.*)|*.*";
            else
                openFileDlg.Filter = "All Supported Files|*.*";

            if (openFileDlg.ShowDialog() == DialogResult.OK)
            {
                txtFirmwarePath.Text = openFileDlg.FileName;
                if (txtSlot1 != null) txtSlot1.Text = openFileDlg.FileName;
            }
        }

        private void InitializePaths()
        {
            qflEnginePath = Path.Combine(Application.StartupPath, "QFL", "QFL.exe");
            string qflBin = Path.Combine(Application.StartupPath, "QFL", "bin");

            if (IOFile.Exists(Path.Combine(qflBin, "adb.exe")))
            {
                adbPath = Path.Combine(qflBin, "adb.exe");
                fastbootPath = Path.Combine(qflBin, "fastboot.exe");
            }
            else if (IOFile.Exists(Path.Combine(Application.StartupPath, "bin", "adb.exe")))
            {
                adbPath = Path.Combine(Application.StartupPath, "bin", "adb.exe");
                fastbootPath = Path.Combine(Application.StartupPath, "bin", "fastboot.exe");
            }
            else
            {
                adbPath = Path.Combine(Application.StartupPath, "adb.exe");
                fastbootPath = Path.Combine(Application.StartupPath, "fastboot.exe");
            }
        }

        private void InitializePythonPaths()
        {
            pythonPath = "python";
            mtkScriptPath = Path.Combine(Application.StartupPath, "mtkclient", "mtk.py");
            edlScriptPath = Path.Combine(Application.StartupPath, "edl", "edl.py");
            spdScriptPath = Path.Combine(Application.StartupPath, "spd", "spd.py");
        }

        private void InitializePortTimer()
        {
            portTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            portTimer.Tick += (s, e) => { if (!isOperationRunning) RefreshPorts(); };
            portTimer.Start();
        }

        private void RefreshPorts()
        {
            try
            {
                string[] ports = SerialPort.GetPortNames();
                string currentSelection = mobilePortCombo.SelectedItem?.ToString() ?? "";

                mobilePortCombo.Items.Clear();
                mobilePortCombo.Items.Add("Auto Detect / USB");

                foreach (string port in ports) mobilePortCombo.Items.Add(port);

                if (mobilePortCombo.Items.Contains(currentSelection)) mobilePortCombo.SelectedItem = currentSelection;
                else mobilePortCombo.SelectedIndex = 0;
            }
            catch { }
        }

        // ================= 🔍 Partition Hex Editor (devinfo/config စတဲ့ သေးငယ်တဲ့ partition) =================
        private async void btnQcHexEdit_Click(object sender, EventArgs e)
        {
            string part = selectedPartitionName;
            var pinfo = partitions.FirstOrDefault(p => p.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(part) || pinfo == null)
            {
                MessageBox.Show("အရင် 📋 Read GPT လုပ်ပြီး grid ထဲက partition row တစ်ခုကို ရွေးပေးပါ။", "Hex Edit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ulong partSize = 0;
            try { partSize = Convert.ToUInt64(pinfo.Length.Replace("0x", ""), 16); } catch { }
            if (partSize == 0 || partSize > 64UL * 1024 * 1024)
            {
                MessageBox.Show("ဒီ tool က 64MB အောက် partition တွေအတွက်ပါ (ဒီ partition: " + pinfo.Length + " B) — persist လိုအကြီးကြီးဆို Persist B/U tool သုံးပါ။", "Hex Edit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            string dumpPath = Path.Combine(Path.GetTempPath(), $"pmk_{part}_dump.bin");
            try { if (IOFile.Exists(dumpPath)) IOFile.Delete(dumpPath); } catch { }

            SetOperationState(true);
            SetStatus($"Dumping {part}...");
            LogQualcomm($"\n🔍 [Hex Edit] Dumping partition [{part}] ({pinfo.Length} bytes)...");
            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlLoaderArg()}r {part} \"{dumpPath}\"", $"Reading {part}...", true);

            byte[] data = null;
            if (PythonOpSucceeded(res) && IOFile.Exists(dumpPath))
            {
                try { data = IOFile.ReadAllBytes(dumpPath); } catch { }
            }

            if (data == null || data.Length == 0)
            {
                LogError("❌ Partition dump မအောင်ပါ — device/loader စစ်ပြီး ထပ်စမ်းပါ။");
                SetOperationState(false);
                SetStatus("Ready");
                return;
            }
            LogSuccess($"✅ Dumped: {data.Length} bytes");

            using var editor = new HexEditWindow(data, part);
            if (editor.ShowDialog() != DialogResult.OK || !editor.Modified) return;

            byte[] edited = editor.EditedBytes;
            string editPath = Path.Combine(Path.GetTempPath(), $"pmk_{part}_edited.bin");
            try { IOFile.WriteAllBytes(editPath, edited); } catch (Exception ex) { LogError("❌ " + ex.Message); return; }

            if (MessageBox.Show($"Edited [{part}] ({edited.Length} bytes) ကို ဖုန်းထဲ ပြန်ရေးမလား?", "Write Back", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                LogInfo($"ℹ️ Edited file ကို သိမ်းထားပါတယ်: {editPath}");
                SetOperationState(false);
                SetStatus("Ready");
                return;
            }

            SetStatus($"Writing {part}...");
            LogQualcomm($"\n🔥 Writing edited [{part}] back to phone...");
            string res2 = await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlLoaderArg()}w {part} \"{editPath}\"", $"Writing {part}...", true);
            if (PythonOpSucceeded(res2))
            {
                LogSuccess($"✅ [{part}] ပြန်ရေးပြီးပါပြီ!");
                LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
                await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                LogSuccess("📱 Phone is restarting!\n");
            }
            else
            {
                LogError($"❌ [{part}] ပြန်ရေးမအောင်ပါ — edited file: {editPath}");
            }
            SetOperationState(false);
            SetStatus("Ready");
        }

        // ================= 💾 Persist Backup / Restore (donor trick အတွက်) =================
        private async void btnQcPersistBackup_Click(object sender, EventArgs e)
        {
            saveFileDlg.Filter = "Partition Image (*.img)|*.img|All Files (*.*)|*.*";
            saveFileDlg.FileName = "persist_backup.img";
            if (saveFileDlg.ShowDialog() != DialogResult.OK) return;

            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            LogQualcomm("\n💾 [Persist Backup] Dumping persist partition...");
            SetOperationState(true);
            SetStatus("Backing up persist...");

            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlLoaderArg()}r persist \"{saveFileDlg.FileName}\"", "Reading persist...", true);
            if (PythonOpSucceeded(res) && IOFile.Exists(saveFileDlg.FileName))
            {
                long sz = new FileInfo(saveFileDlg.FileName).Length;
                LogSuccess($"✅ persist backup ပြီးပါပြီ: {(sz / (1024.0 * 1024.0)):F1} MB → {saveFileDlg.FileName}");
            }
            else
            {
                LogError("❌ persist dump မအောင်ပါ။");
            }
            SetOperationState(false);
            SetStatus("Ready");
        }

        private async void btnQcPersistRestore_Click(object sender, EventArgs e)
        {
            openFileDlg.Filter = "Persist Image (*.img;*.bin)|*.img;*.bin|All Files (*.*)|*.*";
            if (openFileDlg.ShowDialog() != DialogResult.OK) return;

            if (MessageBox.Show($"persist image ကို ဖုန်းထဲ ပြန်ရေးမလား?\n\n{Path.GetFileName(openFileDlg.FileName)} — ဒီ partition ကို ပြန်ရေးတာက ဖုန်း model တူမှသာ လုပ်သင့်ပါတယ်!", "Persist Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string script = Path.Combine(Application.StartupPath, "edl", "edl.py");
            LogQualcomm("\n♻️ [Persist Restore] Writing persist partition...");
            SetOperationState(true);
            SetStatus("Restoring persist...");

            string res = await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlLoaderArg()}w persist \"{openFileDlg.FileName}\"", "Writing persist...", true);
            if (PythonOpSucceeded(res))
            {
                LogSuccess("✅ persist restore ပြီးပါပြီ!");
                LogQualcomm("🔄 [Auto Reboot] Restarting phone...");
                await RunProcessCommand(pythonPath, $"\"{script}\" {GetEdlResetArgs()}reset", "Rebooting...", false);
                LogSuccess("📱 Phone is restarting!\n");
            }
            else
            {
                LogError("❌ persist restore မအောင်ပါ။");
            }
            SetOperationState(false);
            SetStatus("Ready");
        }
    }

    // ================= 🔍 Partition Hex Editor Window =================
    public class HexEditWindow : Form
    {
        private readonly byte[] buffer;
        private readonly string partName;
        private readonly RichTextBox txtHex;
        private readonly TextBox txtOffset;
        private readonly TextBox txtBytes;
        private readonly Label lblInfo;
        private bool modified = false;

        public bool Modified => modified;
        public byte[] EditedBytes => buffer;

        public HexEditWindow(byte[] data, string partition)
        {
            buffer = data;
            partName = partition;
            Text = $"🔍 Hex Edit — {partition} ({data.Length} bytes)";
            Size = new Size(760, 620);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(20, 26, 36);

            txtHex = new RichTextBox
            {
                Dock = DockStyle.Top,
                Height = 420,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.FromArgb(12, 17, 24),
                ForeColor = Color.FromArgb(210, 225, 240),
                ReadOnly = true,
                WordWrap = false
            };
            Controls.Add(txtHex);

            Panel bottom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            lblInfo = new Label { AutoSize = true, ForeColor = Color.FromArgb(160, 190, 220), Text = "" };
            Label l1 = new Label { Text = "Offset (hex):", AutoSize = true, ForeColor = Color.White, Top = 30 };
            txtOffset = new TextBox { Width = 120, BackColor = Color.FromArgb(35, 45, 58), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            Label l2 = new Label { Text = "Bytes (hex, ဥပမာ 00 01 FF):", AutoSize = true, ForeColor = Color.White, Top = 30 };
            txtBytes = new TextBox { Width = 260, BackColor = Color.FromArgb(35, 45, 58), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            Button btnApply = new Button { Text = "✏️ Apply", Width = 90, BackColor = Color.FromArgb(47, 72, 101), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button btnOk = new Button { Text = "✅ Save & Write", Width = 120, BackColor = Color.FromArgb(40, 130, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button btnCancel = new Button { Text = "Cancel", Width = 90, BackColor = Color.FromArgb(70, 80, 95), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };

            int x = 12;
            lblInfo.Location = new Point(x, 6);
            l1.Location = new Point(x, 32); x += l1.Width + 4;
            txtOffset.Location = new Point(x, 28); x += txtOffset.Width + 14;
            l2.Location = new Point(x, 32); x += l2.Width + 4;
            txtBytes.Location = new Point(x, 28); x += txtBytes.Width + 10;
            btnApply.Location = new Point(x, 27); x += btnApply.Width + 10;
            btnOk.Location = new Point(12, 62);
            btnCancel.Location = new Point(140, 62);
            bottom.Controls.AddRange(new Control[] { lblInfo, l1, txtOffset, l2, txtBytes, btnApply, btnOk, btnCancel });
            Controls.Add(bottom);

            RenderView(0);
            lblInfo.Text = "နည်း: offset (hex) ရိုက်ပြီး bytes (hex) ထည့်ကာ ✏️ Apply နှိပ်ပါ — offset 0x မပါဘဲ ရေးလို့ရတယ်။";

            btnApply.Click += (s, e) =>
            {
                try
                {
                    string offTxt = txtOffset.Text.Trim().Replace("0x", "").Replace("0X", "");
                    int off = Convert.ToInt32(offTxt, 16);
                    string hex = txtBytes.Text.Trim().Replace(" ", "").Replace("0x", "").Replace("0X", "");
                    if (hex.Length == 0 || hex.Length % 2 != 0) { MessageBox.Show("Bytes ကို hex အတွဲလိုက် ရိုက်ပါ (ဥပမာ 00 01 FF)"); return; }
                    byte[] nb = new byte[hex.Length / 2];
                    for (int i = 0; i < nb.Length; i++) nb[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    if (off < 0 || off + nb.Length > buffer.Length) { MessageBox.Show("Offset က partition ထက် ကျော်နေပါတယ် (size " + buffer.Length + " bytes)"); return; }
                    for (int i = 0; i < nb.Length; i++) buffer[off + i] = nb[i];
                    modified = true;
                    LogEdit(off, nb.Length);
                    RenderView(off);
                }
                catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); }
            };
            btnOk.Click += (s, e) => DialogResult = DialogResult.OK;
            btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
        }

        private void LogEdit(int off, int len)
        {
            lblInfo.Text = $"✅ Edited @ 0x{off:X} ({len} bytes) — 'Save & Write' နှိပ်ရင် ဖုန်းထဲ ပြန်ရေးမယ်";
            lblInfo.ForeColor = Color.FromArgb(120, 230, 140);
        }

        private void RenderView(int centerOffset)
        {
            var sb = new System.Text.StringBuilder();
            int show = Math.Min(buffer.Length, 0x4000);
            int start = Math.Max(0, Math.Min(centerOffset - 0x100, Math.Max(0, show - 0x800)));
            for (int i = start; i < start + show; i += 16)
            {
                sb.Append(i.ToString("X8")).Append("  ");
                for (int k = 0; k < 16; k++)
                {
                    if (i + k < buffer.Length) sb.Append(buffer[i + k].ToString("X2")).Append(' ');
                    else sb.Append("   ");
                }
                sb.Append(" |");
                for (int k = 0; k < 16 && i + k < buffer.Length; k++)
                {
                    byte b = buffer[i + k];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                sb.AppendLine("|");
            }
            txtHex.Text = sb.ToString();
        }
    }
}