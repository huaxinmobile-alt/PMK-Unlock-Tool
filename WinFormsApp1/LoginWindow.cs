#nullable disable
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinFormsApp1
{
    // Tool ဖွင့်တာနဲ့ အရင်ဆုံး ပေါ်တဲ့ login gate — password မှန်မှ main UI ပွင့်တယ်။
    // "Gmail နဲ့ register" — Google OAuth တစ်ခါ register ပြီးရင် နောက်ပိုင်း offline login ပုံမှန်
    public class LoginWindow : Form
    {
        private readonly TextBox txtUser = new TextBox();
        private readonly TextBox txtPass = new TextBox();
        private readonly Label lblError = new Label();
        private readonly Button btnLogin = new Button();
        private readonly Button btnRegister = new Button();

        // Login / password-အသစ်ထည့် panels
        private readonly Panel pnlLogin = new Panel();
        private readonly Panel pnlChange = new Panel();
        private readonly Label lblChgTitle = new Label();
        private readonly Label lblChgSub = new Label();
        private readonly TextBox txtNew1 = new TextBox();
        private readonly TextBox txtNew2 = new TextBox();
        private readonly Label lblChangeError = new Label();

        // Mode: force-change (default pw) / register (Gmail verified)
        private UserEntry _pendingEntry;
        private string _pendingEmail;

        private int failCount;
        private DateTime lockUntil = DateTime.MinValue;
        private readonly System.Windows.Forms.Timer lockTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        private static readonly Color Bg = Color.FromArgb(18, 24, 32);
        private static readonly Color FieldBg = Color.FromArgb(30, 39, 52);
        private static readonly Color ErrColor = Color.FromArgb(229, 115, 115);
        private static readonly Color InfoColor = Color.FromArgb(180, 205, 235);

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

            // ================= Login panel =================
            pnlLogin.Location = new Point(0, 86);
            pnlLogin.Size = new Size(480, 340);
            pnlLogin.BackColor = Bg;

            pnlLogin.Controls.Add(MakeLabel("Username", 16));
            StyleField(txtUser, 38, false);
            pnlLogin.Controls.Add(MakeLabel("Password", 74));
            StyleField(txtPass, 96, true);

            StyleButton(btnLogin, "🔓  LOGIN", Color.FromArgb(21, 101, 192), 140, 400);
            btnLogin.Click += (s, e) => DoLogin();

            lblError.Location = new Point(40, 184);
            lblError.Size = new Size(400, 36);
            lblError.ForeColor = ErrColor;
            lblError.Font = new Font("Segoe UI", 9F);

            Label lblHint = MakeLabel("Authorized users only", 224);
            lblHint.ForeColor = Color.FromArgb(120, 140, 160);
            lblHint.Font = new Font("Segoe UI", 8.5F);

            StyleButton(btnRegister, "📧  Gmail နဲ့ register (ဆိုင်သစ်)", Color.FromArgb(47, 72, 101), 250, 400);
            btnRegister.Click += (s, e) => DoRegister();

            pnlLogin.Controls.AddRange(new Control[] { txtUser, txtPass, btnLogin, lblError, lblHint, btnRegister });

            // ================= Password အသစ် panel (force-change / register) =================
            pnlChange.Location = new Point(0, 86);
            pnlChange.Size = new Size(480, 340);
            pnlChange.BackColor = Bg;

            lblChgTitle.Location = new Point(40, 24);
            lblChgTitle.Size = new Size(400, 26);
            lblChgTitle.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
            lblChgTitle.ForeColor = Color.White;

            lblChgSub.Location = new Point(40, 52);
            lblChgSub.Size = new Size(400, 32);
            lblChgSub.Font = new Font("Segoe UI", 8.8F);
            lblChgSub.ForeColor = Color.FromArgb(180, 200, 120);

            pnlChange.Controls.Add(MakeLabel("Password အသစ်", 90));
            StyleField(txtNew1, 112, true);
            pnlChange.Controls.Add(MakeLabel("Password ထပ်ရိုက်ပါ", 148));
            StyleField(txtNew2, 170, true);

            Button btnSave = new Button();
            StyleButton(btnSave, "✅  SAVE & ENTER", Color.FromArgb(40, 130, 80), 214, 400);
            btnSave.Click += (s, e) => DoChange();

            lblChangeError.Location = new Point(40, 258);
            lblChangeError.Size = new Size(400, 60);
            lblChangeError.ForeColor = ErrColor;
            lblChangeError.Font = new Font("Segoe UI", 9F);

            pnlChange.Controls.AddRange(new Control[] { lblChgTitle, lblChgSub, txtNew1, txtNew2, btnSave, lblChangeError });

            Controls.Add(pnlLogin);
            Controls.Add(pnlChange);
            pnlChange.Visible = false;

            // အောက်ခြေ version
            Label lblVer = MakeLabel($"PMK Unlock Tool v{Application.ProductVersion.Split('+')[0]}  ·  Offline member access", 400);
            lblVer.Location = new Point(40, 400);
            lblVer.ForeColor = Color.FromArgb(100, 120, 140);
            lblVer.Font = new Font("Segoe UI", 8F);
            Controls.Add(lblVer);

            AcceptButton = btnLogin;
            Shown += (s, e) => txtUser.Focus();
            lockTimer.Tick += (s, e) => UpdateLockState();
            txtPass.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoLogin(); };
            txtUser.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoLogin(); };
            txtNew2.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoChange(); };
        }

        // ================= UI helpers =================
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
            b.Size = new Size(w, 32);
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
        }

        private void ShowError(string msg, bool isError = true)
        {
            lblError.Text = msg;
            lblError.ForeColor = isError ? ErrColor : InfoColor;
        }

        private void ChgErr(string msg) => lblChangeError.Text = msg;

        // ================= Lock: မှား ၅ ခါ → 30s စောင့် =================
        private void UpdateLockState()
        {
            if (DateTime.Now < lockUntil)
            {
                int left = (int)Math.Ceiling((lockUntil - DateTime.Now).TotalSeconds);
                ShowError($"စမ်းမှား များနေပါတယ် — {left}s စောင့်ပါ");
                btnLogin.Enabled = false;
                btnRegister.Enabled = false;
                return;
            }
            btnLogin.Enabled = true;
            btnRegister.Enabled = true;
            if (failCount >= 5) { failCount = 0; lblError.Text = ""; }
        }

        // ================= Login =================
        private void DoLogin()
        {
            if (pnlChange.Visible) return;
            if (DateTime.Now < lockUntil) { UpdateLockState(); return; }

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
                _pendingEntry = entry;
                _pendingEmail = null;
                ShowChangePanel($"🔑 {entry.Username} — Password အသစ် ထည့်ပါ",
                    "ပထမဆုံး login မို့ default password ကို ပြောင်းပေးပါ (မပြောင်းရင် ဆက်မသုံးရပါ)");
                return;
            }

            DialogResult = DialogResult.OK;
        }

        // ================= Gmail register (တစ်ခါပဲ) =================
        private async void DoRegister()
        {
            if (pnlChange.Visible) return;
            if (DateTime.Now < lockUntil) { UpdateLockState(); return; }

            btnLogin.Enabled = false;
            btnRegister.Enabled = false;
            ShowError("Google စာမျက်နှာ ပွင့်ပါမယ် — Gmail နဲ့ ဝင်ပြီး Continue နှိပ်ပါ…", isError: false);
            try
            {
                var (email, reason) = await GoogleAuth.AuthorizeAndGetEmailAsync();
                if (email == null)
                {
                    ShowError(reason ?? "Google login မအောင်ဘူး");
                    return;
                }
                if (UserManager.Find(email) != null)
                {
                    ShowError($"'{email}' က account ရှိပြီးသားပါ — password နဲ့ login ဝင်ပါ");
                    return;
                }
                ShowError("ခွင့်ပြုစာရင်း စစ်နေပါတယ်…", isError: false);
                bool allowed = await GoogleAuth.IsEmailAllowedAsync(email);
                if (!allowed)
                {
                    ShowError($"ဒီ Gmail ({email}) က ခွင့်ပြုစာရင်းမှာ မပါပါ။\nဝယ်ယူပြီးရင် PMK ကို ဆက်သွယ်ပြီး ထည့်ခိုင်းပါ");
                    return;
                }
                _pendingEmail = email;
                _pendingEntry = null;
                ShowChangePanel($"📧 {email} ✅ — အတည်ပြုပြီးပါပြီ",
                    "ဒီ tool အတွက် login password အသစ် ထည့်ပါ (နောက်ပိုင်း ဒီ password နဲ့ ဝင်ရမယ်)");
            }
            catch (Exception ex)
            {
                ShowError("Register error: " + ex.Message);
            }
            finally
            {
                if (!pnlChange.Visible)
                {
                    btnLogin.Enabled = true;
                    btnRegister.Enabled = true;
                }
            }
        }

        private void ShowChangePanel(string title, string subtitle)
        {
            lblChgTitle.Text = title;
            lblChgSub.Text = subtitle;
            lblChangeError.Text = "";
            txtNew1.Clear();
            txtNew2.Clear();
            pnlLogin.Visible = false;
            pnlChange.Visible = true;
            txtNew1.Focus();
        }

        // ================= Password set (force-change / register နှစ်မျိုးလုံး) =================
        private void DoChange()
        {
            string p1 = txtNew1.Text;
            string p2 = txtNew2.Text;
            if (p1.Length < 6) { ChgErr("Password က အနည်းဆုံး စာလုံး ၆ လုံး ရှိရမယ်"); return; }
            if (p1 == UserManager.DefaultAdminPassword) { ChgErr("Default password ကိုပဲ ပြန်မသုံးပါနဲ့"); return; }
            if (p1 != p2) { ChgErr("Password နှစ်ခု မတူပါ"); txtNew2.SelectAll(); return; }

            if (_pendingEmail != null)
            {
                // Gmail register — account အသစ် ဖန်တီး
                string err = UserManager.AddUser(_pendingEmail, p1, "admin");
                if (err != null) { ChgErr(err); return; }
                UserManager.TryLogin(_pendingEmail, p1, out _); // session set
                DialogResult = DialogResult.OK;
                return;
            }
            if (_pendingEntry != null)
            {
                // Default password ပြောင်း
                string err = UserManager.SetPassword(_pendingEntry.Username, p1);
                if (err != null) { ChgErr(err); return; }
                DialogResult = DialogResult.OK;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // e.Graphics ကို dispose မလုပ်ရဘူး — framework ပိုင်တယ်
            using var grad = new LinearGradientBrush(ClientRectangle, Bg, Color.FromArgb(12, 18, 26), LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(grad, ClientRectangle);
        }
    }
}
