#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    /// <summary>
    /// Login UI settings + security state — %AppData%\PMKUnlockTool\
    ///   config.json          : Remember Me / Auto-Login / username / DPAPI-encrypted password
    ///   login_state.json     : မှား ရေတွက် + lock အခြေအနေ (tool ပြန်ဖွင့်လည်း မပျောက်)
    ///   login_attempts.log   : login ကြိုးစားမှု မှတ်တမ်း (password မရေးရ)
    /// </summary>
    internal static class LoginSettings
    {
        public sealed class Config
        {
            [JsonPropertyName("remember_me")] public bool RememberMe { get; set; }
            [JsonPropertyName("auto_login")] public bool AutoLogin { get; set; }
            [JsonPropertyName("username")] public string Username { get; set; } = "";
            [JsonPropertyName("password_enc")] public string PasswordEnc { get; set; } = "";
            [JsonPropertyName("saved_at")] public string SavedAt { get; set; } = "";
            [JsonPropertyName("master_reset")] public bool MasterReset { get; set; }   // master က lock ဖြေခဲ့ရင်
        }

        public sealed class LockState
        {
            [JsonPropertyName("fails")] public int Fails { get; set; }
            [JsonPropertyName("lock_until")] public string LockUntil { get; set; } = "";
        }

        private static readonly object gate = new object();

        private static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PMKUnlockTool");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string ConfigPath => Path.Combine(Dir, "config.json");
        private static string LockPath => Path.Combine(Dir, "login_state.json");
        public static string AttemptsLogPath => Path.Combine(Dir, "login_attempts.log");
        private static string SkipFlagPath => Path.Combine(Dir, "skip_autologin.flag");

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = true };

        // ================= Saved credentials =================
        public static Config Load()
        {
            lock (gate)
            {
                try
                {
                    if (!IOFile.Exists(ConfigPath)) return new Config();
                    return JsonSerializer.Deserialize<Config>(IOFile.ReadAllText(ConfigPath)) ?? new Config();
                }
                catch { return new Config(); }
            }
        }

        private static void Save(Config c)
        {
            lock (gate)
            {
                try { IOFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(c, JsonOpts)); }
                catch { /* မရေးနိုင်ရင် ဘာမှ မလုပ် — login ကို မနှောင့်ရ */ }
            }
        }

        /// <summary>Remember Me ဖွင့်ထားရင် username + DPAPI-encrypted password ကို သိမ်း</summary>
        public static void SaveCredentials(string username, string password, bool autoLogin)
        {
            Save(new Config
            {
                RememberMe = true,
                AutoLogin = autoLogin,
                Username = (username ?? "").Trim(),
                PasswordEnc = Dpapi.Protect(password) ?? "",
                SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });
        }

        /// <summary>Remember Me ပိတ်ရင်/Logout ရင် သိမ်းထားတာ ရှင်း</summary>
        public static void ClearCredentials()
        {
            var c = Load();
            c.RememberMe = false;
            c.AutoLogin = false;
            c.Username = "";
            c.PasswordEnc = "";
            c.SavedAt = "";
            Save(c);
        }

        /// <summary>
        /// Auto-Login ကို "ဒီတစ်ခါပဲ" ကျော်ခိုင်း (Logout နှိပ်ချိန် သုံး) — preference (auto_login=true) က မပျက်ဘူး၊
        /// ဒါကြောင့် Logout လုပ်ပြီး နောက်တစ်ခါ ဖွင့်ရင် ပြန်အလိုအလျောက် ဝင်မယ်။
        /// </summary>
        public static void SuppressNextAutoLogin()
        {
            try
            {
                IOFile.WriteAllText(SkipFlagPath,
                    DateTime.Now.AddMinutes(5).ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch { }
        }

        /// <summary>ဒီတစ်ခါ Auto-Login ကို ကျော်ရမလား (flag ကို တစ်ခါပဲ သုံးပြီး ဖျက်တယ်)</summary>
        public static bool ConsumeAutoLoginSuppression()
        {
            try
            {
                if (!IOFile.Exists(SkipFlagPath)) return false;
                string txt = IOFile.ReadAllText(SkipFlagPath).Trim();
                IOFile.Delete(SkipFlagPath);
                return DateTime.TryParse(txt, out var until) && DateTime.Now <= until;
            }
            catch { return false; }
        }

        /// <summary>သိမ်းထားတဲ့ password ကို ပြန် decrypt (မအောင်ရင် null)</summary>
        public static string GetSavedPassword(Config c)
        {
            if (c == null || string.IsNullOrEmpty(c.PasswordEnc)) return null;
            return Dpapi.Unprotect(c.PasswordEnc);
        }

        // ================= Lock state (မှား ၃ ခါ → ၅ မိနစ်) =================
        public const int MaxAttempts = 3;
        public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

        public static LockState LoadLock()
        {
            lock (gate)
            {
                try
                {
                    if (!IOFile.Exists(LockPath)) return new LockState();
                    return JsonSerializer.Deserialize<LockState>(IOFile.ReadAllText(LockPath)) ?? new LockState();
                }
                catch { return new LockState(); }
            }
        }

        private static void SaveLock(LockState s)
        {
            lock (gate)
            {
                try { IOFile.WriteAllText(LockPath, JsonSerializer.Serialize(s, JsonOpts)); } catch { }
            }
        }

        public static DateTime GetLockUntil()
        {
            var s = LoadLock();
            if (DateTime.TryParse(s.LockUntil, out var dt)) return dt;
            return DateTime.MinValue;
        }

        /// <summary>မှား တစ်ခါ မှတ် — ၃ ခါ ပြည့်ရင် lock ချ (lockUntil ပြန်ပေး)</summary>
        public static DateTime RegisterFailure()
        {
            var s = LoadLock();
            s.Fails++;
            if (s.Fails >= MaxAttempts)
            {
                var until = DateTime.Now.Add(LockDuration);
                s.LockUntil = until.ToString("yyyy-MM-dd HH:mm:ss");
                s.Fails = 0;
                SaveLock(s);
                return until;
            }
            SaveLock(s);
            return DateTime.MinValue;
        }

        public static void ResetFailures()
        {
            SaveLock(new LockState());
        }

        public static int RemainingAttempts()
        {
            var s = LoadLock();
            int left = MaxAttempts - s.Fails;
            return left < 0 ? 0 : left;
        }

        // ================= Attempt log =================
        public static void LogAttempt(string username, string result)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] user='{(username ?? "").Trim()}' result={result} pc={Environment.MachineName} win={Environment.UserName}\r\n";
                IOFile.AppendAllText(AttemptsLogPath, line);
            }
            catch { }
        }

        public static List<string> ReadAttempts(int maxLines = 20)
        {
            var list = new List<string>();
            try
            {
                if (!IOFile.Exists(AttemptsLogPath)) return list;
                var all = IOFile.ReadAllLines(AttemptsLogPath);
                int start = Math.Max(0, all.Length - maxLines);
                for (int i = start; i < all.Length; i++) list.Add(all[i]);
            }
            catch { }
            return list;
        }
    }
}
