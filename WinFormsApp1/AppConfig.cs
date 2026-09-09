using System.IO;
using System.Windows.Forms;

namespace WinFormsApp1
{
    /// <summary>
    /// App တစ်ခုလုံးသုံးတဲ့ file / folder လမ်းကြောင်းတွေကို တစ်နေရာတည်းက ထိန်းချုပ်တဲ့ config class။
    /// Base က Application.StartupPath (tool folder) — portable ဖြစ်အောင် absolute path မသုံးဘူး။
    /// Form1.cs မှာ ပြန့်ကြဲနေတဲ့ Path.Combine(Application.StartupPath, ...) တွေကို ဒီနေရာကနေ ပြန်ခေါ်သုံးလို့ရတယ်။
    /// </summary>
    public static class AppConfig
    {
        // ================= Root =================
        /// <summary>Tool folder (exe ရှိတဲ့နေရာ) — အကုန်လုံးရဲ့ base</summary>
        public static string BaseDir => Application.StartupPath;

        // ================= Root-level files =================
        public static string AdbExe => Path.Combine(BaseDir, "adb.exe");
        public static string FastbootExe => Path.Combine(BaseDir, "fastboot.exe");
        public static string MiExe => Path.Combine(BaseDir, "mi.exe");                       // Xiaomi modified adb
        public static string AdbWinApiDll => Path.Combine(BaseDir, "AdbWinApi.dll");
        public static string AdbWinUsbApiDll => Path.Combine(BaseDir, "AdbWinUsbApi.dll");
        public static string ThemeFile => Path.Combine(BaseDir, "theme.txt");                // theme persistence
        public static string SigBinRoot => Path.Combine(BaseDir, "sig.bin");                 // Xiaomi auth sig fallback

        // ================= Main folders =================
        public static string DeviceDatabaseDir => Path.Combine(BaseDir, "device_database");  // chipset DB + auto_loader_map
        public static string LoadersDir => Path.Combine(BaseDir, "Loaders");                 // firehose loader library (Brand/Model/…)
        public static string QualcommCoreDir => Path.Combine(BaseDir, "QualcommCore");       // QSaharaServer / fh_loader
        public static string EdlDir => Path.Combine(BaseDir, "edl");                         // python EDL client
        public static string MtkClientDir => Path.Combine(BaseDir, "mtkclient");             // python MTK client
        public static string MtkFallbackDir => Path.Combine(BaseDir, "mtk");                 // mtkclient fallback location
        public static string SpdDir => Path.Combine(BaseDir, "spd");                         // python SPD (Unisoc) client
        public static string TestPointsDir => Path.Combine(BaseDir, "TestPoints");           // pinout images: TestPoints/Brand/Model.jpg
        public static string ScrcpyDir => Path.Combine(BaseDir, "scrcpy");                   // screen mirror
        public static string QflDir => Path.Combine(BaseDir, "QFL");                         // QFL native engine
        public static string DriversDir => Path.Combine(BaseDir, "Drivers");                 // driver installer (.exe) files

        // Extra loader search roots (loader auto-detect လုပ်တုန်း ဒီ folder တွေပါ ရှာပေးတယ်)
        public static string QualcommFirehosesDir => Path.Combine(BaseDir, "Qualcomm-firehoses-main");
        public static string EdlLoadersDir => Path.Combine(EdlDir, "Loaders");
        public static string EdlClientLoadersDir => Path.Combine(EdlDir, "edlclient", "Loaders");   // python auto-loader DB

        // ================= Key files inside folders =================
        public static string EdlScript => Path.Combine(EdlDir, "edl.py");
        public static string MtkScript => Path.Combine(MtkClientDir, "mtk.py");
        public static string MtkFallbackScript => Path.Combine(MtkFallbackDir, "mtk.py");
        public static string SpdScript => Path.Combine(SpdDir, "spd.py");
        public static string QSaharaServerExe => Path.Combine(QualcommCoreDir, "QSaharaServer.exe");
        public static string FhLoaderExe => Path.Combine(QualcommCoreDir, "fh_loader.exe");
        public static string QflEngineExe => Path.Combine(QflDir, "QFL.exe");
        public static string AutoLoaderMapFile => Path.Combine(DeviceDatabaseDir, "auto_loader_map.txt");
        public static string ScrcpyExe => Path.Combine(ScrcpyDir, "scrcpy.exe");
        public static string ScrcpyExeRootFallback => Path.Combine(BaseDir, "scrcpy.exe");   // scrcpy folder မရှိရင် root မှာရှာတယ်

        /// <summary>ပေါင်းစပ်လမ်းကြောင်း (ဥပမာ: AppConfig.Combine(AppConfig.LoadersDir, "Xiaomi", "SIG's", "sig.bin"))</summary>
        public static string Combine(params string[] parts) => Path.Combine(parts);

        /// <summary>အဓိက folder တွေ မရှိသေးရင် ဖန်တီးပေးတယ် (app စတင်တုန်း ခေါ်ဖို့)</summary>
        public static void EnsureCoreDirectories()
        {
            Directory.CreateDirectory(DeviceDatabaseDir);
            Directory.CreateDirectory(LoadersDir);
            Directory.CreateDirectory(QualcommCoreDir);
        }
    }
}
