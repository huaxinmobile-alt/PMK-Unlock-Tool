#nullable disable
using System;
using System.IO;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Login session မှတ်စရာ — login ဝင်ပြီးရင် tool ပိတ်/ပြန်ဖွင့် ပြန်မမေးတော့ဘူး။
    // Settings → Logout နှိပ်မှပဲ session ရှင်းပြီး နောက်တစ်ခေါက် login ပြန်တောင်းတယ်။
    public static class LoginSession
    {
        private static string FilePath => Path.Combine(AppConfig.BaseDir, "session.dat");

        public static bool Exists
        {
            get
            {
                try { return IOFile.Exists(FilePath); }
                catch { return false; }
            }
        }

        // နောက်ဆုံး login ဝင်ခဲ့တဲ့ username
        public static string RememberedUser
        {
            get
            {
                try
                {
                    if (!IOFile.Exists(FilePath)) return null;
                    string u = IOFile.ReadAllText(FilePath).Trim();
                    return u.Length == 0 ? null : u;
                }
                catch { return null; }
            }
        }

        public static void Save(string username)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                IOFile.WriteAllText(FilePath, (username ?? "").Trim());
            }
            catch { /* session save မအောင်ရင် နောက်တစ်ခါ login မေးရုံပဲ */ }
        }

        public static void Clear()
        {
            try { if (IOFile.Exists(FilePath)) IOFile.Delete(FilePath); } catch { }
        }
    }
}
