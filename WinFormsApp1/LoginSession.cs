#nullable disable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Login session မှတ်စရာ — login ဝင်ပြီးရင် tool ပိတ်/ပြန်ဖွင့် ပြန်မမေးတော့ဘူး (ရက် ၃၀ အထိ)။
    // Settings → Logout နှိပ်မှ session ရှင်းပြီး နောက်တစ်ခေါက် login ပြန်တောင်းတယ်။
    //
    // ဖိုင် format: pmksess1|<username>|<issued ticks>|<HMAC-SHA256 ရဲ့ ပထမ 32 hex>
    //   - HMAC ကြောင့် ဖိုင်ကို လက်ဖြင်း ပြင်လို့မရ (username/ရက်စွဲ ပြောင်းရင် signature မကိုက်)
    //   - Format အဟောင်း (username သက်သက်) ကိုလည်း ဖတ်နိုင်တယ် — ဖတ်ပြီးရင် format အသစ်နဲ့ ပြန်ရေးတယ်
    public static class LoginSession
    {
        public const int SessionDays = 30;
        private static string FilePath => Path.Combine(AppConfig.BaseDir, "session.dat");

        private static byte[] HmacKey() =>
            SHA256.HashData(Encoding.UTF8.GetBytes("pmk-session-v1|" + UserManager.Pepper));

        private static string Sign(string username, long ticks)
        {
            using var h = new HMACSHA256(HmacKey());
            byte[] mac = h.ComputeHash(Encoding.UTF8.GetBytes($"{username}|{ticks}"));
            return Convert.ToHexString(mac).Substring(0, 32).ToLowerInvariant();
        }

        public static bool Exists
        {
            get
            {
                try { return IOFile.Exists(FilePath); }
                catch { return false; }
            }
        }

        /// <summary>Session ရှိပြီး သက်တမ်း (ရက် ၃၀) မကုန်၊ HMAC မှန်ရင် အဲဒီ username ပြန်ပေး — မဟုတ်ရင် null</summary>
        public static string RememberedUser
        {
            get
            {
                try
                {
                    if (!IOFile.Exists(FilePath)) return null;
                    string raw = IOFile.ReadAllText(FilePath).Trim();
                    if (raw.Length == 0) return null;

                    if (raw.StartsWith("pmksess1|", StringComparison.Ordinal))
                    {
                        string[] p = raw.Split('|');
                        if (p.Length != 4) return null;
                        string user = p[1];
                        if (!long.TryParse(p[2], out long ticks)) return null;
                        if (!string.Equals(p[3], Sign(user, ticks), StringComparison.OrdinalIgnoreCase))
                            return null;                                  // ဖိုင် ပြင်ထားတယ် — session မယုံ
                        var issued = new DateTime(ticks, DateTimeKind.Local);
                        if (issued > DateTime.Now.AddMinutes(5)) return null;              // အနာဂတ် ရက်စွဲ
                        if ((DateTime.Now - issued).TotalDays > SessionDays) return null;  // ရက် ၃၀ ကျော်
                        return user.Length == 0 ? null : user;
                    }

                    // format အဟောင်း (username သက်သက်) — ဖတ်ပြီး format အသစ်နဲ့ ပြန်ရေးမယ်
                    if (raw.IndexOf('|') < 0)
                    {
                        Save(raw);
                        return raw;
                    }
                    return null;
                }
                catch { return null; }
            }
        }

        public static bool IsValid => RememberedUser != null;

        public static void Save(string username)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string user = (username ?? "").Trim();
                long ticks = DateTime.Now.Ticks;
                IOFile.WriteAllText(FilePath, $"pmksess1|{user}|{ticks}|{Sign(user, ticks)}");
            }
            catch { /* session save မအောင်ရင် နောက်တစ်ခါ login မေးရုံပဲ */ }
        }

        public static void Clear()
        {
            try { if (IOFile.Exists(FilePath)) IOFile.Delete(FilePath); } catch { }
        }

        /// <summary>Session စတင်ခဲ့တဲ့ အချိန် (မရှိရင် null)</summary>
        public static DateTime? IssuedAt
        {
            get
            {
                try
                {
                    if (!IOFile.Exists(FilePath)) return null;
                    string[] p = IOFile.ReadAllText(FilePath).Trim().Split('|');
                    if (p.Length == 4 && long.TryParse(p[2], out long ticks)) return new DateTime(ticks, DateTimeKind.Local);
                }
                catch { }
                return null;
            }
        }

        /// <summary>Session ကျန်တဲ့ ရက် (ကုန်ရင် 0)</summary>
        public static int DaysLeft
        {
            get
            {
                var issued = IssuedAt;
                if (issued == null) return 0;
                int left = SessionDays - (int)(DateTime.Now - issued.Value).TotalDays;
                return left < 0 ? 0 : left;
            }
        }
    }
}
