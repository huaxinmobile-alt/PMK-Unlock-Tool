using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinFormsApp1
{
    public class HexEditWindow : Form
    {
        private readonly byte[] buffer;
        private readonly string partName;
        private readonly RichTextBox txtHex;
        private readonly TextBox txtOffset;
        private readonly TextBox txtBytes;
        private readonly Label lblInfo;
        private bool modified = false;

        public bool Modified => modified;
        public byte[] EditedBytes => buffer;

        public HexEditWindow(byte[] data, string partition)
        {
            buffer = data;
            partName = partition;
            Text = $"🔍 Hex Edit — {partition} ({data.Length} bytes)";
            Size = new Size(760, 620);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(20, 26, 36);

            txtHex = new RichTextBox
            {
                Dock = DockStyle.Top,
                Height = 420,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.FromArgb(12, 17, 24),
                ForeColor = Color.FromArgb(210, 225, 240),
                ReadOnly = true,
                WordWrap = false
            };
            Controls.Add(txtHex);

            Panel bottom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            lblInfo = new Label { AutoSize = true, ForeColor = Color.FromArgb(160, 190, 220), Text = "" };
            Label l1 = new Label { Text = "Offset (hex):", AutoSize = true, ForeColor = Color.White, Top = 30 };
            txtOffset = new TextBox { Width = 120, BackColor = Color.FromArgb(35, 45, 58), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            Label l2 = new Label { Text = "Bytes (hex, ဥပမာ 00 01 FF):", AutoSize = true, ForeColor = Color.White, Top = 30 };
            txtBytes = new TextBox { Width = 260, BackColor = Color.FromArgb(35, 45, 58), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            Button btnApply = new Button { Text = "✏️ Apply", Width = 90, BackColor = Color.FromArgb(47, 72, 101), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button btnOk = new Button { Text = "✅ Save & Write", Width = 120, BackColor = Color.FromArgb(40, 130, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button btnCancel = new Button { Text = "Cancel", Width = 90, BackColor = Color.FromArgb(70, 80, 95), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };

            int x = 12;
            lblInfo.Location = new Point(x, 6);
            l1.Location = new Point(x, 32); x += l1.Width + 4;
            txtOffset.Location = new Point(x, 28); x += txtOffset.Width + 14;
            l2.Location = new Point(x, 32); x += l2.Width + 4;
            txtBytes.Location = new Point(x, 28); x += txtBytes.Width + 10;
            btnApply.Location = new Point(x, 27); x += btnApply.Width + 10;
            btnOk.Location = new Point(12, 62);
            btnCancel.Location = new Point(140, 62);
            bottom.Controls.AddRange(new Control[] { lblInfo, l1, txtOffset, l2, txtBytes, btnApply, btnOk, btnCancel });
            Controls.Add(bottom);

            RenderView(0);
            lblInfo.Text = "နည်း: offset (hex) ရိုက်ပြီး bytes (hex) ထည့်ကာ ✏️ Apply နှိပ်ပါ — offset 0x မပါဘဲ ရေးလို့ရတယ်။";

            btnApply.Click += (s, e) =>
            {
                try
                {
                    string offTxt = txtOffset.Text.Trim().Replace("0x", "").Replace("0X", "");
                    int off = Convert.ToInt32(offTxt, 16);
                    string hex = txtBytes.Text.Trim().Replace(" ", "").Replace("0x", "").Replace("0X", "");
                    if (hex.Length == 0 || hex.Length % 2 != 0) { MessageBox.Show("Bytes ကို hex အတွဲလိုက် ရိုက်ပါ (ဥပမာ 00 01 FF)"); return; }
                    byte[] nb = new byte[hex.Length / 2];
                    for (int i = 0; i < nb.Length; i++) nb[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    if (off < 0 || off + nb.Length > buffer.Length) { MessageBox.Show("Offset က partition ထက် ကျော်နေပါတယ် (size " + buffer.Length + " bytes)"); return; }
                    for (int i = 0; i < nb.Length; i++) buffer[off + i] = nb[i];
                    modified = true;
                    LogEdit(off, nb.Length);
                    RenderView(off);
                }
                catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); }
            };
            btnOk.Click += (s, e) => DialogResult = DialogResult.OK;
            btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
        }

        private void LogEdit(int off, int len)
        {
            lblInfo.Text = $"✅ Edited @ 0x{off:X} ({len} bytes) — 'Save & Write' နှိပ်ရင် ဖုန်းထဲ ပြန်ရေးမယ်";
            lblInfo.ForeColor = Color.FromArgb(120, 230, 140);
        }

        private void RenderView(int centerOffset)
        {
            var sb = new System.Text.StringBuilder();
            int show = Math.Min(buffer.Length, 0x4000);
            int start = Math.Max(0, Math.Min(centerOffset - 0x100, Math.Max(0, show - 0x800)));
            for (int i = start; i < start + show; i += 16)
            {
                sb.Append(i.ToString("X8")).Append("  ");
                for (int k = 0; k < 16; k++)
                {
                    if (i + k < buffer.Length) sb.Append(buffer[i + k].ToString("X2")).Append(' ');
                    else sb.Append("   ");
                }
                sb.Append(" |");
                for (int k = 0; k < 16 && i + k < buffer.Length; k++)
                {
                    byte b = buffer[i + k];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                sb.AppendLine("|");
            }
            txtHex.Text = sb.ToString();
        }
    }
}
