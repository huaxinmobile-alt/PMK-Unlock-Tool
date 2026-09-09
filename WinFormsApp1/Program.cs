using System;
using System.Windows.Forms;

namespace WinFormsApp1
{
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
            using (var login = new LoginWindow())
            {
                if (login.ShowDialog() != DialogResult.OK) return;
            }

            Application.Run(new Form1());
        }
    }
}
