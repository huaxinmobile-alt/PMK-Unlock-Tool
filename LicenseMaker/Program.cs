using System;
using System.Windows.Forms;

namespace PMKLicenseMaker
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowCrash(e.ExceptionObject as Exception);

            Application.Run(new LicenseMakerForm());
        }

        private static void ShowCrash(Exception ex)
        {
            try
            {
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "license_maker_crash.log");
                System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
            }
            catch { }
            MessageBox.Show("Error: " + (ex?.Message ?? "unknown"), "PMK License Maker",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
