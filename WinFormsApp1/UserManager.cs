#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // users.dat ထဲက တစ်ဦးချင်းစီ — password ကို plaintext မသိမ်းဘူး (PBKDF2 hash ပဲ ရှိတယ်)
    public sealed class UserEntry
    {
        public string Username { get; set; }
        public string PasswordHash { get; set; } // base64
        public string Salt { get; set; }         // base64
        public int Iterations { get; set; }
        public string Role { get; set; }         // "master" | "admin"
        public bool ForceChange { get; set; }    // default password ဖြစ်နေလို့ ပြောင်းခိုင်းရန်
        public long CreatedUtc { get; set; }
    }

    // Offline user gate — စာရင်းကို AES-256-GCM (portable key) နဲ့ encrypt ပြီး users.dat မှာ သိမ်းတယ်။
    // ဒါက trusted ဖြန့်ဖို့ member gate — crack-proof မဟုတ်ဘူး (အပြည့်အဝ ကာကွယ်ဖို့ online license server လို)
    public class UserManager
    {
        // File ကို ဝှက်ဖို့ key အရင်းအမြစ် (obfuscation level — exe ထဲ ကြည့်ရင် တွေ့နိုင်တယ်)
        internal const string Pepper = "PMK-Unlock-Tool::userdb::v4::7d3a1f9c";
        private const string FileMagic = "PMKUF1|";
        public const string DefaultAdmin = "admin";
        public const string DefaultAdminPassword = "admin1234";
        public const string MasterName = "master";
        public const string MasterPassword = "PmK-Mst4r-Unl0ck-2026"; // PMK ကိုယ်တိုင် အတွက် — README/repo မှာ မထည့်နဲ့
        private const int Iterations = 100_000;

        private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes(Pepper));
        private static readonly List<UserEntry> Users = new List<UserEntry>();
        private static bool _loaded;

        public static string UsersFilePath => Path.Combine(AppConfig.BaseDir, "users.dat");

        // Login ဝင်ပြီးသား user (session) — Program.cs က Form1 မဖွင့်ခင် LoginWindow မှာ set
        public static UserEntry CurrentUser { get; private set; }

        public static string CurrentUsername => CurrentUser?.Username ?? "";
        public static string CurrentRole => CurrentUser?.Role ?? "";
        public static bool IsMasterSession => CurrentUser?.Role == "master";

        // ================= File load / seed =================
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (IOFile.Exists(UsersFilePath))
                {
                    string text = IOFile.ReadAllText(UsersFilePath);
                    if (!string.IsNullOrEmpty(text) && text.StartsWith(FileMagic, StringComparison.Ordinal))
                    {
                        byte[] plain = Decrypt(Convert.FromBase64String(text.Substring(FileMagic.Length)));
                        if (plain != null)
                        {
                            var list = JsonSerializer.Deserialize<List<UserEntry>>(plain);
                            if (list != null && list.Count > 0) { Users.AddRange(list); return; }
                        }
                    }
                    // ဖိုင် ပျက်စီးနေတာ — မိတ္တူယူပြီး default ပြန်စိုက်တယ်
                    try { IOFile.Copy(UsersFilePath, UsersFilePath + ".corrupt", overwrite: true); } catch { }
                }
                SeedDefaults();
            }
            catch (Exception)
            {
                SeedDefaults();
            }
        }

        private static void SeedDefaults()
        {
            Users.Clear();
            var admin = NewEntry(DefaultAdmin, DefaultAdminPassword, "admin");
            admin.ForceChange = true; // admin/admin1234 default ကို ပထမဆုံး login မှာ ပြောင်းခိုင်း
            Users.Add(admin);
            Users.Add(NewEntry(MasterName, MasterPassword, "master"));
            Save();
        }

        private static UserEntry NewEntry(string username, string password, string role)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            return new UserEntry
            {
                Username = username,
                Salt = Convert.ToBase64String(salt),
                Iterations = Iterations,
                PasswordHash = Convert.ToBase64String(HashPassword(password, salt, Iterations)),
                Role = role,
                ForceChange = false,
                CreatedUtc = DateTime.UtcNow.Ticks
            };
        }

        private static byte[] HashPassword(string password, byte[] salt, int iterations)
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        }

        // ================= Login =================
        // အောင်ရင် entry — မအောင်ရင် null (reason string နဲ့အတူ)
        public static UserEntry TryLogin(string username, string password, out string reason)
        {
            reason = "";
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                reason = "Username နဲ့ password ထည့်ပါ";
                return null;
            }
            var entry = Users.FirstOrDefault(u => u.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry == null) { reason = "Username သို့မဟုတ် password မမှန်ပါ"; return null; }
            byte[] salt = Convert.FromBase64String(entry.Salt);
            byte[] attempt = HashPassword(password, salt, entry.Iterations);
            if (!CryptographicOperations.FixedTimeEquals(attempt, Convert.FromBase64String(entry.PasswordHash)))
            {
                reason = "Username သို့မဟုတ် password မမှန်ပါ";
                return null;
            }
            CurrentUser = entry;
            return entry;
        }

        // ================= User management (admin/master) =================
        // Session remember — password မရိုက်ဘဲ နောက်ဆုံး login user ကို ပြန်တင် (Program.cs က သုံး)
        public static bool RestoreSession(string username)
        {
            EnsureLoaded();
            var entry = Find(username);
            if (entry == null) return false;
            CurrentUser = entry;
            return true;
        }

        public static List<UserEntry> ListUsers()
        {
            EnsureLoaded();
            return Users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static UserEntry Find(string username) => ListUsers().FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        public static bool IsDefaultPasswordStillActive() => Find(DefaultAdmin)?.ForceChange ?? false;

        public static bool IsDefaultPasswordFor(UserEntry e) => e?.ForceChange ?? false;

        public static string AddUser(string username, string password, string role)
        {
            EnsureLoaded();
            username = (username ?? "").Trim();
            if (username.Length < 3) return "Username က အနည်းဆုံး စာလုံး ၃ လုံး ရှိရမယ်";
            if (string.IsNullOrEmpty(password) || password.Length < 6) return "Password က အနည်းဆုံး စာလုံး ၆ လုံး ရှိရမယ်";
            if (Users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase))) return $"'{username}' က ရှိပြီးသားပါ";
            Users.Add(NewEntry(username, password, role ?? "admin"));
            return Save();
        }

        // master ကို ဘယ်သူမှ ဖျက်လို့မရ (code ကနေပဲ ပြောင်းလို့ရ)
        public static string RemoveUser(string username)
        {
            EnsureLoaded();
            var entry = Find(username);
            if (entry == null) return "User မတွေ့ဘူး";
            if (entry.Role == "master") return "Master account ကို ဖျက်လို့မရပါ";
            if (CurrentUser != null && entry.Username.Equals(CurrentUser.Username, StringComparison.OrdinalIgnoreCase))
                return "ကိုယ့်ကိုယ်ကိုယ် ဖျက်လို့မရပါ";
            Users.Remove(entry);
            return Save();
        }

        public static string SetPassword(string username, string newPassword)
        {
            EnsureLoaded();
            var entry = Find(username);
            if (entry == null) return "User မတွေ့ဘူး";
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
                return "Password က အနည်းဆုံး စာလုံး ၆ လုံး ရှိရမယ်";
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            entry.Salt = Convert.ToBase64String(salt);
            entry.Iterations = Iterations;
            entry.PasswordHash = Convert.ToBase64String(HashPassword(newPassword, salt, Iterations));
            entry.ForceChange = false;
            return Save();
        }

        // ================= File save (AES-256-GCM) =================
        // အောင်ရင် null — error ဆို message ပြန်
        private static string Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(UsersFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(Users);
                byte[] blob = Encrypt(Encoding.UTF8.GetBytes(json));
                string tmp = UsersFilePath + ".tmp";
                IOFile.WriteAllText(tmp, FileMagic + Convert.ToBase64String(blob));
                IOFile.Move(tmp, UsersFilePath, overwrite: true);
                return null;
            }
            catch (Exception ex)
            {
                return $"users.dat သိမ်းလို့ မရဘူး: {ex.Message}";
            }
        }

        private static byte[] Encrypt(byte[] plain)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] cipher = new byte[plain.Length];
            byte[] tag = new byte[16];
            using (var gcm = new AesGcm(Key, 16))
                gcm.Encrypt(nonce, plain, cipher, tag);
            byte[] outBlob = new byte[nonce.Length + tag.Length + cipher.Length];
            Buffer.BlockCopy(nonce, 0, outBlob, 0, 12);
            Buffer.BlockCopy(tag, 0, outBlob, 12, 16);
            Buffer.BlockCopy(cipher, 0, outBlob, 28, cipher.Length);
            return outBlob;
        }

        private static byte[] Decrypt(byte[] blob)
        {
            try
            {
                if (blob.Length < 29) return null;
                byte[] nonce = new byte[12];
                byte[] tag = new byte[16];
                byte[] cipher = new byte[blob.Length - 28];
                Buffer.BlockCopy(blob, 0, nonce, 0, 12);
                Buffer.BlockCopy(blob, 12, tag, 0, 16);
                Buffer.BlockCopy(blob, 28, cipher, 0, cipher.Length);
                byte[] plain = new byte[cipher.Length];
                using (var gcm = new AesGcm(Key, 16))
                    gcm.Decrypt(nonce, cipher, tag, plain);
                return plain;
            }
            catch (CryptographicException)
            {
                return null; // key မကိုက် / data ပျက်
            }
        }
    }
}
