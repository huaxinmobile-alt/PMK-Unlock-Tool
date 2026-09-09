#nullable disable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Per-PC license activation — Installation ID + RSA signature
    // License string: pmklic1|<PC-ID>|<ShopName>|<Expiry yyyy-MM-dd|none>|<base64 signature>
    // Private key က PMK ရဲ့ PC (tools/private/) မှာပဲ — public key ကပဲ ဒီမှာ ပါတယ်
    public sealed class LicenseData
    {
        public string MachineId;
        public string ShopName;
        public string Expiry; // "none" = အမြဲ — မဟုတ်ရင် yyyy-MM-dd
    }

    public static class License
    {
        public const string Prefix = "pmklic1";
        private const string HwidSalt = "pmk-hwid-v1:";

        // tools/private/activation_key.pub.pem (RSA-2048) — public မို့ ဒီမှာ ထည့်ထားတာ
        private const string PublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA6soCUVkTGAGjt262rxDk\n" +
            "nszhd6z8plSOiNC1tqS349r4lqvBNAFdwXVZG/3J9ibDJgodlR+a1opubrA5yNQR\n" +
            "HDsfVBtcX3a2n6Za6noWUTDHbRfzjTMNB8b3H2LW459fsMfU+zpEJtKFBVtqLtvI\n" +
            "lyL33xQTu9bq2B3ePOFf5TUZ44ZfRy4+LKokx46zL7W/MYFBsCp9eX1GG+VzAVg0\n" +
            "tGCu48PG8qKLm6qJboAJM11N+HFltxRq1+XAvY7k6FTEx9Wu0JpU4Fd16jv1xefh\n" +
            "YSvzc9zpTNwy++uggyXXxODv8MvyEpkJRIqBjn0SEsDceLO/MLs2QXb+DlCL2DYj\n" +
            "lQIDAQAB\n" +
            "-----END PUBLIC KEY-----";

        private static readonly RSA Rsa = CreateRsa();

        public static string FilePath => Path.Combine(AppConfig.BaseDir, "license.dat");

        private static RSA CreateRsa()
        {
            try
            {
                var rsa = RSA.Create();
                rsa.ImportFromPem(PublicKeyPem);
                return rsa;
            }
            catch { return null; }
        }

        // ================= Installation ID (ဒီ PC ရဲ့ သီးသန့်ကုဒ်) =================
        private static string _machineId;

        public static string MachineIdRaw
        {
            get
            {
                if (_machineId != null) return _machineId;
                string guid = null;
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                    guid = key?.GetValue("MachineGuid") as string;
                }
                catch { }
                if (string.IsNullOrWhiteSpace(guid))
                    guid = Environment.MachineName + "|" + Environment.UserName; // registry မရရင် fallback
                byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(HwidSalt + guid));
                _machineId = Convert.ToHexString(h).ToLowerInvariant().Substring(0, 32);
                return _machineId;
            }
        }

        // ABCD-EFGH-IJKL-MNOP-QRST-UVWX-YZ12-3456 ပုံစံ ပြရတယ်
        public static string MachineIdDisplay
        {
            get
            {
                string raw = MachineIdRaw;
                var sb = new StringBuilder();
                for (int i = 0; i < raw.Length; i += 4)
                {
                    if (sb.Length > 0) sb.Append('-');
                    sb.Append(raw.Substring(i, 4));
                }
                return sb.ToString();
            }
        }

        // ================= License verify =================
        // License string ကို စစ်ပြီး data ပြန် — မမှန်ရင် null
        public static LicenseData ParseAndVerify(string licenseText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(licenseText) || Rsa == null) return null;
                licenseText = licenseText.Trim();
                string[] parts = licenseText.Split('|');
                if (parts.Length != 5) return null;
                if (parts[0] != Prefix) return null;
                if (!parts[1].Equals(MachineIdRaw, StringComparison.OrdinalIgnoreCase)) return null;

                string payload = $"{parts[0]}|{parts[1]}|{parts[2]}|{parts[3]}";
                byte[] data = Encoding.UTF8.GetBytes(payload);
                byte[] sig;
                try { sig = Convert.FromBase64String(parts[4]); }
                catch { return null; }
                if (!Rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;

                // expiry format စစ်
                if (parts[3] != "none" &&
                    !DateTime.TryParseExact(parts[3], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
                    return null;

                return new LicenseData { MachineId = parts[1], ShopName = parts[2], Expiry = parts[3] };
            }
            catch { return null; }
        }

        public static bool IsExpired(LicenseData data)
        {
            if (data == null) return true;
            if (data.Expiry == "none") return false;
            return DateTime.TryParseExact(data.Expiry, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var exp)
                   && DateTime.Today > exp;
        }

        // license.dat ထဲက လက်ရှိ license (ဖတ်လို့မရ/မမှန် → null)
        public static LicenseData Current
        {
            get
            {
                try
                {
                    if (!IOFile.Exists(FilePath)) return null;
                    return ParseAndVerify(IOFile.ReadAllText(FilePath));
                }
                catch { return null; }
            }
        }

        public static bool IsActivated
        {
            get
            {
                var d = Current;
                return d != null && !IsExpired(d);
            }
        }

        public static string ShopName => Current?.ShopName ?? "";
        public static bool HasExpiry => Current != null && Current.Expiry != "none";

        // Activate — အောင်ရင် null, မအောင်ရင် error message
        public static string Activate(string licenseText)
        {
            var data = ParseAndVerify(licenseText);
            if (data == null) return "License key မမှန်ပါ — ကော်ပီအပြည့်အစုံ ရှိမရှိ စစ်ပါ (ဒီ PC ရဲ့ Installation ID နဲ့ ကိုက်ရမယ်)";
            try
            {
                IOFile.WriteAllText(FilePath, licenseText.Trim());
                return null;
            }
            catch (Exception ex)
            {
                return "license.dat သိမ်းလို့ မရဘူး: " + ex.Message;
            }
        }
    }
}
