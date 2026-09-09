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

            // Login gate — ဝင်ပြီးမှသာ main UI ပွင့်တယ် (ပိတ်/cancel ဆို app ထွက်တယ်)
            // Session ရှိပြီး (logout မထွက်ရသေးဘဲ) license valid ဆိုရင် login ကို ကျော်တယ်
            string remembered = LoginSession.RememberedUser;
            if (remembered == null || !License.IsActivated || !UserManager.RestoreSession(remembered))
            {
                using (var login = new LoginWindow())
                {
                    if (login.ShowDialog() != DialogResult.OK) return;
                }
            }

            Application.Run(new Form1());
        }
    }
}
