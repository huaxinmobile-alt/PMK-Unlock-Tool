using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Form1 ရဲ့ UI layout creation / control factory methods တွေကို ခွဲထုတ်ထားတဲ့ static builder
    public static class UIBuilder
    {
        // ================= UI Helpers =================
        public static Label CreateSeaLabel(string text, Point location, bool heading) => new Label { Text = text, Location = location, AutoSize = true, ForeColor = heading ? Color.FromArgb(210, 225, 240) : Color.FromArgb(175, 190, 205), Font = new Font("Segoe UI", heading ? 9F : 8.5F, heading ? FontStyle.Bold : FontStyle.Regular) };
        public static CheckBox CreateSeaCheckBox(string text, Point location) => new CheckBox { Text = text, Location = location, AutoSize = true, ForeColor = Color.FromArgb(205, 215, 225) };
        public static TextBox CreateServiceTextBox(Point location, int width) => new TextBox { Location = location, Size = new Size(width, 25), BackColor = Color.FromArgb(40, 50, 65), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        public static Button CreateSeaButton(string text, Point location, int width, int height, EventHandler handler)
        {
            Button b = new Button { Text = text, Location = location, Size = new Size(width, height), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(47, 72, 101), ForeColor = Color.White, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), Margin = new Padding(3) };
            Form1.Ui3D.Restyle3D(b);
            b.Click += handler;
            return b;
        }

        public static void BuildMainLayout(Form1 form)
        {
            form.tabControl.Visible = false;
            form.devicePanel.Visible = false;
            form.topPanel.Visible = false;

            form.mobileSeaShell = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 24, 32), Padding = new Padding(6), AllowDrop = true };

            // ===== Left Panel =====
            Panel leftPanel = new Panel { Dock = DockStyle.Left, Width = 430, BackColor = Color.FromArgb(14, 20, 27), Padding = new Padding(6) };

            Form1.BevelCardPanel connectionPanel = new Form1.BevelCardPanel { Dock = DockStyle.Top, Height = 95, BackColor = Color.FromArgb(27, 36, 48) };
            Label connectionTitle = CreateSeaLabel("🔌  CONNECTION", new Point(10, 8), true);
            Label portLabel = CreateSeaLabel("Communications Port", new Point(10, 36), false);

            form.mobilePortCombo = new ComboBox { Location = new Point(10, 58), Size = new Size(160, 25), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(35, 45, 58), ForeColor = Color.White };
            form.mobilePortCombo.Items.Add("Auto Detect / USB");
            form.mobilePortCombo.SelectedIndex = 0;

            CheckBox autoConnect = new CheckBox { Text = "Auto", Location = new Point(175, 60), AutoSize = true, Checked = true, ForeColor = Color.White };
            form.btnMobileGo = CreateSeaButton("🔄 Ref", new Point(230, 56), 55, 28, (s, e) => { form.RefreshPorts(); form.Log("🔄 Ports refreshed.", form.colorInfo); });
            Button btnDevMgr = CreateSeaButton("🛠️ DevMgr", new Point(290, 56), 65, 28, (s, e) => { try { Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }); } catch (Exception ex) { form.LogWarning($"⚠️ btnDevMgr_Click warning: {ex.Message}"); } });
            btnDevMgr.BackColor = Color.FromArgb(40, 70, 90);
            Form1.Ui3D.Restyle3D(btnDevMgr);

            Button btnDrivers = CreateSeaButton("📦 Driver", new Point(360, 56), 60, 28, (s, e) => form.InstallAllDrivers());
            btnDrivers.BackColor = Color.FromArgb(45, 75, 60);
            Form1.Ui3D.Restyle3D(btnDrivers);

            connectionPanel.Controls.AddRange(new Control[] { connectionTitle, portLabel, form.mobilePortCombo, autoConnect, form.btnMobileGo, btnDevMgr, btnDrivers });

            Panel logHeader = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(35, 47, 61), Padding = new Padding(4) };
            logHeader.Controls.Add(CreateSeaLabel("📋  LOG", new Point(8, 9), true));

            Button btnStopOp = CreateSeaButton("🛑 STOP", new Point(160, 4), 90, 28, form.btnStop_Click);
            btnStopOp.BackColor = Color.FromArgb(220, 40, 40);
            Form1.Ui3D.Restyle3D(btnStopOp);
            btnStopOp.ForeColor = Color.White;
            btnStopOp.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            logHeader.Controls.Add(btnStopOp);

            Button btnSaveLog = CreateSeaButton("💾 Save", new Point(260, 4), 70, 28, (s, e) => form.ExportLogToFile());
            btnSaveLog.BackColor = Color.FromArgb(45, 75, 60);
            Form1.Ui3D.Restyle3D(btnSaveLog);
            logHeader.Controls.Add(btnSaveLog);

            Button btnClear = CreateSeaButton("🗑️ Clear", new Point(338, 4), 70, 28, (s, e) => { form.rtbOutput.Clear(); form.Log("form.Log cleared", form.colorWarning); });
            btnClear.BackColor = Color.FromArgb(50, 60, 75);
            Form1.Ui3D.Restyle3D(btnClear);
            logHeader.Controls.Add(btnClear);

            form.rtbOutput.Parent = leftPanel;
            form.rtbOutput.Dock = DockStyle.Fill;
            form.rtbOutput.Margin = new Padding(0);
            form.rtbOutput.BackColor = Color.FromArgb(10, 16, 22);
            form.rtbOutput.ForeColor = Color.FromArgb(210, 220, 230);
            form.rtbOutput.BorderStyle = BorderStyle.FixedSingle;
            form.rtbOutput.WordWrap = false;

            leftPanel.Controls.Add(form.rtbOutput);
            leftPanel.Controls.Add(logHeader);
            leftPanel.Controls.Add(connectionPanel);

            // ===== Right Panel =====
            Panel rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 29, 39), Padding = new Padding(8) };
            // AutoScroll မလိုအောင်: 8 tabs (93px + 5px gap = 787px) က bar အတွင်း အကုန်အဆင်ပြေဝင်တယ် —
            // scrollbar ပေါ်ရင် tab အောက်ခြေတွေ ဖုံးခံရလို့ AutoScroll ပိတ်ထားတယ်
            FlowLayoutPanel categoryBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(31, 41, 55), Padding = new Padding(4, 6, 4, 6), WrapContents = false, AutoScroll = false };

            string[] categories = { "Qualcomm", "MediaTek", "ADB", "Fastboot", "Sideload", "Spreadtrum", "Samsung", "Settings" };
            form.categoryTabButtons.Clear();
            foreach (string category in categories)
            {
                var catBtn = new Form1.Tab3DButton
                {
                    Text = category,
                    Size = new Size(93, 36),
                    Margin = new Padding(0, 0, 5, 0),
                    BackColor = Color.FromArgb(47, 72, 101),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold)
                };
                catBtn.Click += (s, e) => form.SwitchCategory(catBtn.Text);
                form.categoryTabButtons.Add(catBtn);
                categoryBar.Controls.Add(catBtn);
            }

            // 🎨 Settings tab panel (theme + PC info) — theme selector က အခု Settings ထဲမှာပဲ
            form.BuildSettingsPanel(rightPanel);

            // ===== Profile Panel with TP Pinout Button =====
            form.profilePanel = new Form1.BevelCardPanel { Dock = DockStyle.Top, Height = 75, BackColor = Color.FromArgb(27, 36, 48) };
            form.profilePanel.Controls.Add(CreateSeaLabel("PROFILE", new Point(10, 10), true));
            form.profilePanel.Controls.Add(CreateSeaLabel("Brand", new Point(75, 10), false));

            form.mobileBrandCombo = new ComboBox
            {
                Location = new Point(120, 7),
                Size = new Size(160, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 45, 58),
                ForeColor = Color.White
            };
            form.mobileBrandCombo.SelectedIndexChanged += form.MobileBrandCombo_SelectedIndexChanged;
            form.profilePanel.Controls.Add(form.mobileBrandCombo);

            form.profilePanel.Controls.Add(CreateSeaLabel("Model", new Point(290, 10), false));
            form.mobileModelCombo = new ComboBox
            {
                Location = new Point(335, 7),
                Size = new Size(200, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 45, 58),
                ForeColor = Color.White
            };
            form.mobileModelCombo.Items.Add("# Auto Detect");
            form.mobileModelCombo.SelectedIndex = 0;
            form.mobileModelCombo.SelectedIndexChanged += form.MobileModelCombo_SelectedIndexChanged;
            form.profilePanel.Controls.Add(form.mobileModelCombo);

            form.btnShowTp = CreateSeaButton("📌 Pinout", new Point(545, 6), 85, 27, (s, e) =>
            {
                string brand = form.mobileBrandCombo.SelectedItem?.ToString() ?? "";
                string model = form.mobileModelCombo.SelectedItem?.ToString() ?? "";
                form.ShowTestPointViewer(brand, model);
            });
            form.btnShowTp.BackColor = Color.FromArgb(255, 152, 0);
            Form1.Ui3D.Restyle3D(form.btnShowTp);
            form.profilePanel.Controls.Add(form.btnShowTp);

            form.lblLoaderTitle = CreateSeaLabel("📁 Firehose Loader:", new Point(10, 42), false);
            form.txtFirmwarePath = CreateServiceTextBox(new Point(140, 40), 395);
            form.btnBrowseLoader = CreateSeaButton("📂 Browse", new Point(545, 38), 85, 26, form.BrowseFirmware_Click);

            form.profilePanel.Controls.AddRange(new Control[] { form.lblLoaderTitle, form.txtFirmwarePath, form.btnBrowseLoader });

            form.dynamicActionPanel = new Form1.BevelCardPanel { Dock = DockStyle.Top, Height = 135, BackColor = Color.FromArgb(25, 33, 44), Padding = new Padding(6), AutoScroll = true };

            // ===== Partition Grid Context Menu =====
            form.partitionContextMenu = new ContextMenuStrip();
            ToolStripMenuItem menuRead = new ToolStripMenuItem("📖 Read Partition (Dump)", null, (s, e) => form.ExecutePartitionAction("Read"));
            ToolStripMenuItem menuWrite = new ToolStripMenuItem("✏️ Write Partition (Flash)", null, (s, e) => form.ExecutePartitionAction("Write"));
            ToolStripMenuItem menuErase = new ToolStripMenuItem("🗑️ Erase Partition", null, (s, e) => form.ExecutePartitionAction("Erase"));
            ToolStripMenuItem menuFormat = new ToolStripMenuItem("🔄 Format Partition", null, (s, e) => form.ExecutePartitionAction("Format"));
            form.partitionContextMenu.Items.AddRange(new ToolStripItem[] { menuRead, menuWrite, menuErase, menuFormat });

            // ===== Partition Grid =====
            form.mobilePartitionGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(13, 20, 28),
                GridColor = Color.FromArgb(45, 60, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                ContextMenuStrip = form.partitionContextMenu,
                ColumnHeadersHeight = 28
            };
            form.mobilePartitionGrid.RowTemplate.Height = 24;

            form.mobilePartitionGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 44, 60);
            form.mobilePartitionGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(220, 235, 250);
            form.mobilePartitionGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            form.mobilePartitionGrid.DefaultCellStyle.BackColor = Color.FromArgb(18, 26, 36);
            form.mobilePartitionGrid.DefaultCellStyle.ForeColor = Color.FromArgb(220, 230, 245);
            form.mobilePartitionGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(33, 150, 243);
            form.mobilePartitionGrid.DefaultCellStyle.SelectionForeColor = Color.White;
            form.mobilePartitionGrid.DefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            form.mobilePartitionGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(23, 33, 46);
            form.mobilePartitionGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "✓", Width = 30, ReadOnly = false });
            form.mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Partition Name", Width = 150, ReadOnly = true });
            form.mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File Name", Width = 160, ReadOnly = true });
            form.mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Offset", Width = 140, ReadOnly = true });
            form.mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Length / Size", Width = 120, ReadOnly = true });
            form.mobilePartitionGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });

            form.mobilePartitionGrid.SelectionChanged += form.MobilePartitionGrid_SelectionChanged;
            form.mobilePartitionGrid.CellDoubleClick += form.MobilePartitionGrid_CellDoubleClick;

            // Checkbox tick ပြောင်းတာနဲ့ Flash Selected ခလုတ်ရဲ့ count ကို update လုပ်ဖို့
            form.mobilePartitionGrid.CellValueChanged += (s, e) => { if (form.qcFirmwarePreviewMode && e.RowIndex >= 0 && e.ColumnIndex == 0) form.UpdateFlashSelButtonText(); };
            form.mobilePartitionGrid.CurrentCellDirtyStateChanged += (s, e) => { if (form.mobilePartitionGrid.IsCurrentCellDirty) form.mobilePartitionGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };

            // Universal Multi-Brand Flasher Panel
            BuildFlasherHub(form, rightPanel);

            // ===== Sideload Package Row (Sideload tab မှာသာ ပေါ်မယ်) =====
            form.sideloadPanel = new Form1.BevelCardPanel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(27, 36, 48),
                Visible = false
            };
            form.sideloadPanel.Controls.Add(CreateSeaLabel("📦 Package (.zip):", new Point(10, 12), false));
            form.txtSideloadPath = CreateServiceTextBox(new Point(150, 9), 500);
            form.txtSideloadPath.ReadOnly = true;
            form.sideloadPanel.Controls.Add(form.txtSideloadPath);
            Button btnSlBrowse = CreateSeaButton("📂 Browse", new Point(665, 8), 85, 26, (s, e) =>
            {
                using var dlg = new OpenFileDialog { Title = "Select ZIP package to sideload (ROM/OTA/patch)", Filter = "ZIP Package (*.zip)|*.zip|All files (*.*)|*.*" };
                if (dlg.ShowDialog() == DialogResult.OK) form.txtSideloadPath.Text = dlg.FileName;
            });
            btnSlBrowse.BackColor = Color.FromArgb(33, 150, 243);
            Form1.Ui3D.Restyle3D(btnSlBrowse);
            form.sideloadPanel.Controls.Add(btnSlBrowse);

            // Footer Bar with Progress Bar
            Panel footerBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(20, 28, 38) };

            form.globalProgressBar = new ProgressBar
            {
                Location = new Point(8, 6),
                Size = new Size(300, 18),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            form.lblProgressPercent = new Label
            {
                Text = "0%",
                Location = new Point(315, 6),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 230, 118)
            };

            footerBar.Controls.Add(form.globalProgressBar);
            footerBar.Controls.Add(form.lblProgressPercent);

            // ===== Memory Type Selector (Qualcomm EDL: eMMC / UFS) =====
            form.lblMemType = CreateSeaLabel("Mem:", new Point(660, 8), false);
            form.lblMemType.ForeColor = Color.FromArgb(0, 230, 118);
            form.lblMemType.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);

            form.cboMemoryType = new ComboBox
            {
                Location = new Point(695, 4),
                Size = new Size(85, 23),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 45, 58),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5F)
            };
            form.cboMemoryType.Items.AddRange(new object[] { "eMMC", "UFS" });
            form.cboMemoryType.SelectedIndex = 0;
            form.cboMemoryType.SelectedIndexChanged += (s, e) =>
            {
                form.currentMemoryType = form.cboMemoryType.SelectedItem?.ToString() ?? "emmc";
            };

            footerBar.Controls.Add(form.lblMemType);
            footerBar.Controls.Add(form.cboMemoryType);

            rightPanel.Controls.Add(form.mobilePartitionGrid);
            rightPanel.Controls.Add(form.flasherHubPanel);
            if (form.settingsPanel != null) rightPanel.Controls.Add(form.settingsPanel);
            rightPanel.Controls.Add(footerBar);
            rightPanel.Controls.Add(form.dynamicActionPanel);
            if (form.sideloadPanel != null) rightPanel.Controls.Add(form.sideloadPanel);
            rightPanel.Controls.Add(form.profilePanel);
            rightPanel.Controls.Add(categoryBar);

            // PROFILE စာတန်းတွေ ဖတ်ရလွယ်အောင် ဖောင့် ပိုကြီး/ထူပေးတယ်
            foreach (Control c in form.profilePanel.Controls)
            {
                if (c is Label l)
                    l.Font = new Font("Segoe UI", l.Font.Bold ? 9.5F : 9.25F, FontStyle.Bold);
            }

            form.mobileSeaShell.Controls.Add(rightPanel);
            form.mobileSeaShell.Controls.Add(leftPanel);

            // ===== Top Brand Banner — PMK MOBILE SERVICE TOOL (gradient header) =====
            var banner = new Form1.GradientBannerHeader();
            banner.Dock = DockStyle.Top;
            banner.Height = 32;

            form.statusStrip.Dock = DockStyle.Bottom;
            form.statusStrip.BackColor = Color.FromArgb(12, 17, 23);
            form.statusStrip.ForeColor = Color.White;
            form.Controls.Add(form.mobileSeaShell);
            form.Controls.Add(form.statusStrip);
            form.Controls.Add(banner);
            form.Text = "PMK MOBILE SERVICE TOOL";
        }
        public static void BuildFlasherHub(Form1 form, Panel parentPanel)
        {
            form.flasherHubPanel = new Form1.BevelCardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 24, 32),
                Padding = new Padding(12),
                Visible = false
            };

            form.lblFlasherTitle = CreateSeaLabel("⚡ FIRMWARE FLASHING ENGINE", new Point(12, 8), true);
            form.lblFlasherTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            form.lblFlasherTitle.ForeColor = Color.FromArgb(100, 181, 246);
            form.flasherHubPanel.Controls.Add(form.lblFlasherTitle);

            int startY = 40;
            int gapY = 36;

            void CreateSlotRow(out CheckBox chk, out TextBox txt, out Button btn, int yPos, int slotIndex)
            {
                chk = new CheckBox
                {
                    Text = "SLOT",
                    Location = new Point(15, yPos + 3),
                    Size = new Size(125, 24),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Checked = true
                };

                txt = CreateServiceTextBox(new Point(145, yPos), 425);
                txt.ReadOnly = true;

                btn = CreateSeaButton("📂 Browse", new Point(580, yPos - 1), 90, 27, (s, e) => form.BrowseFlasherSlot(slotIndex));
                btn.BackColor = Color.FromArgb(40, 60, 85);
                Form1.Ui3D.Restyle3D(btn);

                form.flasherHubPanel.Controls.AddRange(new Control[] { chk, txt, btn });
            }

            { CheckBox cc1; TextBox tt1; Button bb1;
              CreateSlotRow(out cc1, out tt1, out bb1, startY, 1);
              form.chkSlot1 = cc1; form.txtSlot1 = tt1; form.btnBrowseSlot1 = bb1; }
            { CheckBox cc2; TextBox tt2; Button bb2;
              CreateSlotRow(out cc2, out tt2, out bb2, startY + gapY, 2);
              form.chkSlot2 = cc2; form.txtSlot2 = tt2; form.btnBrowseSlot2 = bb2; }
            { CheckBox cc3; TextBox tt3; Button bb3;
              CreateSlotRow(out cc3, out tt3, out bb3, startY + (gapY * 2), 3);
              form.chkSlot3 = cc3; form.txtSlot3 = tt3; form.btnBrowseSlot3 = bb3; }
            { CheckBox cc4; TextBox tt4; Button bb4;
              CreateSlotRow(out cc4, out tt4, out bb4, startY + (gapY * 3), 4);
              form.chkSlot4 = cc4; form.txtSlot4 = tt4; form.btnBrowseSlot4 = bb4; }
            { CheckBox cc5; TextBox tt5; Button bb5;
              CreateSlotRow(out cc5, out tt5, out bb5, startY + (gapY * 4), 5);
              form.chkSlot5 = cc5; form.txtSlot5 = tt5; form.btnBrowseSlot5 = bb5; }

            // Options Bar
            Panel optPanel = new Panel { Location = new Point(15, startY + (gapY * 5) + 6), Size = new Size(655, 36), BackColor = Color.FromArgb(25, 33, 44) };
            form.chkAutoRebootMaster = new CheckBox { Text = "Auto Reboot after Flash", Location = new Point(15, 8), AutoSize = true, Checked = true, ForeColor = Color.FromArgb(0, 230, 118), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
            optPanel.Controls.Add(form.chkAutoRebootMaster);
            form.flasherHubPanel.Controls.Add(optPanel);

            // Action Buttons
            form.btnMasterFlash = CreateSeaButton("⚡ START (FLASH FIRMWARE)", new Point(15, startY + (gapY * 6) + 10), 240, 40, (s, e) => form.ExecuteMasterFlash());
            form.btnMasterFlash.BackColor = Color.FromArgb(230, 60, 60);
            Form1.Ui3D.Restyle3D(form.btnMasterFlash);
            form.btnMasterFlash.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);

            form.btnResetFlasher = CreateSeaButton("🗑️ Reset Slots", new Point(265, startY + (gapY * 6) + 10), 120, 40, (s, e) => form.ResetFlasherSlots());
            form.btnResetFlasher.BackColor = Color.FromArgb(60, 70, 85);
            Form1.Ui3D.Restyle3D(form.btnResetFlasher);

            form.flasherHubPanel.Controls.AddRange(new Control[] { form.btnMasterFlash, form.btnResetFlasher });
            parentPanel.Controls.Add(form.flasherHubPanel);
        }
    }
}
