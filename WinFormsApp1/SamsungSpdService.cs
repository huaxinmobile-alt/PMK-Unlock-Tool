#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Samsung (MTP/ADB) + Spreadtrum (SPD) business logic service — Form1 ကနေ ခွဲထုတ်ထားတယ်
    public class SamsungSpdService
    {
        private static readonly Color SamsungColor = Color.FromArgb(171, 71, 188);
        private static readonly Color SpdColor = Color.FromArgb(0, 188, 212);
        private static readonly Color SuccessColor = Color.FromArgb(129, 199, 132);
        private static readonly Color ErrorColor = Color.FromArgb(229, 115, 115);
        private static readonly Color WarningColor = Color.FromArgb(255, 213, 79);
        private static readonly Color InfoColor = Color.FromArgb(200, 200, 220);
        private static readonly Color FastbootColor = Color.FromArgb(255, 167, 38);

        private readonly LogHandler _log;
        private readonly Action<int, string> _updateProgress;
        private readonly Action<string> _setStatus;
        private readonly Action<bool> _setOperationState;
        private readonly ProcessRunnerService _processRunner;
        private readonly Func<string> _getAdbPath;

        public SamsungSpdService(
            LogHandler log,
            Action<int, string> updateProgress,
            Action<string> setStatus,
            Action<bool> setOperationState,
            ProcessRunnerService processRunner,
            Func<string> getAdbPath)
        {
            _log = log ?? delegate { };
            _updateProgress = updateProgress ?? delegate { };
            _setStatus = setStatus ?? delegate { };
            _setOperationState = setOperationState ?? delegate { };
            _processRunner = processRunner;
            _getAdbPath = getAdbPath ?? (() => "adb");
        }

        // ================= Spreadtrum =================
        public async Task SpdGetDeviceInfoAsync()
        {
            _log("\n📱 [SPD] Reading Spreadtrum Device Hardware Info...", SpdColor);
            string platform = await _processRunner.RunAdbTargeted("shell getprop ro.board.platform", "", false);
            string chip = await _processRunner.RunAdbTargeted("shell getprop ro.hardware", "", false);
            string model = await _processRunner.RunAdbTargeted("shell getprop ro.product.model", "", false);

            _log("╔══════════════════════════════════════════════════════════╗", SpdColor);
            _log("║             📱 SPREADTRUM / UNISOC INFO                  ║", SpdColor);
            _log("╚══════════════════════════════════════════════════════════╝", SpdColor);
            _log($"  • Model Name       : {model?.Trim() ?? "N/A"}", SuccessColor);
            _log($"  • Platform         : {platform?.Trim() ?? "N/A"}", InfoColor);
            _log($"  • Chipset          : {chip?.Trim() ?? "N/A"}", InfoColor);
            _log("────────────────────────────────────────────────────────────\n", SpdColor);
        }

        public async Task SpdFrpResetAsync()
        {
            _log("\n🔓 [SPD] Resetting Spreadtrum FRP...", SpdColor);
            await _processRunner.RunAdbTargeted("shell pm clear com.google.android.setupwizard", "Clearing SetupWizard...", false);
            await _processRunner.RunAdbTargeted("reboot", "Rebooting...", false);
            _log("✅ SPD FRP Reset command sent! Phone is restarting.", SuccessColor);
        }

        // ================= Samsung (ADB / MTP) =================
        public async Task GetDeviceInfoAsync()
        {
            _log("\n📱 [Samsung] Reading Samsung Device Info (MTP / ADB)...", SamsungColor);
            string model = await _processRunner.RunAdbTargeted("shell getprop ro.product.model", "", false);
            string csc = await _processRunner.RunAdbTargeted("shell getprop ro.csc.sales_code", "", false);
            string build = await _processRunner.RunAdbTargeted("shell getprop ro.build.display.id", "", false);
            string oneui = await _processRunner.RunAdbTargeted("shell getprop ro.build.version.oneui", "", false);

            _log("╔══════════════════════════════════════════════════════════╗", SamsungColor);
            _log("║             📱 SAMSUNG DEVICE INFORMATION                ║", SamsungColor);
            _log("╚══════════════════════════════════════════════════════════╝", SamsungColor);
            _log($"  • Model Name       : {model?.Trim() ?? "N/A"}", SuccessColor);
            _log($"  • CSC / Region     : {csc?.Trim() ?? "N/A"}", FastbootColor);
            _log($"  • One UI Version   : {oneui?.Trim() ?? "N/A"}", InfoColor);
            _log($"  • PDA / Build      : {build?.Trim() ?? "N/A"}", InfoColor);
            _log("────────────────────────────────────────────────────────────\n", SamsungColor);
        }

        public async Task RebootToDownloadAsync()
        {
            _log("\n⚡ Rebooting Samsung device to Download Mode...", SamsungColor);
            await _processRunner.RunAdbTargeted("reboot download", "To Download Mode...");
        }

        public async Task RebootToSystemAsync()
        {
            _log("\n🔄 Rebooting Samsung device to System...", SamsungColor);
            await _processRunner.RunAdbTargeted("reboot", "Rebooting...");
        }

        public async Task ReadPitAsync()
        {
            _log("\n📋 Reading Samsung PIT Partition Table in Download Mode...", SamsungColor);
            _log("📱 Connect phone in Download Mode (Vol Down + Power + Insert USB)", InfoColor);
            await Task.Delay(100);
        }

        // Samsung MTP FRP Exploit — COM port (AT) scan + ADB authorization wait
        // (delegates တွေက Form1 ဘက်က thread-safe marshaling လုပ်ပေးတယ် — Invoke wrapper မလို)
        public async Task MtpFrpResetAsync()
        {
            _log("\n╔══════════════════════════════════════════════════════════╗", SamsungColor);
            _log("║         🔓 SAMSUNG MTP ONE-CLICK FRP RESET               ║", SamsungColor);
            _log("╚══════════════════════════════════════════════════════════╝", SamsungColor);
            _log("📱 1. Power ON device to Welcome Screen.", SamsungColor);
            _log("📱 2. Click [Emergency Call] -> Type: *#0*# (or *#*#88#*#*)", SamsungColor);
            _log("📱 3. Test Menu screen must appear on phone!", SamsungColor);
            _log("⏳ Sending AT Commands to trigger USB Debugging...", SamsungColor);

            _setOperationState(true);
            _setStatus("Running Samsung MTP FRP...");
            _updateProgress(10, "Detecting Modem...");

            await Task.Run(async () =>
            {
                string[] ports = SerialPort.GetPortNames();
                bool found = false;

                foreach (string portName in ports)
                {
                    try
                    {
                        using SerialPort sp = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
                        sp.ReadTimeout = 1000;
                        sp.WriteTimeout = 1000;
                        sp.Open();

                        sp.WriteLine("AT\r\n");
                        Thread.Sleep(200);
                        string res = sp.ReadExisting();

                        if (res.Contains("OK"))
                        {
                            found = true;
                            _log($"⚡ Found Samsung Modem Port on [{portName}]!", SuccessColor);

                            string[] atCmds = {
                                "AT+KICOMPATIBILITY=0\r\n",
                                "AT+DUMPCTRL=1,0\r\n",
                                "AT+DEBUGLVC=0,5\r\n",
                                "AT+SWVERSION=1\r\n",
                                "AT+ACTIVATE=0,0,0\r\n"
                            };

                            foreach (var cmd in atCmds)
                            {
                                sp.WriteLine(cmd);
                                Thread.Sleep(300);
                            }
                            break;
                        }
                    }
                    catch (Exception ex) { _log($"⚠️ btnSamMtpFrp_Click error: {ex.Message}", WarningColor); }
                }

                if (!found)
                {
                    _log("❌ Samsung Modem COM Port not found. Ensure Samsung USB Drivers are installed.", ErrorColor);
                    _setOperationState(false);
                    _setStatus("Ready");
                    return;
                }

                _log("🚀 Exploit sent! Look at the phone screen and tap [ALLOW ALWAYS] for USB Debugging.", SuccessColor);
                _updateProgress(50, "Waiting for ADB...");

                for (int i = 1; i <= 15; i++)
                {
                    _setStatus($"Waiting for ADB Authorization ({i}/15)...");
                    string dev = await _processRunner.RunProcessCommand(_getAdbPath(), "devices", "", false);
                    if (dev != null && dev.Contains("\tdevice"))
                    {
                        _log("✅ ADB Device Authorized! Resetting FRP...", SuccessColor);
                        _updateProgress(85, "Resetting FRP...");

                        await _processRunner.RunProcessCommand(_getAdbPath(), "shell content insert --uri content://settings/secure --bind name:s:user_setup_complete --bind value:s:1", "", false);
                        await _processRunner.RunProcessCommand(_getAdbPath(), "shell pm clear com.sec.android.app.SecSetupWizard", "", false);
                        await _processRunner.RunProcessCommand(_getAdbPath(), "shell pm clear com.google.android.setupwizard", "", false);
                        await _processRunner.RunProcessCommand(_getAdbPath(), "reboot", "", false);

                        _updateProgress(100, "Done");
                        _log("🎉 Samsung FRP Reset Successfully! Phone is rebooting to Home.", SuccessColor);
                        _setOperationState(false);
                        _setStatus("Ready");
                        return;
                    }
                    await Task.Delay(2000);
                }

                _log("⚠️ ADB authorization timeout. Please retry.", WarningColor);
                _setOperationState(false);
                _setStatus("Ready");
                _updateProgress(0, "");
            });
        }
    }
}
