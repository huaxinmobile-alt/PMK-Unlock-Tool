#nullable disable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinFormsApp1
{
    // Gate window — activated မဟုတ်ရင် activation screen၊ ပြီးမှ login (offline member gate)
    //  · Remember Me / Auto-Login  : password ကို DPAPI (Windows user-bound) နဲ့ encrypt ပြီး %AppData%\PMKUnlockTool\config.json မှာ သိမ်း
    //  · Session                   : login အောင်ရင် session.dat (ရက် ၃၀) — Logout နှိပ်မှ ပြန်မေး
    //  · Security                  : မှား ၃ ခါ → ၅ မိနစ် lock (tool ပြန်ဖွင့်လည်း မပျောက်) + login_attempts.log
    public class LoginWindow : Form
    {
        // ===== Login panel =====
        private readonly TextBox txtUser = new TextBox();
        private readonly TextBox txtPass = new TextBox();
        private readonly CheckBox chkRemember = new CheckBox();
        private readonly CheckBox chkAutoLogin = new CheckBox();
        private readonly Label lblError = new Label();
        private readonly Button btnLogin = new Button();
        private readonly Button btnRegister = new Button();
        private readonly LinkLabel lnkForgot = new LinkLabel();
        private readonly Panel pnlLogin = new Panel();
        private readonly Label lblSpinner = new Label();
        private readonly Label lblSavedHint = new Label();

        // ===== Password အသစ် panel (force-change / gmail register) =====
        private readonly Panel pnlChange = new Panel();
        private readonly Label lblChgTitle = new Label();
        private readonly Label lblChgSub = new Label();
        private readonly TextBox txtNew1 = new TextBox();
        private readonly TextBox txtNew2 = new TextBox();
        private readonly Label lblChangeError = new Label();

        // ===== Activation panel (per-PC license) =====
        private readonly Panel pnlActivate = new Panel();
        private readonly TextBox txtInstall = new TextBox();
        private readonly TextBox txtLicense = new TextBox();
        private readonly Label lblActError = new Label();
        private readonly Label lblActStatus = new Label();
        private readonly Label lblPcInfo = new Label();
        private readonly Button btnCopyId = new Button();
        private readonly Button btnCheckStatus = new Button();
        private readonly Button btnDeactivate = new Button();

        private UserEntry _pendingEntry;
        private string _pendingEmail;

        private int failCount;
        private DateTime lockUntil = DateTime.MinValue;
        private readonly System.Windows.Forms.Timer lockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly System.Windows.Forms.Timer spinTimer = new System.Windows.Forms.Timer { Interval = 120 };
        private int spinFrame;
        private bool loginBusy;

        // Support / purchase — လိုအပ်ရင် ဒီနေရာမှာ ပြင်ပါ
        private const string SupportUrl = "https://github.com/huaxinmobile-alt/PMK-Unlock-Tool/issues";
        private const string PurchaseUrl = "https://github.com/huaxinmobile-alt/PMK-Unlock-Tool";

        // ===== Theme (Ocean Dark) =====
        private static readonly Color Bg = Color.FromArgb(18, 24, 32);
        private static readonly Color BgDeep = Color.FromArgb(12, 18, 26);
        private static readonly Color FieldBg = Color.FromArgb(30, 39, 52);
        private static readonly Color ErrColor = Color.FromArgb(229, 115, 115);
        private static readonly Color OkColor = Color.FromArgb(0, 230, 118);
        private static readonly Color InfoColor = Color.FromArgb(180, 205, 235);
        private static readonly Color WarnColor = Color.FromArgb(255, 213, 79);
        private static readonly Color MutedColor = Color.FromArgb(120, 140, 160);

        public LoginWindow()
        {
            Text = "PMK MOBILE SERVICE TOOL";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 560);
            BackColor = Bg;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9.5F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // ===== Top branding =====
            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 86,
                Text = "PMK MOBILE SERVICE TOOL",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 84, 158)
            };
            Controls.Add(header);

            var lblTag = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "Qualcomm EDL · MediaTek · Samsung · Spreadtrum · ADB / Fastboot",
                ForeColor = Color.FromArgb(150, 190, 225),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8F),
                BackColor = Color.FromArgb(0, 66, 126)
            };
            Controls.Add(lblTag);

            BuildLoginPanel();
            BuildChangePanel();
            BuildActivatePanel();

            Controls.Add(pnlActivate);
            Controls.Add(pnlLogin);
            Controls.Add(pnlChange);
            pnlLogin.Visible = false;
            pnlChange.Visible = false;

            // ===== Start view: activated → login, မဟုတ်ရင် activation =====
            if (License.IsActivated)
            {
                ShowLoginPanel();
            }
            else
            {
                var cur = License.Current;
                if (cur != null && License.IsExpired(cur))
                {
                    SetActStatus($"⏳  သက်တမ်းကုန်ပါပြီ ({cur.Expiry}) — license အသစ်အတွက် PMK ကို ဆက်သွယ်ပါ", WarnColor);
                }
                ShowActivatePanel();
            }

            // အောက်ခြေ version
            Label lblVer = new Label
            {
                Location = new Point(24, 522),
                AutoSize = false,
                Size = new Size(472, 20),
                ForeColor = MutedColor,
                Font = new Font("Segoe UI", 8F),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = $"PMK Unlock Tool v{Application.ProductVersion.Split('+')[0]}  ·  Offline member access  ·  session {LoginSession.SessionDays} days"
            };
            Controls.Add(lblVer);

            lockTimer.Tick += (s, e) => UpdateLockState();
            txtPass.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoLogin(); };
            txtUser.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoLogin(); };
            txtNew2.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoChange(); };
            txtLicense.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && e.Control) DoActivate(); };
            Shown += (s, e) =>
            {
                UiFocus.BringToFront(this);
                // Update စစ်လို့ မရခဲ့ရင် (offline) warning — block မလုပ်ဘူး
                if (!string.IsNullOrWhiteSpace(UpdateGate.StartupWarning) && pnlLogin.Visible)
                    ShowError(UpdateGate.StartupWarning);
                // Logout လုပ်ထားလို့ auto-login ကျော်ထားတာ ရှင်းပြ (နောက်တစ်ခါ ဖွင့်ရင် ပြန်အလိုအလျောက် ဝင်မယ်)
                if (suppressNote && pnlLogin.Visible)
                    ShowError("🔒 Logout လုပ်ထားလို့ ဒီတစ်ခါ Auto-Login ကို ကျော်ထားတယ် — LOGIN နှိပ်ပါ။ (နောက်တစ်ခါ ဖွင့်ရင် ပြန်အလိုအလျောက် ဝင်ပါမယ်)", isError: false);
                if (pnlActivate.Visible) txtInstall.SelectAll();
                else if (pnlLogin.Visible) { PrefillSaved(); RunPendingAutoLogin(); txtUser.Focus(); }
            };
        }

        // =====================================================================
        //  LOGIN PANEL
        // =====================================================================
        private void BuildLoginPanel()
        {
            pnlLogin.Location = new Point(0, 116);
            pnlLogin.Size = new Size(520, 400);
            pnlLogin.BackColor = Bg;

            pnlLogin.Controls.Add(MakeLabel("👤  Username", 6));
            StyleField(txtUser, 30, false);
            pnlLogin.Controls.Add(txtUser);

            pnlLogin.Controls.Add(MakeLabel("🔒  Password", 74));
            StyleField(txtPass, 98, true);
            pnlLogin.Controls.Add(txtPass);

            chkRemember.Text = "Remember Me";
            chkRemember.Location = new Point(26, 142);
            chkRemember.AutoSize = true;
            chkRemember.ForeColor = Color.FromArgb(205, 220, 235);
            chkRemember.Font = new Font("Segoe UI", 9F);
            chkRemember.CheckedChanged += (s, e) =>
            {
                if (!chkRemember.Checked)
                {
                    chkAutoLogin.Checked = false;
                    LoginSettings.ClearCredentials();
                }
                chkAutoLogin.Enabled = chkRemember.Checked;
                UpdateSavedHint();
            };
            pnlLogin.Controls.Add(chkRemember);

            chkAutoLogin.Text = "Auto-Login";
            chkAutoLogin.Location = new Point(176, 142);
            chkAutoLogin.AutoSize = true;
            chkAutoLogin.ForeColor = Color.FromArgb(205, 220, 235);
            chkAutoLogin.Font = new Font("Segoe UI", 9F);
            chkAutoLogin.Enabled = false;
            chkAutoLogin.CheckedChanged += (s, e) => UpdateSavedHint();
            pnlLogin.Controls.Add(chkAutoLogin);

            lblSavedHint.Location = new Point(24, 166);
            lblSavedHint.Size = new Size(472, 18);
            lblSavedHint.Font = new Font("Segoe UI", 8.2F);
            lblSavedHint.ForeColor = MutedColor;
            pnlLogin.Controls.Add(lblSavedHint);

            StyleButton(btnLogin, "🔓  LOGIN", Color.FromArgb(21, 101, 192), 188, 472);
            btnLogin.Height = 38;
            btnLogin.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnLogin.Click += (s, e) => DoLogin();
            pnlLogin.Controls.Add(btnLogin);

            lblSpinner.Location = new Point(24, 234);
            lblSpinner.Size = new Size(200, 20);
            lblSpinner.ForeColor = InfoColor;
            lblSpinner.Font = new Font("Segoe UI", 9.5F);
            lblSpinner.Visible = false;
            pnlLogin.Controls.Add(lblSpinner);

            lblError.Location = new Point(24, 232);
            lblError.Size = new Size(472, 40);
            lblError.ForeColor = ErrColor;
            lblError.Font = new Font("Segoe UI", 9F);
            pnlLogin.Controls.Add(lblError);

            Label lblHint = MakeLabel("Authorized users only — အကောင့် မရှိရင် ဆိုင်အက်ဒမင်ကို ဆက်သွယ်ပါ", 280);
            lblHint.ForeColor = MutedColor;
            lblHint.Font = new Font("Segoe UI", 8.5F);
            pnlLogin.Controls.Add(lblHint);

            StyleButton(btnRegister, "📧  Create Account (Gmail နဲ့ register)", Color.FromArgb(47, 72, 101), 302, 472);
            btnRegister.Click += (s, e) => DoRegister();
            pnlLogin.Controls.Add(btnRegister);

            lnkForgot.Text = "Forgot Password?";
            lnkForgot.Location = new Point(24, 346);
            lnkForgot.AutoSize = true;
            lnkForgot.LinkColor = Color.FromArgb(100, 181, 246);
            lnkForgot.ActiveLinkColor = Color.White;
            lnkForgot.Font = new Font("Segoe UI", 9F);
            lnkForgot.LinkClicked += (s, e) => ShowForgotHelp();
            pnlLogin.Controls.Add(lnkForgot);

            Label lblSec = new Label
            {
                Location = new Point(150, 346),
                AutoSize = false,
                Size = new Size(346, 20),
                ForeColor = MutedColor,
                Font = new Font("Segoe UI", 8F),
                TextAlign = ContentAlignment.MiddleRight,
                Text = "ဖိုင်: %AppData%\\PMKUnlockTool\\  ·  password ကို DPAPI နဲ့ encrypt သိမ်း"
            };
            pnlLogin.Controls.Add(lblSec);
        }

        private bool autoLoginDone;
        private bool suppressNote;
        private bool suppressAutoLogin;   // Logout ပြီးချင်း process အတွက် auto-login ပိတ်ထား

        // သိမ်းထားတဲ့ credential တွေ ဖြည့် (Remember Me ဖွင့်ထားရင်) + Auto-Login ရှိရင် အလိုအလျောက် ဝင်
        private void PrefillSaved()
        {
            var cfg = LoginSettings.Load();
            chkRemember.Checked = cfg.RememberMe;
            chkAutoLogin.Enabled = cfg.RememberMe;
            chkAutoLogin.Checked = cfg.AutoLogin;
            UpdateSavedHint();

            if (!cfg.RememberMe || string.IsNullOrWhiteSpace(cfg.Username)) return;
            txtUser.Text = cfg.Username;
            string pw = LoginSettings.GetSavedPassword(cfg);
            if (pw != null) txtPass.Text = pw;

            // Auto-Login ကို ဒီနေရာမှာ မလုပ်ဘူး — Form handle မရှိသေးရင် BeginInvoke crash ဖြစ်တယ်
            // (ctor ထဲကနေ ခေါ်လို့). RunPendingAutoLogin() ကို Shown / panel ပြောင်းချိန်မှာ ခေါ်တယ်.
            if (cfg.AutoLogin && pw != null && !autoLoginDone)
            {
                // Logout လုပ်ပြီးချင်း ဖွင့်တာဆိုရင် ဒီတစ်ခါပဲ auto-login ကို ကျော် (login screen ပြဖို့)
                // မှတ်ချက်: PrefillSaved() က ctor နဲ့ Shown မှာ နှစ်ခါ ခေါ်ခံရတာမို့ suppressAutoLogin ကို
                // process တစ်ခုလုံးအတွက် မှတ်ထားရတယ် (မဟုတ်ရင် ဒုတိယအခါမှာ auto-login ပြန်ဖွင့်မိမယ်)
                if (suppressAutoLogin || LoginSettings.ConsumeAutoLoginSuppression())
                {
                    suppressAutoLogin = true;
                    suppressNote = true;
                    return;
                }
                pendingAutoLogin = true;
            }
        }

        private bool pendingAutoLogin;

        /// <summary>Form ပေါ်ပြီး handle ရှိမှ Auto-Login စမ်း (ctor ထဲမှာ ခေါ်ရင် crash ဖြစ်တယ်)</summary>
        private void RunPendingAutoLogin()
        {
            if (suppressAutoLogin) return;
            if (!pendingAutoLogin || autoLoginDone || !pnlLogin.Visible) return;
            string user = txtUser.Text.Trim();
            string pass = txtPass.Text;
            if (user.Length == 0 || pass.Length == 0) return;

            autoLoginDone = true;
            pendingAutoLogin = false;
            LoginSettings.LogAttempt(user, "AUTO-LOGIN");
            DoLogin(autoLogin: true);
        }

        // သိမ်းထားတဲ့ အခြေအနေကို ရှင်းရှင်းလင်းလင်း ပြ
        private void UpdateSavedHint()
        {
            if (lblSavedHint == null) return;
            if (!chkRemember.Checked)
            {
                lblSavedHint.Text = "💾 သိမ်းထားတာ မရှိပါ — ပိတ်/ဖွင့်တိုင်း username + password ရိုက်ရမယ်";
                lblSavedHint.ForeColor = MutedColor;
            }
            else if (chkAutoLogin.Checked)
            {
                lblSavedHint.Text = "⚡ Auto-Login ဖွင့်ထားတယ် — နောက်တစ်ခါ ဖွင့်ရင် LOGIN နှိပ်စရာ မလိုဘူး (session ရက် ၃၀)";
                lblSavedHint.ForeColor = OkColor;
            }
            else
            {
                lblSavedHint.Text = "👤 username/password ဖြည့်ပြီးသား ဖြစ်မယ် — LOGIN နှိပ်ရမယ် (Auto-Login ကိုပါ tick လုပ်ရင် အလိုအလျောက် ဝင်မယ်)";
                lblSavedHint.ForeColor = WarnColor;
            }
        }

        private void ShowForgotHelp()
        {
            string msg = "Password ပြန်လည်သတ်မှတ်ရန်:\n\n" +
                         "• Staff account ဆိုရင် — ဆိုင်ရဲ့ admin account နဲ့ ဝင်ပြီး\n" +
                         "   Settings → 👥 USERS & ACCESS ကနေ password အသစ် သတ်မှတ်ပေးပါ\n\n" +
                         "• Admin/master account ဆိုရင် — PMK ကို ဆက်သွယ်ပါ\n" +
                         $"   {SupportUrl}\n\n" +
                         "• PC တစ်လုံးတည်း သုံးပြီး admin password ပျောက်ရင် users.dat ဖိုင်ကို\n" +
                         "   ဖျက်ပြီး tool ပြန်ဖွင့်နိုင်တယ် (account စာရင်း default ပြန်ဖြစ်မယ် —\n" +
                         "   license/activation ကတော့ မထိခိုက်ဘူး)";
            MessageBox.Show(this, msg, "Forgot Password — အကူအညီ", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // =====================================================================
        //  PASSWORD အသစ် PANEL (force-change / register)
        // =====================================================================
        private void BuildChangePanel()
        {
            pnlChange.Location = new Point(0, 116);
            pnlChange.Size = new Size(520, 400);
            pnlChange.BackColor = Bg;

            lblChgTitle.Location = new Point(24, 10);
            lblChgTitle.Size = new Size(472, 28);
            lblChgTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblChgTitle.ForeColor = Color.White;

            lblChgSub.Location = new Point(24, 40);
            lblChgSub.Size = new Size(472, 40);
            lblChgSub.Font = new Font("Segoe UI", 9F);
            lblChgSub.ForeColor = Color.FromArgb(180, 200, 120);

            pnlChange.Controls.Add(lblChgTitle);
            pnlChange.Controls.Add(lblChgSub);

            pnlChange.Controls.Add(MakeLabel("Password အသစ် (အနည်းဆုံး ၆ လုံး)", 92));
            StyleField(txtNew1, 116, true);
            pnlChange.Controls.Add(txtNew1);

            pnlChange.Controls.Add(MakeLabel("Password ထပ်ရိုက်ပါ", 164));
            StyleField(txtNew2, 188, true);
            pnlChange.Controls.Add(txtNew2);

            Button btnSave = new Button();
            StyleButton(btnSave, "✅  SAVE & ENTER", Color.FromArgb(40, 130, 80), 236, 472);
            btnSave.Height = 36;
            btnSave.Click += (s, e) => DoChange();
            pnlChange.Controls.Add(btnSave);

            lblChangeError.Location = new Point(24, 286);
            lblChangeError.Size = new Size(472, 60);
            lblChangeError.ForeColor = ErrColor;
            lblChangeError.Font = new Font("Segoe UI", 9F);
            pnlChange.Controls.Add(lblChangeError);
        }

        // =====================================================================
        //  ACTIVATION PANEL
        // =====================================================================
        private void BuildActivatePanel()
        {
            pnlActivate.Location = new Point(0, 116);
            pnlActivate.Size = new Size(520, 400);
            pnlActivate.BackColor = Bg;

            Label lblActTitle = MakeLabel("🔑  Tool Activation", 4);
            lblActTitle.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            lblActTitle.ForeColor = Color.White;
            pnlActivate.Controls.Add(lblActTitle);

            Label lblActSub = MakeLabel("ဒီ PC ရဲ့ Installation ID ကို PMK ကို ပို့ပါ — license key ပြန်ရလာရင် အောက်မှာ paste လုပ်ပြီး Activate နှိပ်ပါ", 32);
            lblActSub.Size = new Size(472, 34);
            lblActSub.Font = new Font("Segoe UI", 8.8F);
            lblActSub.ForeColor = Color.FromArgb(150, 175, 205);
            pnlActivate.Controls.Add(lblActSub);

            // PC အချက်အလက် (PC name + Windows version)
            lblPcInfo.Location = new Point(24, 66);
            lblPcInfo.Size = new Size(472, 18);
            lblPcInfo.Font = new Font("Segoe UI", 8.2F);
            lblPcInfo.ForeColor = MutedColor;
            lblPcInfo.Text = PcInfoText();
            pnlActivate.Controls.Add(lblPcInfo);

            Label lblInstLabel = MakeLabel("Installation ID (ဒီ PC):", 88);
            lblInstLabel.ForeColor = Color.FromArgb(200, 214, 230);
            lblInstLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            pnlActivate.Controls.Add(lblInstLabel);

            StyleField(txtInstall, 110, false);
            txtInstall.ReadOnly = true;
            txtInstall.Font = new Font("Consolas", 11F, FontStyle.Bold);
            txtInstall.Text = License.MachineIdDisplay;
            txtInstall.BackColor = Color.FromArgb(24, 32, 44);
            txtInstall.ForeColor = Color.FromArgb(0, 230, 118);
            txtInstall.Width = 472;
            txtInstall.Click += (s, e) => txtInstall.SelectAll();
            pnlActivate.Controls.Add(txtInstall);

            // Copy to Clipboard — ID field ရဲ့ အောက် သီးသန့် row (spec layout)
            StyleButton(btnCopyId, "📋  Copy to Clipboard", Color.FromArgb(60, 90, 120), 146, 472);
            btnCopyId.Height = 30;
            btnCopyId.Click += (s, e) => CopyInstallId();
            pnlActivate.Controls.Add(btnCopyId);

            Label lblLicLabel = MakeLabel("License Key (PMK ဆီက ရလာတဲ့ key ကို paste လုပ်ပါ):", 184);
            lblLicLabel.ForeColor = Color.FromArgb(200, 214, 230);
            lblLicLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            pnlActivate.Controls.Add(lblLicLabel);

            StyleField(txtLicense, 208, false);
            txtLicense.Font = new Font("Consolas", 9F);
            txtLicense.Multiline = true;
            txtLicense.Height = 60;
            txtLicense.ScrollBars = ScrollBars.Vertical;
            txtLicense.WordWrap = true;
            pnlActivate.Controls.Add(txtLicense);

            StyleButton(btnCheckStatus, "🔎 Check Status", Color.FromArgb(60, 90, 120), 276, 140);
            btnCheckStatus.Location = new Point(252, 276);
            btnCheckStatus.Click += (s, e) => CheckLicenseStatus();
            pnlActivate.Controls.Add(btnCheckStatus);

            Button btnActivate = new Button();
            StyleButton(btnActivate, "✅  ACTIVATE", Color.FromArgb(21, 101, 192), 276, 220);
            btnActivate.Height = 36;
            btnActivate.Click += (s, e) => DoActivate();
            pnlActivate.Controls.Add(btnActivate);

            StyleButton(btnDeactivate, "🗑 Deactivate", Color.FromArgb(90, 60, 60), 276, 96);
            btnDeactivate.Location = new Point(400, 276);
            btnDeactivate.Click += (s, e) => DoDeactivate();
            pnlActivate.Controls.Add(btnDeactivate);

            lblActStatus.Location = new Point(24, 320);
            lblActStatus.Size = new Size(472, 20);
            lblActStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblActStatus.ForeColor = InfoColor;
            pnlActivate.Controls.Add(lblActStatus);

            lblActError.Location = new Point(24, 366);
            lblActError.Size = new Size(472, 30);
            lblActError.ForeColor = ErrColor;
            lblActError.Font = new Font("Segoe UI", 9F);
            pnlActivate.Controls.Add(lblActError);

            // Contact / Purchase
            LinkLabel lnkSupport = new LinkLabel
            {
                Text = "💬 Contact Support",
                Location = new Point(24, 342),
                AutoSize = true,
                LinkColor = Color.FromArgb(100, 181, 246),
                ActiveLinkColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            lnkSupport.LinkClicked += (s, e) => OpenUrl(SupportUrl);
            pnlActivate.Controls.Add(lnkSupport);

            LinkLabel lnkBuy = new LinkLabel
            {
                Text = "🛒 Purchase License",
                Location = new Point(170, 342),
                AutoSize = true,
                LinkColor = Color.FromArgb(0, 230, 118),
                ActiveLinkColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            lnkBuy.LinkClicked += (s, e) => OpenUrl(PurchaseUrl);
            pnlActivate.Controls.Add(lnkBuy);

            Label lblNote = new Label
            {
                Location = new Point(300, 342),
                AutoSize = false,
                Size = new Size(196, 20),
                ForeColor = MutedColor,
                Font = new Font("Segoe UI", 8F),
                TextAlign = ContentAlignment.MiddleRight,
                Text = "License က ဒီ PC နဲ့ပဲ ချိတ်ထားတယ်"
            };
            pnlActivate.Controls.Add(lblNote);

            RefreshActivationStatus();
        }

        private static string PcInfoText()
        {
            string pc = Environment.MachineName;
            string win = Environment.OSVersion.VersionString;
            try
            {
                win = $"Windows {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor} (build {Environment.OSVersion.Version.Build})";
                string desc = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", null) as string;
                if (!string.IsNullOrWhiteSpace(desc)) win = $"{desc} ({Environment.OSVersion.Version.Build})";
            }
            catch { }
            return $"💻 PC: {pc}   ·   {win}";
        }

        private void SetActStatus(string text, Color color)
        {
            lblActStatus.Text = text;
            lblActStatus.ForeColor = color;
        }

        // လက်ရှိ activation အခြေအနေ ပြ
        private void RefreshActivationStatus()
        {
            var cur = License.Current;
            if (cur == null)
            {
                SetActStatus("⏳  Waiting for activation — license key မထည့်ရသေးပါ", WarnColor);
                return;
            }
            if (License.IsExpired(cur))
            {
                SetActStatus($"❌  Expired ({cur.Expiry}) — renewal လိုပါတယ်", ErrColor);
                return;
            }
            string exp = cur.Expiry == "none" ? "သက်တမ်းမကုန်" : $"သက်တမ်း {cur.Expiry} အထိ";
            SetActStatus($"✅  Activated — {cur.ShopName}  ·  {exp}", OkColor);
        }

        private void CopyInstallId()
        {
            try
            {
                Clipboard.SetText(License.MachineIdDisplay);
                SetActStatus("📋  Installation ID ကို copy လုပ်ပြီးပါပြီ — PMK ကို ပို့ပါ", OkColor);
            }
            catch { lblActError.Text = "Copy မလုပ်နိုင်ပါ — ID ကို လက်ဖြင်း ရွေးပြီး Ctrl+C နှိပ်ပါ"; }
        }

        private void CheckLicenseStatus()
        {
            var cur = License.Current;
            if (cur == null)
            {
                SetActStatus("⏳  Not activated — license key ထည့်ပြီး ACTIVATE နှိပ်ပါ", WarnColor);
                lblActError.Text = "";
                return;
            }
            if (!string.Equals(cur.MachineId, License.MachineIdRaw, StringComparison.OrdinalIgnoreCase))
            {
                SetActStatus("❌  License ID က ဒီ PC နဲ့ မကိုက်ပါ — license အသစ် လိုပါတယ်", ErrColor);
                return;
            }
            if (License.IsExpired(cur))
            {
                SetActStatus($"❌  Expired ({cur.Expiry})", ErrColor);
                return;
            }
            string exp = cur.Expiry == "none" ? "သက်တမ်းမကုန်" : $"{cur.Expiry} အထိ";
            SetActStatus($"✅  Valid — {cur.ShopName}  ·  {exp}  ·  ဒီ PC အတွက် မှန်ကန်ပါတယ်", OkColor);
            lblActError.Text = "";
        }

        private void DoDeactivate()
        {
            if (License.Current == null)
            {
                SetActStatus("⏳  Deactivate လုပ်စရာ license မရှိပါ", WarnColor);
                return;
            }
            var res = MessageBox.Show(this,
                "ဒီ PC ကနေ license ကို ဖျက်မလား?\n\n" +
                "• license ဖိုင် (license.dat) ကို ဖျက်မယ် — tool ပြန်သုံးရင် key ပြန်ထည့်ရမယ်\n" +
                "• Key က ဒီ PC အတွက် ဖြစ်နေတာမို့ ပြန်ထည့်ရင် အလုပ်လုပ်ပါတယ် (တစ်ခြား PC ကို ရွှေ့လို့ မရပါ)\n" +
                "• တစ်ခြား PC ကို ရွှေ့ချင်ရင် PMK ကို ဆက်သွယ်ပြီး license အသစ် ထုတ်ပါ",
                "Deactivate License", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (res != DialogResult.Yes) return;

            License.Deactivate();
            txtLicense.Clear();
            lblActError.Text = "License ဖျက်ပြီးပါပြီ — key အသစ် ထည့်ပြီး ပြန် Activate လုပ်နိုင်ပါတယ်";
            lblActError.ForeColor = WarnColor;
            RefreshActivationStatus();
            ShowActivatePanel();
        }

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { lblActError.Text = "Link ဖွင့်မရပါ: " + ex.Message; }
        }

        private void DoActivate()
        {
            string key = txtLicense.Text.Trim();
            if (key.Length == 0) { lblActError.ForeColor = ErrColor; lblActError.Text = "License key ထည့်ပါ"; return; }

            // Format ကို အရင် စစ် (server/verify မလုပ်ခင်) — မှန်ရင် pmklic1|id|shop|expiry|sig
            if (!key.StartsWith("pmklic1|", StringComparison.Ordinal) || key.Split('|').Length != 5)
            {
                lblActError.ForeColor = ErrColor;
                lblActError.Text = "❌ License key format မမှန်ပါ — `pmklic1|...` နဲ့ စပြီး အပိုင်း ၅ ပိုင်း ရှိရမယ် (PMK ဆီက key အတိအကျ ကူးထည့်ပါ)";
                SetActStatus("❌  Invalid key format", ErrColor);
                return;
            }

            string err = License.Activate(key);
            if (err != null)
            {
                lblActError.ForeColor = ErrColor;
                lblActError.Text = "❌ " + err;
                SetActStatus("❌  Activation failed", ErrColor);
                return;
            }

            lblActError.Text = "";
            RefreshActivationStatus();
            ShowLoginPanel();
            lblError.ForeColor = OkColor;
            lblError.Text = $"✅ Activated — {License.ShopName}";
        }

        // =====================================================================
        //  UI helpers
        // =====================================================================
        private Label MakeLabel(string text, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(24, y),
                AutoSize = false,
                Size = new Size(472, 22),
                ForeColor = Color.FromArgb(210, 222, 235),
                Font = new Font("Segoe UI", 9F)
            };
        }

        private static void StyleField(TextBox t, int y, bool secret)
        {
            t.Location = new Point(24, y);
            t.Width = 472;
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
            b.Location = new Point(24, y);
            b.Size = new Size(w, 34);
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

        // =====================================================================
        //  Panel switch
        // =====================================================================
        private void ShowLoginPanel()
        {
            UserManager.EnsureLoaded();
            pnlActivate.Visible = false;
            pnlChange.Visible = false;
            pnlLogin.Visible = true;
            AcceptButton = btnLogin;
            // Activation/force-change ပြီးလို့ login panel ပြန်ရောက်ချိန်လည်း သိမ်းထားတာ ဖြည့် + Auto-Login လုပ်
            PrefillSaved();
            if (IsHandleCreated) BeginInvoke(new Action(RunPendingAutoLogin));
            txtUser.Focus();
        }

        private void ShowActivatePanel()
        {
            pnlLogin.Visible = false;
            pnlChange.Visible = false;
            pnlActivate.Visible = true;
            AcceptButton = null;
            RefreshActivationStatus();
            txtLicense.Focus();
        }

        private void ShowChangePanel(string title, string subtitle)
        {
            lblChgTitle.Text = title;
            lblChgSub.Text = subtitle;
            lblChangeError.Text = "";
            txtNew1.Clear();
            txtNew2.Clear();
            pnlLogin.Visible = false;
            pnlActivate.Visible = false;
            pnlChange.Visible = true;
            AcceptButton = null;
            txtNew1.Focus();
        }

        // =====================================================================
        //  Lock / spinner
        // =====================================================================
        private void UpdateLockState()
        {
            if (DateTime.Now < lockUntil)
            {
                int left = (int)Math.Ceiling((lockUntil - DateTime.Now).TotalSeconds);
                int min = left / 60, sec = left % 60;
                ShowError($"🔒 Account ကို ခေတ္တ ပိတ်ထားတယ် — မိနစ် {min}:{sec:00} စောင့်ပြီး ပြန်စမ်းပါ");
                btnLogin.Enabled = false;
                btnRegister.Enabled = false;
                return;
            }
            if (!btnLogin.Enabled)
            {
                btnLogin.Enabled = true;
                btnRegister.Enabled = true;
                lblError.Text = "";
                LoginSettings.ResetFailures();
                ShowError($"ပြန်စမ်းလို့ရပါပြီ (ကျန် {LoginSettings.MaxAttempts} ခါ)", isError: false);
            }
        }

        private void StartSpinner()
        {
            spinFrame = 0;
            lblSpinner.Visible = true;
            txtUser.Enabled = txtPass.Enabled = btnLogin.Enabled = btnRegister.Enabled = false;
            spinTimer.Start();
        }

        private void StopSpinner()
        {
            spinTimer.Stop();
            lblSpinner.Visible = false;
            txtUser.Enabled = txtPass.Enabled = true;
            btnLogin.Enabled = btnRegister.Enabled = true;
            if (chkRemember != null) chkRemember.Enabled = true;
        }

        // =====================================================================
        //  LOGIN
        // =====================================================================
        private void DoLogin() => DoLogin(autoLogin: false);

        private async void DoLogin(bool autoLogin)
        {
            if (!pnlLogin.Visible || loginBusy) return;
            if (DateTime.Now < lockUntil) { UpdateLockState(); return; }

            string user = txtUser.Text.Trim();
            string pass = txtPass.Text;
            if (user.Length == 0 || pass.Length == 0)
            {
                ShowError("Username နဲ့ password ထည့်ပါ");
                return;
            }

            loginBusy = true;
            StartSpinner();
            lblError.Text = "";
            spinTimer.Tick -= SpinTick;
            spinTimer.Tick += SpinTick;

            // PBKDF2 (100k rounds) က UI ကို မဆွဲထားရအောင် နောက်ခံမှာ စစ်
            UserEntry entry = null;
            string reason = "";
            await Task.Run(() => { entry = UserManager.TryLogin(user, pass, out reason); });

            spinTimer.Tick -= SpinTick;
            spinTimer.Stop();
            loginBusy = false;
            StopSpinner();

            if (entry == null)
            {
                LoginSettings.LogAttempt(user, (autoLogin ? "AUTO-FAIL: " : "FAIL: ") + reason);
                if (autoLogin)
                {
                    // သိမ်းထားတဲ့ password က အဟောင်း ဖြစ်နေတာ — လက်ဖြင်း ရိုက်ခိုင်း (lock မချ)
                    ShowError("⚡ Auto-Login မအောင်ဘူး — သိမ်းထားတဲ့ password က မကိုက်တော့ပါ။\nPassword ပြန်ရိုက်ပြီး LOGIN နှိပ်ပါ (အောင်ရင် အသစ် ပြန်သိမ်းပေးမယ်)");
                    txtPass.SelectAll();
                    txtPass.Focus();
                    return;
                }
                failCount++;
                var until = LoginSettings.RegisterFailure();
                int left = LoginSettings.RemainingAttempts();
                if (until > DateTime.MinValue)
                {
                    lockUntil = until;
                    lockTimer.Start();
                    UpdateLockState();
                }
                else
                {
                    ShowError($"❌ {reason}");
                    if (left > 0) lblError.Text += $"  ·  ကျန် {left} ခါ";
                }
                txtPass.SelectAll();
                txtPass.Focus();
                return;
            }

            LoginSettings.LogAttempt(user, autoLogin ? "AUTO-OK" : "OK");
            LoginSettings.ResetFailures();
            failCount = 0;

            // Remember Me — အောင်မှ သိမ်း (unchecked ဆို ရှင်းပြီးသား)
            try
            {
                if (chkRemember.Checked) LoginSettings.SaveCredentials(entry.Username, pass, chkAutoLogin.Checked);
                else LoginSettings.ClearCredentials();
            }
            catch { }

            if (entry.ForceChange && entry.Role != "master")
            {
                _pendingEntry = entry;
                _pendingEmail = null;
                ShowChangePanel($"🔑 {entry.Username} — Password အသစ် ထည့်ပါ",
                    "ပထမဆုံး login မို့ default password ကို ပြောင်းပေးပါ (မပြောင်းရင် ဆက်မသုံးရပါ)");
                return;
            }

            CompleteLogin(entry);
        }

        private void SpinTick(object sender, EventArgs e)
        {
            string[] frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
            spinFrame = (spinFrame + 1) % frames.Length;
            lblSpinner.Text = $"{frames[spinFrame]}  စစ်ဆေးနေပါတယ်…";
        }

        // Login အောင်ပြီ — session မှတ်ပြီး ပိတ်တယ် (နောက်တစ်ခါ ဖွင့်ရင် ပြန်မမေးတော့ဘူး)
        private void CompleteLogin(UserEntry entry)
        {
            if (entry != null) LoginSession.Save(entry.Username);
            DialogResult = DialogResult.OK;
        }

        // =====================================================================
        //  Gmail register (တစ်ခါပဲ — client id ရှိမှ)
        // =====================================================================
        private async void DoRegister()
        {
            if (!pnlLogin.Visible || loginBusy) return;
            if (DateTime.Now < lockUntil) { UpdateLockState(); return; }

            loginBusy = true;
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
                loginBusy = false;
                if (!pnlChange.Visible)
                {
                    btnLogin.Enabled = true;
                    btnRegister.Enabled = true;
                }
            }
        }

        // =====================================================================
        //  Password set (force-change / register)
        // =====================================================================
        private void DoChange()
        {
            string p1 = txtNew1.Text;
            string p2 = txtNew2.Text;
            if (p1.Length < 6) { ChgErr("Password က အနည်းဆုံး စာလုံး ၆ လုံး ရှိရမယ်"); return; }
            if (p1 == UserManager.DefaultAdminPassword) { ChgErr("Default password ကိုပဲ ပြန်မသုံးပါနဲ့"); return; }
            if (p1 != p2) { ChgErr("Password နှစ်ခု မတူပါ"); txtNew2.SelectAll(); return; }

            if (_pendingEmail != null)
            {
                string err = UserManager.AddUser(_pendingEmail, p1, "admin");
                if (err != null) { ChgErr(err); return; }
                LoginSettings.LogAttempt(_pendingEmail, "REGISTER+LOGIN");
                if (chkRemember.Checked) LoginSettings.SaveCredentials(_pendingEmail, p1, chkAutoLogin.Checked);
                CompleteLogin(UserManager.TryLogin(_pendingEmail, p1, out _)); // session set
                return;
            }
            if (_pendingEntry != null)
            {
                string err = UserManager.SetPassword(_pendingEntry.Username, p1);
                if (err != null) { ChgErr(err); return; }
                LoginSettings.LogAttempt(_pendingEntry.Username, "PASSWORD-CHANGED");
                // သိမ်းထားတဲ့ password က အဟောင်း ဖြစ်နေမယ် — အသစ်နဲ့ အစားထိုး
                if (chkRemember.Checked) LoginSettings.SaveCredentials(_pendingEntry.Username, p1, chkAutoLogin.Checked);
                CompleteLogin(_pendingEntry);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Size သုည (ဖွင့်စ) အချိန် LinearGradientBrush က "Parameter is not valid" ဖြစ်တတ်လို့ ကာကွယ်
            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) { base.OnPaintBackground(e); return; }
            try
            {
                using var grad = new LinearGradientBrush(ClientRectangle, Bg, BgDeep, LinearGradientMode.Vertical);
                e.Graphics.FillRectangle(grad, ClientRectangle);
            }
            catch { base.OnPaintBackground(e); }
        }
    }
}
