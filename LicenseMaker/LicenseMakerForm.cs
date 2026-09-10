#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace PMKLicenseMaker
{
    /// <summary>
    /// PMK License Maker — ဝယ်သူ PC အတွက် license key ထုတ်ပေးတဲ့ GUI.
    ///   · Installation ID paste → shop name → expiry → Generate → Copy
    ///   · ထုတ်ပြီးသား စာရင်းကို tools\private\issued_licenses.log မှာ မှတ် (history list နဲ့ ပြန် copy လုပ်လို့ရ)
    ///   · Private key (tools\private\activation_key.pem) ကို runtime မှာ ရှာ/ရွေး — app ရဲ့ public key နဲ့ ကိုက်မကိုက် စစ်ပြီး မကိုက်ရင် သတိပေး
    /// ⚠️ ဒီ tool ကို ဝယ်သူတွေဆီ ဘယ်တော့မှ မဖြန့်ရ — private key နဲ့ တွဲသုံးတယ်
    /// </summary>
    public class LicenseMakerForm : Form
    {
        // ---- App (WinFormsApp1/License.cs) ထဲက public key အတိအကျ — license ထုတ်ပြီး ဒီ key နဲ့ ပြန်စစ်တယ်
        //      (keypair လဲရင် ဒီနေရာနဲ့ License.cs နှစ်ခုလုံး update လုပ်ပါ)
        private const string AppPublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA6soCUVkTGAGjt262rxDk\n" +
            "nszhd6z8plSOiNC1tqS349r4lqvBNAFdwXVZG/3J9ibDJgodlR+a1opubrA5yNQR\n" +
            "HDsfVBtcX3a2n6Za6noWUTDHbRfzjTMNB8b3H2LW459fsMfU+zpEJtKFBVtqLtvI\n" +
            "lyL33xQTu9bq2B3ePOFf5TUZ44ZfRy4+LKokx46zL7W/MYFBsCp9eX1GG+VzAVg0\n" +
            "tGCu48PG8qKLm6qJboAJM11N+HFltxRq1+XAvY7k6FTEx9Wu0JpU4Fd16jv1xefh\n" +
            "YSvzc9zpTNwy++uggyXXxODv8MvyEpkJRIqBjn0SEsDceLO/MLs2QXb+DlCL2DYj\n" +
            "lQIDAQAB\n" +
            "-----END PUBLIC KEY-----";

        private const string Prefix = "pmklic1";

        // ---- theme ----
        private static readonly Color Bg = Color.FromArgb(18, 24, 32);
        private static readonly Color Card = Color.FromArgb(26, 34, 45);
        private static readonly Color Field = Color.FromArgb(30, 39, 52);
        private static readonly Color Ok = Color.FromArgb(0, 230, 118);
        private static readonly Color Err = Color.FromArgb(229, 115, 115);
        private static readonly Color Warn = Color.FromArgb(255, 213, 79);
        private static readonly Color Muted = Color.FromArgb(150, 170, 190);
        private static readonly Color Info = Color.FromArgb(180, 205, 235);

        // ---- controls ----
        private readonly TextBox txtId = new TextBox();
        private readonly TextBox txtShop = new TextBox();
        private readonly TextBox txtDays = new TextBox();
        private readonly TextBox txtKey = new TextBox();
        private readonly Label lblIdState = new Label();
        private readonly Label lblKeyState = new Label();
        private readonly Label lblStatus = new Label();
        private readonly ListView lstHistory = new ListView();
        private readonly Button btnGenerate = new Button();

        private string keyPath;
        private RSA signer;              // private key
        private string keyFingerprint = "";
        private bool keyMatchesApp;
        private string lastGenerated = "";

        public LicenseMakerForm()
        {
            Text = "PMK License Maker";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 700);
            BackColor = Bg;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9.5F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int y = 12;

            // ===== header =====
            Controls.Add(MakeLabel("🔑  PMK LICENSE MAKER", 18, y, 400, 26, Color.White, 14F, FontStyle.Bold));
            lblKeyState.Location = new Point(420, y + 6);
            lblKeyState.Size = new Size(328, 20);
            lblKeyState.TextAlign = ContentAlignment.MiddleRight;
            lblKeyState.Font = new Font("Segoe UI", 8.5F);
            Controls.Add(lblKeyState);
            y += 32;

            Controls.Add(MakeLabel("ဝယ်သူရဲ့ Installation ID (tool → Activation screen → 📋 Copy ကနေ ရတယ်)", 24, y, 600, 20, Muted, 8.5F));
            y += 22;
            txtId.Location = new Point(24, y);
            txtId.Size = new Size(560, 28);
            StyleField(txtId, "Consolas", 11F);
            txtId.TextChanged += (s, e) => ValidateId();
            Controls.Add(txtId);
            AddBtn("📋 Paste", 592, y - 1, 80, 30, Color.FromArgb(60, 90, 120), (s, e) => PasteId());
            AddBtn("✖", 678, y - 1, 58, 30, Color.FromArgb(70, 80, 95), (s, e) => { txtId.Clear(); });
            y += 32;
            lblIdState.Location = new Point(26, y);
            lblIdState.Size = new Size(710, 18);
            lblIdState.Font = new Font("Segoe UI", 8.5F);
            Controls.Add(lblIdState);
            y += 26;

            Controls.Add(MakeLabel("ဆိုင်နာမည် / ဝယ်သူ အမည်", 24, y, 300, 20, Muted, 8.5F));
            y += 22;
            txtShop.Location = new Point(24, y);
            txtShop.Size = new Size(712, 28);
            StyleField(txtShop, "Segoe UI", 10.5F);
            Controls.Add(txtShop);
            y += 38;

            Controls.Add(MakeLabel("သက်တမ်း (Expiry)", 24, y, 300, 20, Muted, 8.5F));
            y += 22;
            AddBtn("∞  အမြဲ (permanent)", 24, y, 170, 30, Color.FromArgb(46, 125, 50), (s, e) => SetDays(""));
            AddBtn("၃၀ ရက်", 200, y, 84, 30, Color.FromArgb(60, 90, 120), (s, e) => SetDays("30"));
            AddBtn("၉၀ ရက်", 290, y, 84, 30, Color.FromArgb(60, 90, 120), (s, e) => SetDays("90"));
            AddBtn("၃၆၅ ရက်", 380, y, 84, 30, Color.FromArgb(60, 90, 120), (s, e) => SetDays("365"));
            txtDays.Location = new Point(474, y + 2);
            txtDays.Size = new Size(80, 26);
            StyleField(txtDays, "Segoe UI", 10F);
            txtDays.TextAlign = HorizontalAlignment.Center;
            Controls.Add(txtDays);
            Controls.Add(MakeLabel("ရက် (ကိုယ်တိုင်)", 560, y + 4, 180, 22, Muted, 8.5F));
            y += 42;

            btnGenerate = AddBtn("🔑  GENERATE LICENSE", 24, y, 260, 42, Color.FromArgb(21, 101, 192), (s, e) => Generate());
            btnGenerate.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            AddBtn("✅ Verify", 294, y, 100, 42, Color.FromArgb(40, 130, 80), (s, e) => VerifyKeyBox());
            AddBtn("⚙ Key File…", 404, y, 120, 42, Color.FromArgb(70, 80, 95), (s, e) => ChooseKeyFile());
            AddBtn("ℹ About", 534, y, 90, 42, Color.FromArgb(70, 80, 95), (s, e) => ShowAbout());
            y += 52;

            Controls.Add(MakeLabel("License key (ဒီစာကြောင်းလုံး ကော်ပီပြီး ဝယ်သူကို ပို့ပါ)", 24, y, 600, 20, Muted, 8.5F));
            y += 22;
            txtKey.Location = new Point(24, y);
            txtKey.Size = new Size(712, 74);
            txtKey.Multiline = true;
            txtKey.ReadOnly = true;
            txtKey.ScrollBars = ScrollBars.Vertical;
            txtKey.WordWrap = true;
            StyleField(txtKey, "Consolas", 8.5F);
            txtKey.BackColor = Color.FromArgb(14, 20, 28);
            txtKey.ForeColor = Ok;
            Controls.Add(txtKey);
            y += 80;

            AddBtn("📋 Copy License", 24, y, 160, 32, Color.FromArgb(46, 125, 50), (s, e) => CopyKey());
            AddBtn("💾 Save .txt", 192, y, 120, 32, Color.FromArgb(60, 90, 120), (s, e) => SaveKey());
            AddBtn("🔄 ပြန်ထုတ်", 320, y, 110, 32, Color.FromArgb(60, 90, 120), (s, e) => Generate());
            lblKeyState2.Location = new Point(444, y + 6);
            lblKeyState2.Size = new Size(292, 20);
            lblKeyState2.TextAlign = ContentAlignment.MiddleRight;
            lblKeyState2.Font = new Font("Segoe UI", 8.5F);
            lblKeyState2.ForeColor = Muted;
            Controls.Add(lblKeyState2);
            y += 42;

            // ===== history =====
            Controls.Add(MakeLabel("ထုတ်ပြီးသား စာရင်း (issued_licenses.log)", 24, y, 400, 20, Muted, 8.5F));
            AddBtn("📂 Log folder", 570, y - 4, 100, 26, Color.FromArgb(60, 90, 120), (s, e) => OpenLogFolder());
            AddBtn("🔄", 676, y - 4, 60, 26, Color.FromArgb(60, 90, 120), (s, e) => LoadHistory());
            y += 22;
            lstHistory.Location = new Point(24, y);
            lstHistory.Size = new Size(712, 120);
            lstHistory.View = View.Details;
            lstHistory.FullRowSelect = true;
            lstHistory.GridLines = false;
            lstHistory.BackColor = Color.FromArgb(14, 20, 28);
            lstHistory.ForeColor = Color.FromArgb(215, 228, 242);
            lstHistory.Columns.Add("အချိန်", 130);
            lstHistory.Columns.Add("ဆိုင်", 170);
            lstHistory.Columns.Add("Machine ID", 230);
            lstHistory.Columns.Add("သက်တမ်း", 90);
            lstHistory.Columns.Add("Key (ဖြတ်)", 80);
            lstHistory.DoubleClick += (s, e) => CopySelectedHistory();
            Controls.Add(lstHistory);
            y += 126;

            AddBtn("📋 Copy selected", 24, y, 160, 30, Color.FromArgb(60, 90, 120), (s, e) => CopySelectedHistory());
            AddBtn("🗑 ဖျက် (log ရှင်း)", 192, y, 150, 30, Color.FromArgb(90, 60, 60), (s, e) => ClearHistory());
            lblStatus.Location = new Point(354, y + 5);
            lblStatus.Size = new Size(382, 20);
            lblStatus.TextAlign = ContentAlignment.MiddleRight;
            lblStatus.Font = new Font("Segoe UI", 8.5F);
            Controls.Add(lblStatus);

            LoadKeyFile(null);
            ValidateId();
            LoadHistory();
        }

        private readonly Label lblKeyState2 = new Label();

        // =====================================================================
        //  UI helpers
        // =====================================================================
        private static Label MakeLabel(string text, int x, int y, int w, int h, Color color, float size = 9F, FontStyle style = FontStyle.Regular)
            => new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), ForeColor = color, Font = new Font("Segoe UI", size, style), BackColor = Color.Transparent };

        private static void StyleField(TextBox t, string font, float size)
        {
            t.BackColor = Field;
            t.ForeColor = Color.White;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = new Font(font, size);
        }

        private Button AddBtn(string text, int x, int y, int w, int h, Color back, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                BackColor = back, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += onClick;
            Controls.Add(b);
            return b;
        }

        private void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        // =====================================================================
        //  ID
        // =====================================================================
        private void PasteId()
        {
            try
            {
                if (Clipboard.ContainsText()) txtId.Text = Clipboard.GetText().Trim();
            }
            catch { }
        }

        private static string NormalizeId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new StringBuilder();
            foreach (char c in raw)
                if (Uri.IsHexDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private static string DisplayId(string hex32)
        {
            if (hex32.Length != 32) return hex32;
            var parts = new List<string>();
            for (int i = 0; i < 32; i += 4) parts.Add(hex32.Substring(i, 4));
            return string.Join("-", parts);
        }

        private void ValidateId()
        {
            string id = NormalizeId(txtId.Text);
            if (id.Length == 0)
            {
                lblIdState.Text = "ID ကို paste/ရိုက်ထည့်ပါ (ဥပမာ 3dcc-5b34-fa31-…)";
                lblIdState.ForeColor = Muted;
                btnGenerate.Enabled = false;
                return;
            }
            if (id.Length != 32)
            {
                lblIdState.Text = $"❌ ID မမှန် — hex {id.Length}/32 လုံး (dash တွေပါလည်း ရတယ်)";
                lblIdState.ForeColor = Err;
                btnGenerate.Enabled = false;
                return;
            }
            lblIdState.Text = $"✅ ID မှန် — {DisplayId(id)}";
            lblIdState.ForeColor = Ok;
            btnGenerate.Enabled = true;
        }

        private void SetDays(string days)
        {
            txtDays.Text = days;
            SetStatus(days.Length == 0 ? "သက်တမ်းမကုန် (permanent)" : $"သက်တမ်း {days} ရက်", Info);
        }

        // =====================================================================
        //  Key file
        // =====================================================================
        private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "license_maker.cfg");

        private static IEnumerable<string> CandidateKeyPaths()
        {
            string exeDir = AppContext.BaseDirectory;
            yield return Path.Combine(exeDir, "activation_key.pem");
            yield return Path.Combine(exeDir, "private", "activation_key.pem");
            // repo ထဲကနေ run ရင် (bin/Debug → ../../../tools/private)
            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
                yield return Path.Combine(dir.FullName, "tools", "private", "activation_key.pem");
            yield return @"C:\Users\PMK\source\repos\WinFormsApp1\tools\private\activation_key.pem";
        }

        private void LoadKeyFile(string pathFromSettings)
        {
            string path = pathFromSettings;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                try { if (File.Exists(SettingsPath)) path = File.ReadAllText(SettingsPath).Trim(); } catch { }
            }
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                foreach (var c in CandidateKeyPaths())
                    if (File.Exists(c)) { path = c; break; }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                keyPath = null; signer = null; keyFingerprint = "";
                lblKeyState.Text = "⚠️ activation_key.pem မတွေ့ — ⚙ Key File… နဲ့ ရွေးပါ";
                lblKeyState.ForeColor = Err;
                SetStatus("Private key မရှိလို့ license မထုတ်နိုင်ပါ", Err);
                btnGenerate.Enabled = false;
                return;
            }

            keyPath = path;
            try
            {
                signer = RSA.Create();
                signer.ImportFromPem(File.ReadAllText(path));

                string pubDerived = signer.ExportSubjectPublicKeyInfoPem();
                keyFingerprint = Convert.ToHexString(SHA256.HashData(signer.ExportSubjectPublicKeyInfo()))[..16].ToLowerInvariant();
                keyMatchesApp = NormalizePem(pubDerived) == NormalizePem(AppPublicKeyPem);

                lblKeyState.Text = (keyMatchesApp ? "✅ " : "⚠️ ") + $"key {keyFingerprint}…" + (keyMatchesApp ? " (app နဲ့ ကိုက်)" : " (app နဲ့ မကိုက်!)");
                lblKeyState.ForeColor = keyMatchesApp ? Ok : Warn;
                SetStatus($"Key: {Path.GetFileName(path)}  ·  {keyFingerprint}", keyMatchesApp ? Info : Warn);
                ValidateId();
            }
            catch (Exception ex)
            {
                signer = null; keyFingerprint = "";
                lblKeyState.Text = "❌ key ဖတ်လို့မရပါ";
                lblKeyState.ForeColor = Err;
                SetStatus("key ဖတ်ရာမှာ error: " + ex.Message, Err);
                btnGenerate.Enabled = false;
            }
        }

        private static string NormalizePem(string pem)
            => (pem ?? "").Replace("\r", "").Replace("\n", "").Replace(" ", "").Trim();

        private void ChooseKeyFile()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "activation_key.pem ကို ရွေးပါ",
                Filter = "PEM key (*.pem)|*.pem|All files (*.*)|*.*"
            };
            string start = keyPath != null ? Path.GetDirectoryName(keyPath) : AppContext.BaseDirectory;
            if (Directory.Exists(start)) dlg.InitialDirectory = start;
            if (dlg.ShowDialog() != DialogResult.OK) return;

            try { File.WriteAllText(SettingsPath, dlg.FileName); } catch { }
            LoadKeyFile(dlg.FileName);
            if (!keyMatchesApp && signer != null)
                MessageBox.Show(this,
                    "သတိထားပါ — ဒီ key က app ရဲ့ public key နဲ့ မကိုက်ပါ။\n" +
                    "ဒီ key နဲ့ ထုတ်လိုက်တဲ့ license တွေ tool မှာ အလုပ်မလုပ်ပါ။\n" +
                    "keypair လဲထားတာ မှန်ရင် WinFormsApp1/License.cs ထဲက public key ကိုပါ update လုပ်ပါ။",
                    "Key မကိုက်ပါ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // =====================================================================
        //  Generate / Verify / Copy
        // =====================================================================
        private void Generate()
        {
            string id = NormalizeId(txtId.Text);
            if (id.Length != 32) { SetStatus("Installation ID မမှန် — hex 32 လုံး လိုပါတယ်", Err); return; }
            string shop = (txtShop.Text ?? "").Trim().Replace("|", "_").Replace("\r", " ").Replace("\n", " ");
            if (shop.Length == 0) { SetStatus("ဆိုင်နာမည် ထည့်ပါ", Err); txtShop.Focus(); return; }
            if (signer == null) { SetStatus("Private key မရှိပါ — ⚙ Key File… နဲ့ ရွေးပါ", Err); return; }

            string expiry = "none";
            string daysText = (txtDays.Text ?? "").Trim();
            if (daysText.Length > 0)
            {
                if (!int.TryParse(daysText, out int days) || days < 1 || days > 3650)
                { SetStatus("ရက် အရေအတွက် မမှန် (1–3650)", Err); return; }
                expiry = DateTime.Today.AddDays(days).ToString("yyyy-MM-dd");
            }

            if (!keyMatchesApp)
            {
                var c = MessageBox.Show(this,
                    "⚠️ ဒီ key က app ရဲ့ public key နဲ့ မကိုက်ပါ — ထုတ်လိုက်တဲ့ license က tool မှာ အလုပ်မလုပ်နိုင်ပါ။\n\nဆက်ထုတ်မလား?",
                    "Key မကိုက်ပါ", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (c != DialogResult.Yes) return;
            }

            try
            {
                string payload = $"{Prefix}|{id}|{shop}|{expiry}";
                byte[] sig = signer.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                string key = payload + "|" + Convert.ToBase64String(sig);

                // ထုတ်ပြီး ချက်ချင်း ပြန်စစ် (app public key နဲ့) — မှားရင် မပြ
                if (!VerifyKeyWithAppKey(key, out string why))
                {
                    SetStatus("❌ ထုတ်လိုက်တဲ့ key ကို ပြန်စစ်တာ မအောင်ဘူး: " + why, Err);
                    return;
                }

                lastGenerated = key;
                txtKey.Text = key;
                string shown = expiry == "none" ? "သက်တမ်းမကုန်" : expiry + " အထိ";
                lblKeyState2.Text = $"✅ Verified · {shop} · {shown}";
                lblKeyState2.ForeColor = Ok;
                SetStatus($"ထုတ်ပြီး ✅  {shop}  ·  {shown}", Ok);

                AppendLog(shop, id, expiry, key);
                LoadHistory();
                try { Clipboard.SetText(key); SetStatus("ထုတ်ပြီး + clipboard ကို copy လုပ်ပြီးပါပြီ 📋", Ok); } catch { }
            }
            catch (Exception ex)
            {
                SetStatus("Generate error: " + ex.Message, Err);
            }
        }

        private static bool VerifyKeyWithAppKey(string key, out string why)
        {
            why = "";
            try
            {
                string[] p = (key ?? "").Trim().Split('|');
                if (p.Length != 5) { why = "အပိုင်း ၅ ပိုင်း မရှိ"; return false; }
                if (p[0] != Prefix) { why = "prefix မမှန်"; return false; }
                using var rsa = RSA.Create();
                rsa.ImportFromPem(AppPublicKeyPem);
                byte[] data = Encoding.UTF8.GetBytes($"{p[0]}|{p[1]}|{p[2]}|{p[3]}");
                bool ok = rsa.VerifyData(data, Convert.FromBase64String(p[4]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                if (!ok) why = "signature မကိုက်";
                return ok;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        private void VerifyKeyBox()
        {
            string key = (txtKey.Text ?? "").Trim();
            if (key.Length == 0) { SetStatus("စစ်ရန် key မရှိပါ — အရင် Generate လုပ်ပါ", Warn); return; }
            if (!VerifyKeyWithAppKey(key, out string why)) { SetStatus("❌ Key မမှန် — " + why, Err); return; }

            string[] p = key.Split('|');
            string exp = p[3] == "none" ? "သက်တမ်းမကုန်" : p[3];
            bool expired = p[3] != "none" && DateTime.TryParse(p[3], out var d) && d.Date < DateTime.Today;
            SetStatus(expired
                ? $"⚠️ Key မှန်တယ် ဒါပေမဲ့ သက်တမ်းကုန်နေပြီ ({p[3]})"
                : $"✅ Key မှန် — {p[2]} · {exp}", expired ? Warn : Ok);
        }

        private void CopyKey()
        {
            string key = lastGenerated.Length > 0 ? lastGenerated : (txtKey.Text ?? "").Trim();
            if (key.Length == 0) { SetStatus("Copy လုပ်ရန် key မရှိပါ", Warn); return; }
            try { Clipboard.SetText(key); SetStatus("📋 License ကို clipboard ထဲ copy လုပ်ပြီးပါပြီ — ဝယ်သူကို ပို့ပါ", Ok); }
            catch (Exception ex) { SetStatus("Copy error: " + ex.Message, Err); }
        }

        private void SaveKey()
        {
            string key = (txtKey.Text ?? "").Trim();
            if (key.Length == 0) { SetStatus("သိမ်းရန် key မရှိပါ", Warn); return; }
            string shop = (txtShop.Text ?? "license").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) shop = shop.Replace(c, '_');
            using var dlg = new SaveFileDialog { FileName = $"license_{shop}_{DateTime.Now:yyyyMMdd_HHmm}.txt", Filter = "Text (*.txt)|*.txt" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                File.WriteAllText(dlg.FileName,
                    $"PMK Unlock Tool — License\r\nShop   : {txtShop.Text}\r\nMachine: {DisplayId(NormalizeId(txtId.Text))}\r\n" +
                    $"Issued : {DateTime.Now:yyyy-MM-dd HH:mm}\r\nExpiry : {(txtDays.Text.Trim().Length == 0 ? "none (permanent)" : txtDays.Text.Trim() + " days")}\r\n\r\n{key}\r\n");
                SetStatus("💾 သိမ်းပြီး: " + dlg.FileName, Ok);
            }
            catch (Exception ex) { SetStatus("Save error: " + ex.Message, Err); }
        }

        // =====================================================================
        //  Log / history
        // =====================================================================
        private string LogPath
        {
            get
            {
                // key ဖိုင်ရှိတဲ့ folder ထဲမှာပဲ (tools\private\ — gitignore ဖြစ်ပြီးသား)
                string dir = keyPath != null ? Path.GetDirectoryName(keyPath) : AppContext.BaseDirectory;
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "issued_licenses.log");
            }
        }

        private void AppendLog(string shop, string machineHex, string expiry, string key)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{shop}\t{DisplayId(machineHex)}\t{expiry}\t{key}\r\n";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
            catch { }
        }

        private void LoadHistory()
        {
            lstHistory.Items.Clear();
            try
            {
                if (!File.Exists(LogPath)) return;
                var lines = File.ReadAllLines(LogPath);
                int start = Math.Max(0, lines.Length - 200);
                for (int i = start; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length < 5) continue;
                    var it = new ListViewItem(p[0]);
                    it.SubItems.Add(p[1]);
                    it.SubItems.Add(p[2]);
                    it.SubItems.Add(p[3]);
                    it.SubItems.Add(p[4].Length > 12 ? p[4].Substring(0, 12) + "…" : p[4]);
                    it.Tag = p[4];
                    lstHistory.Items.Add(it);
                }
            }
            catch { }
        }

        private void CopySelectedHistory()
        {
            if (lstHistory.SelectedItems.Count == 0) { SetStatus("စာရင်းထဲက တစ်ခု ရွေးပါ", Warn); return; }
            string key = lstHistory.SelectedItems[0].Tag as string;
            if (string.IsNullOrEmpty(key)) return;
            try
            {
                Clipboard.SetText(key);
                lastGenerated = key;
                txtKey.Text = key;
                SetStatus("📋 ရွေးထားတဲ့ license ကို copy လုပ်ပြီးပါပြီ (ပြန်ပို့လို့ရ)", Ok);
            }
            catch (Exception ex) { SetStatus("Copy error: " + ex.Message, Err); }
        }

        private void ClearHistory()
        {
            if (!File.Exists(LogPath)) { SetStatus("log ဖိုင် မရှိပါ", Warn); return; }
            if (MessageBox.Show(this, "ထုတ်ပြီးသား စာရင်း (log) ကို ရှင်းမလား?\nပြန်လိုချင်ရင် ပြန်ရနိုင်မှာ မဟုတ်ပါ (backup ယူထားပါ)။",
                    "Log ရှင်း", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { File.Delete(LogPath); LoadHistory(); SetStatus("Log ရှင်းပြီး", Ok); }
            catch (Exception ex) { SetStatus("ဖျက်လို့မရပါ: " + ex.Message, Err); }
        }

        private void OpenLogFolder()
        {
            try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(LogPath)) { UseShellExecute = true }); } catch { }
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                "PMK License Maker v1.0\n\n" +
                "· Installation ID paste → ဆိုင်နာမည် → သက်တမ်း → GENERATE → Copy → ဝယ်သူကို ပို့\n" +
                "· license က အဲဒီ PC နဲ့ပဲ ချိတ်တယ် (HWID) — တစ်ခြား PC ကို ကူးလို့ မရ\n" +
                "· Private key: activation_key.pem (tools\\private\\) — backup ၂ ခု ယူထားပါ၊ ပျောက်ရင် license အကုန် ပြန်ထုတ်ရမယ်\n" +
                "· ထုတ်ပြီးသား စာရင်း: issued_licenses.log\n\n" +
                "⚠️ ဒီ tool ကို ဝယ်သူတွေဆီ ဘယ်တော့မှ မဖြန့်ရ။ Release zip/installer ထဲ မထည့်ရ။",
                "About — PMK License Maker", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) { base.OnPaintBackground(e); return; }
            try
            {
                using var b = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, Bg, Color.FromArgb(12, 18, 26), System.Drawing.Drawing2D.LinearGradientMode.Vertical);
                e.Graphics.FillRectangle(b, ClientRectangle);
            }
            catch { base.OnPaintBackground(e); }
        }
    }
}
