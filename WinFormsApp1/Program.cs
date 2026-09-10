using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinFormsApp1
{
    // Window ကို ရှေ့ဆုံး ဆွဲတင်ပေးတဲ့ အကူ — တစ်ခြား app နောက်မှာ ပိတ်နေမနေအောင်
    internal static class UiFocus
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void BringToFront(Form f)
        {
            if (f == null || f.IsDisposed) return;
            try
            {
                ShowWindow(f.Handle, 9); // SW_RESTORE
                f.Activate();
                f.BringToFront();
                SetForegroundWindow(f.Handle);
            }
            catch { }
        }
    }

    internal static class Program
    {
        // Tool က ကိုယ့် folder ထဲမှာ license.dat / users.dat / session.dat / Loaders ရေးတာမို့
        // folder က writable ဖြစ်ရမယ် (Program Files ထဲ ထည့်မိရင် ဒီ warning ပေါ်မယ်)
        private static void WarnIfFolderNotWritable()
        {
            try
            {
                string probe = System.IO.Path.Combine(AppContext.BaseDirectory, "_pmk_write_test.tmp");
                System.IO.File.WriteAllText(probe, "x");
                System.IO.File.Delete(probe);
            }
            catch
            {
                MessageBox.Show(
                    "ဒီ folder ထဲမှာ ဖိုင်ရေးလို့ မရပါ:\n" + AppContext.BaseDirectory + "\n\n" +
                    "ဒါကြောင့် activation (license.dat), account (users.dat), session (auto-login) နဲ့\n" +
                    "loader download တွေ အလုပ်မလုပ်ပါ။\n\n" +
                    "ဖြေရှင်းနည်း — PMK-Unlock-Tool-Setup.exe နဲ့ ပြန် install လုပ်ပါ (per-user:\n" +
                    "%LocalAppData%\\Programs\\PMK Unlock Tool) သို့မဟုတ် Portable zip ကို Desktop / D:\\ မှာ ဖြေပါ။",
                    "PMK Unlock Tool — Folder မှာ ရေးလို့မရပါ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Unexpected UI errors → crash.log မှာ မှတ်တယ် (shop PC တွေမှာ ဖြစ်ရင် support အတွက်)
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\r\n\r\n");
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED: {e.ExceptionObject}\r\n\r\n");
                }
                catch { }
            };

            // Force-update gate — minimum_required ထက် ငယ်နေရင် login ကို လုံးဝ မပြ (update လုပ်ခိုင်း)
            // (internet မရရင် ပိတ်မထား — warning ပဲပြ; UpdateGate.StartupWarning ကို LoginWindow က ပြ)
            if (!UpdateGate.RunStartupCheck()) return;

            // Login gate — ဝင်ပြီးမှသာ main UI ပွင့်တယ် (ပိတ်/cancel ဆို app ထွက်တယ်)
            // Session ရှိပြီး (logout မထွက်ရသေးဘဲ) license valid ဆိုရင် login ကို ကျော်တယ်
            string remembered = LoginSession.RememberedUser ?? "";   // HMAC မှန် + ရက် ၃၀ မကျော်မှ username ပြန်တယ်
            bool haveSession = remembered.Length > 0;
            if (haveSession && (!License.IsActivated || !UserManager.RestoreSession(remembered)))
            {
                LoginSession.Clear();   // session က မကိုက်တော့ဘူး (user ဖျက်ခံရ / license မရှိ) — ရှင်းလိုက်
                haveSession = false;
            }
            if (!haveSession)
            {
                using (var login = new LoginWindow())
                {
                    if (login.ShowDialog() != DialogResult.OK) return;
                }
            }

            // Folder ရေးလို့မရရင် (ဥပမာ Program Files) — license/session/loader မသိမ်းနိုင်တာ ရှင်းပြ
            WarnIfFolderNotWritable();

            Application.Run(new Form1());
        }
    }
}
