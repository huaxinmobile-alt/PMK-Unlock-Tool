#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Process execution / EDL smart-logging / device communication service
    // — Form1 (God Class) ကနေ ခွဲထုတ်ထားတယ်။ UI (log/progress/status) တွေကို delegate တွေကနေ ပြန်ခေါ်တယ်။
    public class ProcessRunnerService
    {
        // Form1 ရဲ့ color fields တွေနဲ့ တူညီတဲ့ အရောင်တွေ (log output အရောင်တွေ မပြောင်းစေရဘူး)
        private static readonly Color AdbColor = Color.FromArgb(100, 181, 246);
        private static readonly Color FastbootColor = Color.FromArgb(255, 167, 38);
        private static readonly Color MtkColor = Color.FromArgb(102, 187, 106);
        private static readonly Color QualcommColor = Color.FromArgb(239, 83, 80);
        private static readonly Color SuccessColor = Color.FromArgb(129, 199, 132);
        private static readonly Color ErrorColor = Color.FromArgb(229, 115, 115);
        private static readonly Color WarningColor = Color.FromArgb(255, 213, 79);
        private static readonly Color InfoColor = Color.FromArgb(200, 200, 220);

        private readonly LogHandler _log;
        private readonly Action<int, string> _updateProgress;
        private readonly Action<string> _setStatus;
        private readonly Action<bool> _setOperationState;
        private readonly Action _onLoaderUploaded;
        private readonly Func<string> _pythonPath;
        private readonly Func<string> _adbPath;
        private readonly Action<Action> _runOnUi;

        public ProcessRunnerService(
            LogHandler log,
            Action<int, string> updateProgress,
            Action<string> setStatus,
            Action<bool> setOperationState,
            Action onLoaderUploaded,
            Func<string> getPythonPath,
            Func<string> getAdbPath,
            Action<Action> runOnUi)
        {
            _log = log ?? delegate { };
            _updateProgress = updateProgress ?? delegate { };
            _setStatus = setStatus ?? delegate { };
            _setOperationState = setOperationState ?? delegate { };
            _onLoaderUploaded = onLoaderUploaded ?? delegate { };
            _pythonPath = getPythonPath ?? (() => "python");
            _adbPath = getAdbPath ?? (() => "adb");
            _runOnUi = runOnUi ?? (a => a());
        }

        // ================= Shared state (အရင် Form1 ရဲ့ private fields) =================
        public CancellationTokenSource Cts { get; set; }
        public Process CurrentProcess { get; set; }
        public int LastPythonExitCode { get; set; } = -1;   // python op တစ်ခုစီရဲ့ exit code (success စစ်ဖို့)
        public string DetectedChipset { get; set; } = "";    // python ကနေ ဖတ်လို့ရတဲ့ chipset (CPU detected)
        public string DetectedHwid { get; set; } = "";       // HWID (auto-loader learning အတွက်)
        public string DetectedPkhash { get; set; } = "";     // PK_HASH (auto-loader learning အတွက်)
        public bool DetectedLoaderMissing { get; set; } = false; // python က ဒီဖုန်းအတွက် loader မတွေ့ဘူးဆိုတဲ့ flag
        private string _lastSmartLine = "";
        private int _lastSmartRepeat = 0;

        // ================= btnStop_Click ရဲ့ core — operation အကုန် ရပ်တန့်ခြင်း =================
        public void CancelAllOperations()
        {
            try
            {
                Cts?.Cancel();

                if (CurrentProcess != null && !CurrentProcess.HasExited)
                {
                    CurrentProcess.Kill(entireProcessTree: true);
                }

                string[] processNames = { "python", "pythonw", "adb", "fastboot", "QSaharaServer", "fh_loader" };
                foreach (var pName in processNames)
                {
                    var running = Process.GetProcessesByName(pName);
                    foreach (var p in running)
                    {
                        try { p.Kill(); } catch (Exception) { /* Process already exited or access denied, safe to ignore */ }
                    }
                }
            }
            catch (Exception) { /* Operation already stopped or process exited — safe to ignore */ }
            finally
            {
                CurrentProcess = null;
                Cts = null;
            }
        }

        public async Task<bool> RunNativeSaharaLoader(string portName, string loaderPath)
        {
            string qSaharaExe = Path.Combine(AppConfig.QualcommCoreDir, "QSaharaServer.exe");
            if (!IOFile.Exists(qSaharaExe)) return false;

            string args = $"-p \\\\.\\{portName} -s 13:\"{loaderPath}\"";

            _log($"\n🚀 [Native Sahara] Uploading Loader via QSaharaServer: {Path.GetFileName(loaderPath)}...", QualcommColor);
            _updateProgress(25, "Uploading Loader...");

            // Loader upload (serial) က စက္ကန့် ၃၀-၉၀ ကြာနိုင်လို့ timeout 90s ပေးထားပြီး
            // cancel ဖြစ်ရင် COM port လွှတ်ဖို့ process ကို သေချာ kill လုပ်ပါတယ်
            using var saharaCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var output = new System.Text.StringBuilder();

            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = qSaharaExe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppConfig.QualcommCoreDir
            };

            try
            {
                p.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) output.AppendLine(e.Data); };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await p.WaitForExitAsync(saharaCts.Token);

                string result = output.ToString();
                // QSaharaServer build အချို့က success ဖြစ်ရင်တောင် ဘာမှ ထုတ်မပြတတ်လို့ exit code 0 ကို အဓိက စစ်ပါတယ်
                if (p.ExitCode == 0 ||
                    result.Contains("Sahara protocol completed") ||
                    result.Contains("Done sending payload") ||
                    result.Contains("File transferred successfully"))
                {
                    _log("✅ Sahara Handshake Completed! Switched to Firehose Mode.", SuccessColor);
                    return true;
                }

                _log($"❌ QSaharaServer failed with exit code {p.ExitCode}.", ErrorColor);
            }
            catch (OperationCanceledException)
            {
                _log("❌ Sahara Upload Timeout (90s): ဖုန်းဘက်မှ တုံ့ပြန်မှု မရှိပါ။ ဖုန်းကို Battery ဖြုတ်/တပ်ပြီး Test Point ပြန်ထောက်ပေးပါ။", ErrorColor);
            }
            catch (Exception ex)
            {
                _log($"❌ Sahara Error: {ex.Message}", ErrorColor);
            }
            finally
            {
                if (!p.HasExited) { try { p.Kill(entireProcessTree: true); } catch (Exception) { /* Process already exited or access denied, safe to ignore */ } }
            }

            return false;
        }
        public async Task<string> RunNativeFhLoader(string portName, string fhArgs)
        {
            string fhLoaderExe = Path.Combine(AppConfig.QualcommCoreDir, "fh_loader.exe");
            if (!IOFile.Exists(fhLoaderExe)) return null;

            string args = $"--port=\\\\.\\{portName} --noprompt {fhArgs}";
            return await RunProcessCommand(fhLoaderExe, args, "Executing Firehose Command...", true);
        }
        public async Task<string> RunQfilFlash(string arguments, List<string> seqFiles, string statusText)
        {
            if (string.IsNullOrWhiteSpace(_pythonPath())) return null;

            using CancellationTokenSource operationCts = new CancellationTokenSource();
            using Process process = new Process();
            Cts = operationCts;
            CurrentProcess = process;
            _setOperationState(true);
            if (!string.IsNullOrEmpty(statusText)) _setStatus(statusText);
            _updateProgress(5, "Connecting...");

            process.StartInfo = new ProcessStartInfo
            {
                FileName = _pythonPath(),
                Arguments = $"-u {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppConfig.BaseDir
            };
            process.StartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            var fullOutput = new System.Text.StringBuilder();
            int flashCounter = 0;
            int totalFiles = seqFiles != null && seqFiles.Count > 0 ? seqFiles.Count : 0;
            string lastSpeed = "";

            void HandleLine(string rawLine)
            {
                if (string.IsNullOrEmpty(rawLine)) return;
                string line = Regex.Replace(rawLine, @"\x1B\[[^@-~]*[=@-~]", "").Trim();
                if (string.IsNullOrEmpty(line)) return;
                fullOutput.AppendLine(line);

                // python ရဲ့ raw progress bar တွေ (carriage-return) ကို ချန်ပြီး progress bar ကိုပဲ update လုပ်တယ်
                if (line.StartsWith("Progress:") || line.StartsWith("Done |") || line.StartsWith("|") || line.StartsWith("\r") || line.StartsWith("Wrote "))
                {
                    // ⚡ speed (MB/s) ကို ဖမ်းပြီး label မှာ ပြတယ်
                    Match spd = Regex.Match(line, @"([\d.]+)\s*(MB|KB|GB)/s");
                    if (spd.Success) lastSpeed = spd.Groups[1].Value + " " + spd.Groups[2].Value + "/s";

                    Match pct = Regex.Match(line, @"(\d{1,3}(?:\.\d+)?)%");
                    if (pct.Success && double.TryParse(pct.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pv))
                    {
                        int basePct = totalFiles > 0 ? (flashCounter * 100) / totalFiles : 0;
                        _updateProgress(Math.Min(99, basePct + (int)(pv / (totalFiles > 0 ? totalFiles : 1))), lastSpeed);
                    }
                    return;
                }

                _runOnUi(() =>
                {
                    if (line.StartsWith("[PMK-FLASH]"))
                    {
                        string file = line.Substring("[PMK-FLASH]".Length).Trim();
                        flashCounter++;
                        int idx = flashCounter;
                        string cnt = totalFiles > 0 ? $"[{idx}/{totalFiles}] " : "";
                        _log($"\n🔥 {cnt}Flashing: {file} ...", Color.FromArgb(255, 179, 71));
                        _setStatus($"Flashing ({idx}{(totalFiles > 0 ? "/" + totalFiles : "")}): {file}");
                        _updateProgress(totalFiles > 0 ? (idx * 100) / totalFiles : 10, file);
                    }
                    else if (line.StartsWith("[PMK-DONE]"))
                    {
                        string file = line.Substring("[PMK-DONE]".Length).Trim();
                        string spdTxt = lastSpeed.Length > 0 ? $" — ⚡ {lastSpeed}" : "";
                        _log($"✅ {file} — written successfully{spdTxt}", SuccessColor);
                    }
                    else if (line.Contains("[qfil] raw programming ok.") || line.Contains("[qfil] patching ok"))
                    {
                        _log(line.Replace("[qfil]", "📦"), SuccessColor);
                    }
                    else if (line.Contains("Only nop and sig") || line.Contains("Auth detected"))
                    {
                        _log($"🔑 {line}", WarningColor);
                    }
                    else if (line.Contains("authenticated", StringComparison.OrdinalIgnoreCase))
                    {
                        _log($"🔓 {line}", SuccessColor);
                    }
                    else if (line.Contains("Mode detected"))
                    {
                        _log($"🔌 {line}", Color.FromArgb(128, 216, 255));
                    }
                    else if (line.Contains("Traceback") || line.Contains("Error:") || line.Contains("error:") || line.Contains("failed", StringComparison.OrdinalIgnoreCase) || line.Contains("ERROR"))
                    {
                        _log($"❌ {line}", ErrorColor);
                    }
                    else if (line.Contains("Uploading loader") || line.Contains("Waiting for the device") || line.Contains("Device detected"))
                    {
                        _log($"⏳ {line}", InfoColor);
                    }
                    else if (line.StartsWith("main") || line.StartsWith("firehose") || line.StartsWith("sahara") || line.StartsWith("DeviceClass"))
                    {
                        // logger prefix line တွေကို ချန်လိုက်တယ် (အဓိပ္ပါယ်မရှိလို့)
                    }
                    else if (!string.IsNullOrEmpty(line))
                    {
                        _log(line, InfoColor);
                    }
                });
            }

            process.OutputDataReceived += (s, e) => HandleLine(e.Data);
            process.ErrorDataReceived += (s, e) => HandleLine(e.Data);

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(operationCts.Token);
                return fullOutput.ToString().Trim();
            }
            catch (OperationCanceledException)
            {
                _log("🛑 Flash operation cancelled by user.", ErrorColor);
                return null;
            }
            catch (Exception ex)
            {
                _log($"❌ {ex.Message}", ErrorColor);
                return null;
            }
            finally
            {
                LastPythonExitCode = process.HasExited ? process.ExitCode : -1;
                if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (Exception) { /* Process already exited or access denied, safe to ignore */ } }
                CurrentProcess = null;
                Cts = null;
                _setOperationState(false);
                _setStatus("Ready");
            }
        }
        public string RunPythonOneShot(string args, int timeoutSeconds)
        {
            try
            {
                using CancellationTokenSource cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using Process p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath(),
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppConfig.BaseDir
                };
                p.Start();
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit(3000);
                return (outp + "\n" + err).Trim();
            }
            catch (Exception ex) { _log($"⚠️ RunPythonOneShot warning: {ex.Message}", WarningColor); return null; }
        }
        public bool PythonOpSucceeded(string outp)
        {
            if (outp == null) return false;
            if (outp.Contains("Traceback")) return false;
            if (LastPythonExitCode == 0) return true;
            if (Regex.IsMatch(outp, @"(?i)(couldn|failed|not found|doesn'?t exist|no gpt partition|usage:)")) return false;
            return true;
        }
        public void KillCurrentPython()
        {
            if (CurrentProcess != null && !CurrentProcess.HasExited)
            {
                try { CurrentProcess.Kill(entireProcessTree: true); } catch (Exception) { /* Process already exited or access denied, safe to ignore */ }
            }
        }
        public async Task<string> RunMtkSleekCommand(string fullArgs, string statusText)
        {
            using CancellationTokenSource operationCts = new CancellationTokenSource();
            using Process process = new Process();
            Cts = operationCts;
            CurrentProcess = process;
            _setOperationState(true);
            _setStatus(statusText);
            _updateProgress(5, "Connecting...");

            process.StartInfo = new ProcessStartInfo
            {
                FileName = _pythonPath(),
                Arguments = $"-u {fullArgs}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppConfig.BaseDir
            };

            process.StartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            var fullOutput = new System.Text.StringBuilder();
            string cpu = "";
            string hwCode = "";
            string meid = "";
            string emmcId = "";
            string emmcSize = "";

            void HandleIncomingLine(string rawLine)
            {
                if (string.IsNullOrEmpty(rawLine)) return;

                string cleanLine = Regex.Replace(rawLine, @"\x1B\[[^@-~]*[=@-~]", "").Trim();
                if (string.IsNullOrEmpty(cleanLine)) return;

                fullOutput.AppendLine(cleanLine);

                _runOnUi(() =>
                {
                    Match pctMatch = Regex.Match(cleanLine, @"(\d{1,3}(?:\.\d+)?)%");
                    Match speedMatch = Regex.Match(cleanLine, @"(\d+(?:\.\d+)?\s*(?:MB|KB)/s)");
                    if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, out double pVal))
                    {
                        string spd = speedMatch.Success ? speedMatch.Groups[1].Value : "";
                        _updateProgress((int)pVal, spd);
                    }

                    if (cleanLine.Contains("Port - Device detected", StringComparison.OrdinalIgnoreCase))
                    {
                        _log("⚡ Device Detected on BROM Port!", SuccessColor);
                        _updateProgress(15, "Connected");
                    }
                    else if (cleanLine.Contains("CPU:", StringComparison.OrdinalIgnoreCase)) cpu = cleanLine.Substring(cleanLine.IndexOf("CPU:") + 4).Trim();
                    else if (cleanLine.Contains("HW code:", StringComparison.OrdinalIgnoreCase)) hwCode = cleanLine.Substring(cleanLine.IndexOf("HW code:") + 8).Trim();
                    else if (cleanLine.Contains("ME_ID:", StringComparison.OrdinalIgnoreCase)) meid = cleanLine.Substring(cleanLine.IndexOf("ME_ID:") + 6).Trim();
                    else if (cleanLine.Contains("EMMC ID:", StringComparison.OrdinalIgnoreCase)) emmcId = cleanLine.Substring(cleanLine.IndexOf("EMMC ID:") + 8).Trim();
                    else if (cleanLine.Contains("EMMC USER Size:", StringComparison.OrdinalIgnoreCase))
                    {
                        string raw = cleanLine.Substring(cleanLine.IndexOf("EMMC USER Size:") + 15).Trim();
                        try
                        {
                            ulong bytes = Convert.ToUInt64(raw.Replace("0x", ""), 16);
                            emmcSize = $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                        }
                        catch (Exception ex) { _log($"⚠️ eMMC size parse fallback: {ex.Message}", WarningColor); emmcSize = raw; }
                    }
                    else if (cleanLine.Contains("Bypassing security", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("Done sending payload", StringComparison.OrdinalIgnoreCase))
                    {
                        _log("🔓 Bypassing SLA/DAA Security (Kamakiri Exploit)...", SuccessColor);
                        _updateProgress(30, "Exploit OK");
                    }
                    else if (cleanLine.Contains("Uploading xflash stage 1", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("Successfully uploaded stage 2", StringComparison.OrdinalIgnoreCase))
                    {
                        _log("🚀 Uploading Download Agent (DA V5)...", InfoColor);
                        _updateProgress(45, "Uploading DA");
                    }
                    else if (cleanLine.Contains("DRAM setup passed", StringComparison.OrdinalIgnoreCase))
                    {
                        _log($"💾 Memory Initialized: {emmcId} ({emmcSize})", SuccessColor);
                    }
                    else if (cleanLine.Contains("GPT Table:", StringComparison.OrdinalIgnoreCase))
                    {
                        _log("📋 Reading Partition Table (GPT)...", InfoColor);
                        _updateProgress(60, "Reading GPT");
                    }
                    else if (cleanLine.StartsWith("Wrote", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("Wrote ", StringComparison.OrdinalIgnoreCase))
                    {
                        Match fileMatch = Regex.Match(cleanLine, @"Wrote\s+(?:.*[\\/])?([a-zA-Z0-9_\-\.]+)\.img", RegexOptions.IgnoreCase);
                        if (fileMatch.Success)
                        {
                            string pName = fileMatch.Groups[1].Value;
                            string fileName = $"{pName}.img";
                            _log($"  ⚡ [WRITE OK] ➔ Partition: [{pName.PadRight(12)}] ➔ File: {fileName.PadRight(16)}  ✅", Color.FromArgb(0, 230, 118));
                        }
                        else
                        {
                            _log($"  • {cleanLine}", Color.FromArgb(255, 215, 0));
                        }
                    }
                    else if (cleanLine.Contains("Writing partition", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("DaHandler - Writing", StringComparison.OrdinalIgnoreCase))
                    {
                        Match wMatch = Regex.Match(cleanLine, @"partition\s+([a-zA-Z0-9_\-\.]+)", RegexOptions.IgnoreCase);
                        string pName = wMatch.Success ? wMatch.Groups[1].Value : "Partition";
                        _log($"\n🔥 [FLASHING] ➔ Writing [{pName}] ...", Color.FromArgb(255, 215, 0));
                    }
                    else if (cleanLine.Contains("Dumping partition", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("DaHandler - Dumping", StringComparison.OrdinalIgnoreCase))
                    {
                        Match dMatch = Regex.Match(cleanLine, @"partition\s+([a-zA-Z0-9_\-\.]+)", RegexOptions.IgnoreCase);
                        string pName = dMatch.Success ? dMatch.Groups[1].Value : "Partition";
                        _log($"\n💾 [READING] ➔ Dumping [{pName}] ...", Color.FromArgb(0, 229, 255));
                    }
                    else if (cleanLine.Contains("error:", StringComparison.OrdinalIgnoreCase) || cleanLine.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        _log($"❌ {cleanLine}", ErrorColor);
                    }
                });
            }

            process.OutputDataReceived += (s, e) => HandleIncomingLine(e.Data);
            process.ErrorDataReceived += (s, e) => HandleIncomingLine(e.Data);

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(operationCts.Token);

                string output = fullOutput.ToString().Trim();

                if (!string.IsNullOrEmpty(cpu) || !string.IsNullOrEmpty(hwCode))
                {
                    _log("\n------------------------------------------------------------", MtkColor);
                    _log("              📱 MEDIATEK DEVICE INFORMATION                ", MtkColor);
                    _log("------------------------------------------------------------", MtkColor);
                    _log($"  • CPU / Platform   : {cpu} [HW: {hwCode}]", SuccessColor);
                    _log($"  • Connection Mode  : BROM Mode (Security Bypassed)", InfoColor);
                    _log($"  • Security Status  : SBC: True | SLA: Bypassed | DAA: Bypassed", FastbootColor);
                    if (!string.IsNullOrEmpty(meid)) _log($"  • MEID             : {meid}", InfoColor);
                    if (!string.IsNullOrEmpty(emmcId)) _log($"  • Memory / Storage : eMMC ({emmcId}) [Capacity: {emmcSize}]", SuccessColor);
                    _log("------------------------------------------------------------", MtkColor);
                }

                _updateProgress(100, "Completed");
                return output;
            }
            catch (Exception ex)
            {
                _log($"❌ {ex.Message}", ErrorColor);
                return null;
            }
            finally
            {
                if (!process.HasExited) try { process.Kill(entireProcessTree: true); } catch (Exception) { /* Process already exited or access denied, safe to ignore */ }
                CurrentProcess = null;
                Cts = null;
                _setOperationState(false);
                _setStatus("Ready");
            }
        }
        public async Task<string> RunProcessCommand(string executablePath, string arguments, string statusText, bool logLive = true)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return null;

            using CancellationTokenSource operationCts = new CancellationTokenSource();
            using Process process = new Process();
            Cts = operationCts;
            CurrentProcess = process;
            _setOperationState(true);
            if (!string.IsNullOrEmpty(statusText)) _setStatus(statusText);
            _updateProgress(15, "Running...");

            process.StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppConfig.BaseDir
            };

            var fullOutput = new System.Text.StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    string cleanLine = Regex.Replace(e.Data, @"\x1B\[[^@-~]*[=@-~]", "").Trim();
                    if (string.IsNullOrEmpty(cleanLine)) return;

                    fullOutput.AppendLine(cleanLine);

                    Match pctMatch = Regex.Match(cleanLine, @"(\d{1,3}(?:\.\d+)?)%");
                    Match speedMatch = Regex.Match(cleanLine, @"([\d.]+)\s*(MB|KB|GB)/s");
                    if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, out double pVal))
                    {
                        string spd = speedMatch.Success ? speedMatch.Groups[1].Value + " " + speedMatch.Groups[2].Value + "/s" : "";
                        _updateProgress((int)pVal, spd);
                        return;
                    }

                    if (logLive)
                    {
                        _runOnUi(() => LogEdlLineSmart(cleanLine));
                    }
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    string cleanLine = Regex.Replace(e.Data, @"\x1B\[[^@-~]*[=@-~]", "").Trim();
                    if (string.IsNullOrEmpty(cleanLine)) return;

                    fullOutput.AppendLine(cleanLine);
                    if (logLive)
                    {
                        _runOnUi(() => LogEdlLineSmart(cleanLine));
                    }
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(operationCts.Token);
                _updateProgress(100, "Done");
                return fullOutput.ToString().Trim();
            }
            catch (Exception ex)
            {
                if (logLive) _log($"❌ {ex.Message}", ErrorColor);
                return null;
            }
            finally
            {
                if (!process.HasExited) try { process.Kill(entireProcessTree: true); } catch (Exception) { /* Process already exited or access denied, safe to ignore */ }
                CurrentProcess = null;
                Cts = null;
                _setOperationState(false);
                _setStatus("Ready");
            }
        }
        public async Task<string> GetActiveAdbSerial()
        {
            string output = await RunProcessCommand(_adbPath(), "devices", "", false);
            if (string.IsNullOrWhiteSpace(output)) return null;

            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("List of devices") || t.StartsWith("*") || string.IsNullOrWhiteSpace(t)) continue;
                if (t.Contains("device"))
                {
                    string[] parts = t.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && parts[0].Length >= 4) return parts[0];
                }
            }
            return null;
        }
        public async Task<string> RunAdbTargeted(string subArgs, string statusText = "", bool logLive = true)
        {
            string serial = await GetActiveAdbSerial();
            string targetArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";
            return await RunProcessCommand(_adbPath(), $"{targetArg}{subArgs}", statusText, logLive);
        }
        public void LogEdlLineSmart(string rawLine)
        {
            string line = (rawLine ?? "").Trim();
            if (line.Length == 0) return;

            // --- noise / banner / debug စာကြောင်းတွေကို ဖျောက်တယ် ---
            if (line.StartsWith("Qualcomm Sahara / Firehose Client")) return;
            if (line.StartsWith("Binary build date") || line.StartsWith("QSAHARASERVER CALLED LIKE THIS") ||
                line.StartsWith("Current working dir") || line.StartsWith("Sahara mappings:") ||
                line.StartsWith("Supported functions:") || line.StartsWith("Protocol version:") ||
                line.StartsWith("Trying to connect to firehose")) return;
            if (Regex.IsMatch(line, @"^\d+: [a-zA-Z0-9_]+\.mbn")) return;   // QSaharaServer mapping rows
            if (Regex.IsMatch(line, @"^[\-=_]{5,}")) return;                 // separator lines
            if (Regex.IsMatch(line, @"^[a-zA-Z0-9_]+:\s+Offset 0x")) return; // GPT row — grid ထဲမှာပဲ ပြမယ်
            if (line.StartsWith("Parsing Lun") || line.StartsWith("GPT Table:") || line.StartsWith("Version 0x")) return;
            // HWID / PK_HASH — auto-loader learning (ဖုန်းတစ်လုံးစီရဲ့ ID) အတွက် ဖမ်းပြီး log မှာ မပြဘူး
            if (line.Contains("HWID:"))
            {
                Match hm = Regex.Match(line, @"HWID:\s+(0x[0-9A-Fa-f]+)");
                if (hm.Success) DetectedHwid = hm.Groups[1].Value;
                return;
            }
            if (line.Contains("PK_HASH:"))
            {
                Match pm = Regex.Match(line, @"PK_HASH:\s+(0x[0-9A-Fa-f]+)");
                if (pm.Success) DetectedPkhash = pm.Groups[1].Value;
                return;
            }
            if (line.StartsWith("Serial:")) return;
            if (line.StartsWith("boottodwnload") || line.StartsWith("Version 0x")) return;

            // device စောင့်နေတုန်း python ရဲ့ dots/hints တွေ — app ဘက်က ကိုယ်ပိုင် message ရှိပြီးသား
            if (Regex.IsMatch(line, @"^\.+$")) return;
            if (line.StartsWith("Hint:") || line.StartsWith("Xiaomi:") || line.StartsWith("Other:") ||
                line.StartsWith("Run ") || line.Contains("fastpwn")) return;

            // logger prefix (main - / sahara - / firehose_client - / [LIB]: ...) ဖြုတ်တယ် (အရှည်ဆုံးကစ စီစဉ်)
            string body = Regex.Replace(line, @"^(firehose_client|DeviceClass|main|sahara|firehose)(\s*-\s*)?", "");
            body = Regex.Replace(body, @"^\[LIB\]:\s*", "").Trim();
            if (body.Length == 0) return;

            // prefix ဖြုတ်ပြီးမှ ပေါ်လာတဲ့ noise တွေကိုပါ ဖျောက်တယ်
            if (body.StartsWith("Protocol version:") || body.StartsWith("Trying to connect to firehose") ||
                body.StartsWith("Supported functions:") || body.StartsWith("Version 0x") ||
                body.StartsWith("[LIB]") || body.StartsWith("32-Bit mode detected") || body.StartsWith("64-Bit mode detected") ||
                Regex.IsMatch(body, @"^\.+$") || body.StartsWith("Hint:") || body.StartsWith("Xiaomi:") ||
                body.StartsWith("Other:") || body.Contains("fastpwn"))
                return;

            string display = "";
            Color col = InfoColor;

            if (body.StartsWith("Using loader", StringComparison.OrdinalIgnoreCase))
            {
                Match lm = Regex.Match(body, @"([^\\/]+\.(?:elf|mbn|bin))", RegexOptions.IgnoreCase);
                display = lm.Success ? $"🚀 Loader: {lm.Groups[1].Value}" : body;
                col = InfoColor;
            }
            else if (body.Contains("Trying with no loader given", StringComparison.OrdinalIgnoreCase))
            {
                display = "🔍 Loader auto-detect mode (no loader file selected)";
                col = WarningColor;
            }
            else if (body.Contains("Only nop and sig tag"))
            {
                display = "🔑 Xiaomi EDL auth required — sending signature...";
                col = WarningColor;
            }
            else if (body.Contains("Xiaomi EDL Auth detected"))
            {
                display = "🔑 Xiaomi EDL Auth detected — authenticating...";
                col = WarningColor;
            }
            else if (body.Contains("Authenticated successfully", StringComparison.OrdinalIgnoreCase))
            {
                display = "🔓 EDL Authenticated successfully";
                col = SuccessColor;
            }
            else if (body.Contains("Loader successfully uploaded", StringComparison.OrdinalIgnoreCase))
            {
                display = "✅ Firehose Loader uploaded — switching to Firehose";
                col = SuccessColor;
                _onLoaderUploaded(); // hwid+pkhash → loader ကို မှတ်ထား (နောက် Auto Detect အတွက်) — Form1 က LoaderService ခေါ်ပေးတယ်
            }
            else if (body.Contains("Mode detected: sahara"))
            {
                display = "🔌 Mode: Sahara (EDL)";
                col = Color.FromArgb(128, 216, 255);
            }
            else if (body.Contains("Mode detected: firehose"))
            {
                display = "🔌 Mode: Firehose (loader running)";
                col = Color.FromArgb(128, 216, 255);
            }
            else if (body.Contains("Device detected", StringComparison.OrdinalIgnoreCase))
            {
                display = "✅ Device connected";
                col = SuccessColor;
            }
            else if (body.Contains("Waiting for the device", StringComparison.OrdinalIgnoreCase))
            {
                display = "⏳ Waiting for device...";
                col = InfoColor;
            }
            else if (body.Contains("CPU detected", StringComparison.OrdinalIgnoreCase))
            {
                Match m = Regex.Match(body, @"""([^""]+)""");
                if (m.Success)
                {
                    DetectedChipset = m.Groups[1].Value;
                    display = $"📱 Phone detected: {DetectedChipset}";
                    col = Color.FromArgb(255, 179, 71);
                }
            }
            else if (body.StartsWith("Total disk size", StringComparison.OrdinalIgnoreCase))
            {
                Match m = Regex.Match(body, @"0x([0-9A-Fa-f]+)");
                if (m.Success)
                {
                    try
                    {
                        ulong bytes = Convert.ToUInt64(m.Groups[1].Value, 16);
                        display = $"💾 Total disk: {(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
                        col = SuccessColor;
                    }
                    catch (Exception ex) { _log($"⚠️ LogEdlLineSmart size parse fallback: {ex.Message}", WarningColor); return; }
                }
                else return;
            }
            else if (body.Contains("Uploading loader", StringComparison.OrdinalIgnoreCase))
            {
                display = "🚀 Uploading Firehose Loader...";
                col = InfoColor;
            }
            else if (body.Contains("32-Bit mode detected") || body.Contains("64-Bit mode detected"))
            {
                return; // အသေးစိတ် မလို
            }
            else if (body.Contains("Couldn't find a loader", StringComparison.OrdinalIgnoreCase))
            {
                DetectedLoaderMissing = true;
                display = "⚠️ Loader for this phone not in auto-database yet";
                col = WarningColor;
            }
            else if (body.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                     body.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
                     body.Contains("Traceback") ||
                     body.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
                     body.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                // QSaharaServer COM port error spam → တစ်ကြောင်းတည်း ရှင်းပြ
                if (body.Contains("Failed to open com port") || body.Contains("Could not connect"))
                {
                    display = "❌ Cannot open COM port — device not in EDL or port busy (try USB mode / Zadig)";
                }
                else
                {
                    display = "❌ " + (body.Length > 160 ? body.Substring(0, 160) : body);
                }
                col = ErrorColor;
            }
            else if (body.StartsWith("[LIB]") || body.StartsWith("Warning") || body == "main" || body == "sahara" || body == "firehose")
            {
                return;
            }
            else
            {
                display = body; // အခြား output တွေ (adb getprop စသည်) ကို မပြောင်းဘဲ ပြတယ်
                col = InfoColor;
            }

            // ထပ်ခါတလဲလဲ တူညီတဲ့ စာကြောင်းတွေကို ချုံ့တယ် (QSaharaServer ERROR spam လိုမျိုး)
            if (display == _lastSmartLine)
            {
                _lastSmartRepeat++;
                if (_lastSmartRepeat > 3) return;
            }
            else
            {
                _lastSmartLine = display;
                _lastSmartRepeat = 0;
            }

            _log(display, col);
        }
    }
}
