#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinFormsApp1
{
    /// <summary>
    /// Windows DPAPI (CryptProtectData / CryptUnprotectData) — user account နဲ့ ချိတ်ထားတဲ့ encryption.
    /// ဒီ PC + ဒီ Windows user မှသာ ပြန် decrypt လုပ်နိုင်တယ် (ဖိုင်ကို တစ်ခြား PC/user ကို ကူးရင် မဖွင့်နိုင်).
    /// NuGet package မလိုဘဲ crypt32.dll ကို တိုက်ရိုက် ခေါ်တယ်.
    /// </summary>
    internal static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        /// <summary>စာသားကို DPAPI နဲ့ lock ပြီး base64 string ပြန်ပေး (မအောင်ရင် null)</summary>
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return null;
            byte[] data = Encoding.UTF8.GetBytes(plainText);
            var inBlob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            try
            {
                Marshal.Copy(data, 0, inBlob.pbData, data.Length);
                if (!CryptProtectData(ref inBlob, "PMKUnlockTool", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CRYPTPROTECT_UI_FORBIDDEN, out DATA_BLOB outBlob))
                    return null;
                try
                {
                    byte[] outBytes = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, outBytes, 0, outBlob.cbData);
                    return Convert.ToBase64String(outBytes);
                }
                finally { LocalFree(outBlob.pbData); }
            }
            catch { return null; }
            finally { Marshal.FreeHGlobal(inBlob.pbData); }
        }

        /// <summary>base64 (DPAPI) ကို ပြန် decrypt — မအောင်ရင် null</summary>
        public static string Unprotect(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                byte[] data = Convert.FromBase64String(base64);
                var inBlob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
                try
                {
                    Marshal.Copy(data, 0, inBlob.pbData, data.Length);
                    if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                            CRYPTPROTECT_UI_FORBIDDEN, out DATA_BLOB outBlob))
                        return null;
                    try
                    {
                        byte[] outBytes = new byte[outBlob.cbData];
                        Marshal.Copy(outBlob.pbData, outBytes, 0, outBlob.cbData);
                        return Encoding.UTF8.GetString(outBytes);
                    }
                    finally { LocalFree(outBlob.pbData); }
                }
                finally { Marshal.FreeHGlobal(inBlob.pbData); }
            }
            catch { return null; }
        }
    }
}
