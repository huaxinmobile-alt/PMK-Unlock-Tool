#nullable disable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinFormsApp1
{
    /// <summary>
    /// Login screen မတိုင်ခင် အလုပ်လုပ်တဲ့ Force-Update gate.
    ///   · manifest ရဲ့ minimum_required ထက် version ငယ်နေရင် → login ကို လုံးဝ မပြ၊
    ///     "Download Update / ပြန်စစ် / Exit" dialog ပြပြီး update လုပ်ခိုင်းတယ်
    ///   · internet မရ / manifest မရ → ပိတ်မထားဘူး၊ warning ပဲ ပြ (offline shop PC တွေ မခံရအောင်)
    /// ရလဒ်ကို Program.cs က Main() မှာ ခေါ်တယ်: false ဆိုရင် app ထွက်.
    /// </summary>
    internal static class UpdateGate
    {
        public const string ReleasesUrl = "https://github.com/huaxinmobile-alt/PMK-Unlock-Tool/releases/latest";

        /// <summary>Update စစ်လို့ မရခဲ့ရင် ဒီမှာ message ထည့်ထား — Login window က ပြမယ် (block မလုပ်)</summary>
        public static string StartupWarning { get; private set; }

        /// <summary>true = ဆက်သွား (login/session ဆက်လုပ်) · false = user က ထွက်လိုက် (app ပိတ်)</summary>
        public static bool RunStartupCheck()
        {
            for (int attempt = 0; attempt < 20; attempt++)   // Re-check နှိပ်လို့ ထပ်စစ်တာ အများဆုံး ၂၀ ခါ
            {
                UpdateManifest m = null;
                try { m = Task.Run(() => UpdateManager.TryFetchManifestAsync(7)).GetAwaiter().GetResult(); }
                catch { m = null; }

                if (m == null)
                {
                    // offline / server မရ — block မလုပ်၊ warning ပဲ
                    StartupWarning = "⚠️ Update စစ်လို့ မရပါ (internet မရ/နှေးနေတာ) — နောက်ဆုံး version ဖြစ်ကြောင်း သေချာပါ";
                    LogCheck("FETCH-FAILED (offline?) — login ကို ခွင့်ပြု");
                    return true;
                }

                string current = UpdateManager.CurrentVersion;
                string minReq = (m.minimum_required ?? "").Trim();
                if (minReq.Length == 0 || !UpdateManager.IsBelow(current, minReq))
                {
                    StartupWarning = null;
                    LogCheck($"OK — current v{current}, minimum v{(minReq.Length == 0 ? "-" : minReq)}");
                    return true;
                }

                LogCheck($"BLOCKED — current v{current} < minimum v{minReq}");
                var res = ShowRequiredDialog(current, minReq, m);
                if (res == DialogResult.Retry) continue;   // ပြန်စစ်
                return false;                              // Exit
            }
            return true;
        }

        private static void LogCheck(string result)
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PMKUnlockTool");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "update_check.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {result} pc={Environment.MachineName}\r\n");
            }
            catch { }
        }

        // ================= UI: Mandatory Update dialog =================
        private static readonly Color Bg = Color.FromArgb(18, 24, 32);
        private static readonly Color ErrColor = Color.FromArgb(255, 138, 128);
        private static readonly Color WarnColor = Color.FromArgb(255, 213, 79);
        private static readonly Color OkColor = Color.FromArgb(0, 230, 118);
        private static readonly Color Muted = Color.FromArgb(150, 170, 190);

        private static DialogResult ShowRequiredDialog(string current, string required, UpdateManifest m)
        {
            using var dlg = new Form
            {
                Text = "PMK Unlock Tool — Mandatory Update",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(560, 380),
                BackColor = Bg,
                Font = new Font("Segoe UI", 9.5F),
                TopMost = true,
            };
            try { dlg.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 64,
                Text = "⚠️  Mandatory Update Required",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                BackColor = Color.FromArgb(140, 60, 30)
            };
            dlg.Controls.Add(header);

            var body = new Label
            {
                Location = new Point(24, 84),
                Size = new Size(512, 150),
                ForeColor = Color.FromArgb(216, 228, 240),
                Font = new Font("Segoe UI", 9.5F),
                Text =
                    "သင့် tool က ခေတ်မမီတော့ပါ — ဆက်သုံးရန် update လုပ်ရပါမယ်။\r\n" +
                    "Your version is outdated. Please update to continue.\r\n\r\n" +
                    $"• လက်ရှိ / Current        :  v{current}\r\n" +
                    $"• လိုအပ်သည် / Required  :  v{required}     ← minimum\r\n" +
                    $"• အသစ်ဆုံး / Latest        :  v{m.version}\r\n" +
                    (string.IsNullOrWhiteSpace(m.changelog) ? "" : $"\r\n📝 {m.changelog}\r\n")
            };
            dlg.Controls.Add(body);

            var lblHint = new Label
            {
                Location = new Point(24, 232),
                Size = new Size(512, 40),
                ForeColor = Muted,
                Font = new Font("Segoe UI", 8.5F),
                Text = "Download နှိပ်ရင် browser မှာ installer/zip ပွင့်မယ် → install ပြီးရင် tool ကို ပြန်ဖွင့်ပါ (ဒါမှမဟုတ် အောက်က \"ပြန်စစ်\" နှိပ်ပါ)။"
            };
            dlg.Controls.Add(lblHint);

            var btnDown = new Button
            {
                Text = "⬇  Download Update",
                Location = new Point(24, 286),
                Size = new Size(200, 40),
                BackColor = Color.FromArgb(21, 101, 192),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDown.FlatAppearance.BorderSize = 0;

            var btnRetry = new Button
            {
                Text = "🔄  ပြန်စစ် (Re-check)",
                Location = new Point(232, 286),
                Size = new Size(170, 40),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRetry.FlatAppearance.BorderSize = 0;

            var btnExit = new Button
            {
                Text = "✖  Exit",
                Location = new Point(410, 286),
                Size = new Size(126, 40),
                BackColor = Color.FromArgb(90, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnExit.FlatAppearance.BorderSize = 0;

            string url = !string.IsNullOrWhiteSpace(m.download_url) ? m.download_url : ReleasesUrl;
            btnDown.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
                lblHint.ForeColor = WarnColor;
                lblHint.Text = "Browser မှာ download page ပွင့်ပြီးပါပြီ။ Update လုပ်ပြီးရင် \"ပြန်စစ်\" နှိပ်ပါ။";
            };
            btnRetry.Click += (s, e) => { dlg.DialogResult = DialogResult.Retry; dlg.Close(); };
            btnExit.Click += (s, e) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

            dlg.Controls.Add(btnDown);
            dlg.Controls.Add(btnRetry);
            dlg.Controls.Add(btnExit);

            dlg.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(60, 80, 100));
                e.Graphics.DrawRectangle(pen, 0, 0, dlg.ClientSize.Width - 1, dlg.ClientSize.Height - 1);
            };

            var result = dlg.ShowDialog();
            return result == DialogResult.Retry ? DialogResult.Retry : DialogResult.Cancel;
        }
    }
}
