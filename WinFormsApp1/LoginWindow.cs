#nullable disable
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinFormsApp1
{
    // Tool ဖွင့်တာနဲ့ အရင်ဆုံး ပေါ်တဲ့ login gate — password မှန်မှ main UI ပွင့်တယ်
    public class LoginWindow : Form
    {
        private readonly TextBox txtUser = new TextBox();
        private readonly TextBox txtPass = new TextBox();
        private readonly Label lblError = new Label();
        private readonly Button btnLogin = new Button();

        // Force-change (default password ပြောင်းခိုင်း) view
        private readonly Panel pnlLogin = new Panel();
        private readonly Panel pnlChange = new Panel();
        private readonly TextBox txtNew1 = new TextBox();
        private readonly TextBox txtNew2 = new TextBox();
        private readonly Label lblChangeError = new Label();
        private UserEntry pendingEntry;   // password ပြောင်းပြီးမှ login ဝင်ပေးမယ့် user

        private int failCount;
        private DateTime lockUntil = DateTime.MinValue;
        private readonly System.Windows.Forms.Timer lockTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        private static readonly Color Bg = Color.FromArgb(18, 24, 32);
        private static readonly Color FieldBg = Color.FromArgb(30, 39, 52);
        private static readonly Color ErrColor = Color.FromArgb(229, 115, 115);

        public LoginWindow()
        {
            UserManager.EnsureLoaded();
            Text = "PMK MOBILE SERVICE TOOL — Login";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 430);
            BackColor = Bg;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9.5F);

            // ===== Top branding =====
            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 78,
                Text = "PMK MOBILE SERVICE TOOL",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 84, 158)
            };
            Controls.Add(header);

            // ===== Login panel =====
            pnlLogin.Location = new Point(0, 86);
            pnlLogin.Size = new Size(480, 340);
            pnlLogin.BackColor = Bg;

            var lblUser = MakeLabel("Username", 40);
            var lblPass = MakeLabel("Password", 40 + 58);

            StyleField(txtUser, 40 + 24, false);
            StyleField(txtPass, 40 + 82, true);
            txtUser.Left = 40; txtUser.Width = 400;
            txtPass.Left = 40; txtPass.Width = 400;

            StyleButton(btnLogin, "🔓  LOGIN", Color.FromArgb(21, 101, 192), 40 + 146, 400);
            btnLogin.Click += (s, e) => DoLogin();
            btnLogin.FlatStyle = FlatStyle.Flat;

            lblError.Location = new Point(40, 40 + 190);
            lblError.Size = new Size(400, 60);
            lblError.ForeColor = ErrColor;
            lblError.Font = new Font("Segoe UI", 9F);

            var lblHint = MakeLabel("Authorized users only — admin က Settings → USERS မှာ account တွေ ထည့်ပေးပါတယ်", 40 + 244);
            lblHint.ForeColor = Color.FromArgb(120, 140, 160);
            lblHint.Font = new Font("Segoe UI", 8.5F);

            pnlLogin.Controls.AddRange(new Control[] { lblUser, lblPass, txtUser, txtPass, btnLogin, lblError, lblHint });

            // ===== Change password panel (default admin password ပြောင်းဖို့) =====
            pnlChange.Location = new Point(0, 86);
            pnlChange.Size = new Size(480, 340);
            pnlChange.BackColor = Bg;

            var lblChgTitle = MakeLabel("🔑 Password အသစ် ထည့်ပါ", 24);
            lblChgTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblChgTitle.ForeColor = Color.White;

            var lblWhy = MakeLabel("ပထမဆုံး login မို့ default password ကို ပြောင်းပေးပါ (မပြောင်းရင် ဆက်မသုံးရပါ)", 52);
            lblWhy.ForeColor = Color.FromArgb(180, 200, 120);

            var lblN1 = MakeLabel("Password အသစ်", 86);
            var lblN2 = MakeLabel("Password ထပ်ရိုက်ပါ", 86 + 58);
            StyleField(txtNew1, 86 + 24, true);
            StyleField(txtNew2, 86 + 82, true);
            txtNew1.Left = 40; txtNew1.Width = 400;
            txtNew2.Left = 40; txtNew2.Width = 400;

            var btnSave = new Button();
            StyleButton(btnSave, "✅  SAVE & ENTER", Color.FromArgb(40, 130, 80), 86 + 146, 400);
            btnSave.Click += (s, e) => DoChange();

            lblChangeError.Location = new Point(40, 86 + 190);
            lblChangeError.Size = new Size(400, 60);
            lblChangeError.ForeColor = ErrColor;
            lblChangeError.Font = new Font("Segoe UI", 9F);

            pnlChange.Controls.AddRange(new Control[] { lblChgTitle, lblWhy, lblN1, lblN2, txtNew1, txtNew2, btnSave, lblChangeError });
            Controls.Add(pnlLogin);
            Controls.Add(pnlChange);
            pnlChange.Visible = false;

            // အောက်ခြေ version
            var lblVer = MakeLabel("", 400);
            lblVer.Text = $"PMK Unlock Tool v{Application.ProductVersion.Split('+')[0]}  ·  Offline member access";
            lblVer.ForeColor = Color.FromArgb(100, 120, 140);
            lblVer.Font = new Font("Segoe UI", 8F);
            Controls.Add(lblVer);

            AcceptButton = btnLogin;
            Shown += (s, e) => { txtUser.Focus(); };
            lockTimer.Tick += (s, e) => UpdateLockState();
            txtPass.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoLogin(); };
            txtUser.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoLogin(); };
            txtNew2.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoChange(); };
        }

        // ================= Helpers =================
        private Label MakeLabel(string text, int y)
        {
            var l = new Label
            {
                Text = text,
                Location = new Point(40, y),
                AutoSize = false,
                Size = new Size(400, 22),
                ForeColor = Color.FromArgb(210, 222, 235)
            };
            return l;
        }

        private static void StyleField(TextBox t, int y, bool secret)
        {
            t.Location = new Point(40, y);
            t.Height = 30;
            t.BackColor = FieldBg;
            t.ForeColor = Color.White;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = new Font("Segoe UI", 10.5F);
            if (secret) t.UseSystemPasswordChar = true;
        }

        private static void StyleButton(Button b, string text, Color back, int y, int w)
        {
            b.Text = text;
            b.Location = new Point(40, y);
            b.Size = new Size(w, 36);
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
        }

        // ================= Lock: မှား ၅ ခါ → 30s စောင့် =================
        private void UpdateLockState()
        {
            if (DateTime.Now < lockUntil)
            {
                int left = (int)Math.Ceiling((lockUntil - DateTime.Now).TotalSeconds);
                ShowError($"စမ်းမှား များနေပါတယ် — {left}s စောင့်ပါ");
                btnLogin.Enabled = false;
                return;
            }
            btnLogin.Enabled = true;
            if (failCount >= 5) { failCount = 0; lblError.Text = ""; }
        }

        private void ShowError(string msg) => lblError.Text = msg;

        private void DoLogin()
        {
            if (DateTime.Now < lockUntil) { UpdateLockState(); return; }
            if (pnlChange.Visible) return;

            string user = txtUser.Text.Trim();
            string pass = txtPass.Text;
            var entry = UserManager.TryLogin(user, pass, out string reason);
            if (entry == null)
            {
                failCount++;
                ShowError(reason);
                if (failCount >= 5)
                {
                    lockUntil = DateTime.Now.AddSeconds(30);
                    UpdateLockState();
                }
                txtPass.SelectAll();
                return;
            }

            // default password ဖြစ်နေရင် ပြောင်းမှ ဆက်လို့ရ
            if (entry.ForceChange && entry.Role != "master")
            {
                pendingEntry = entry;
                pnlLogin.Visible = false;
                pnlChange.Visible = true;
                lblChangeError.Text = "";
                txtNew1.Clear();
                txtNew2.Clear();
                txtNew1.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void DoChange()
        {
            if (pendingEntry == null) return;
            string p1 = txtNew1.Text;
            string p2 = txtNew2.Text;
            if (p1.Length < 6) { lblChangeError.Text = "Password က အနည်းဆုံး စာလုံး ၆ လုံး ရှိရမယ်"; return; }
            if (p1 == UserManager.DefaultAdminPassword) { lblChangeError.Text = "Default password ကိုပဲ ပြန်မသုံးပါနဲ့"; return; }
            if (p1 != p2) { lblChangeError.Text = "Password နှစ်ခု မတူပါ"; txtNew2.SelectAll(); return; }
            string err = UserManager.SetPassword(pendingEntry.Username, p1);
            if (err != null) { lblChangeError.Text = err; return; }
            DialogResult = DialogResult.OK;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // e.Graphics ကို dispose မလုပ်ရဘူး — framework ပိုင်တယ်
            using var grad = new LinearGradientBrush(ClientRectangle, Bg, Color.FromArgb(12, 18, 26), LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(grad, ClientRectangle);
        }
    }
}
