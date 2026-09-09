#nullable disable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinFormsApp1
{
    // ရိုးရှင်းတဲ့ input dialog (စာသား/လျှို့ဝှက်စာလုံး တစ်ကြောင်း ရိုက်ခိုင်းဖို့)
    // အောင်ရင် string — cancel/ပိတ်ရင် null ပြန်
    public static class InputDialog
    {
        public static string Show(string title, string prompt, bool secret = false, string initial = "")
        {
            using var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(420, 170),
                BackColor = Color.FromArgb(22, 29, 39),
                Font = new Font("Segoe UI", 9.5F)
            };

            var lbl = new Label
            {
                Text = prompt,
                Location = new Point(16, 16),
                AutoSize = false,
                Size = new Size(388, 40),
                ForeColor = Color.FromArgb(210, 222, 235)
            };

            var txt = new TextBox
            {
                Text = initial,
                Location = new Point(16, 62),
                Size = new Size(388, 28),
                BackColor = Color.FromArgb(30, 39, 52),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10.5F)
            };
            if (secret) txt.UseSystemPasswordChar = true;

            var btnOk = new Button { Text = "OK", Location = new Point(230, 110), Size = new Size(80, 32), BackColor = Color.FromArgb(21, 101, 192), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancel", Location = new Point(324, 110), Size = new Size(80, 32), BackColor = Color.FromArgb(70, 80, 95), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;

            form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
            txt.Focus();
            return form.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
        }
    }
}
