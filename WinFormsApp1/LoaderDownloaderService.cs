#nullable disable
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // On-demand loader download — loader ဖိုင် local မှာ မရှိရင် GitHub repo ကနေ တစ်ခုချင်းစီ download လုပ်တယ်
    // (Octoplus ပုံစံ — release zip သေးဖို့ Loaders တွေကို မထည့်တော့ဘူး)
    public class LoaderDownloaderService
    {
        private readonly Action<string, Color> _log;
        private readonly Action<int, string> _updateProgress;
        private readonly HttpClient _httpClient;

        // GitHub Raw Base URL for the loaders (repo: huaxinmobile-alt/PMK-Unlock-Tool, branch: main)
        private const string GITHUB_RAW_BASE = "https://raw.githubusercontent.com/huaxinmobile-alt/PMK-Unlock-Tool/main/WinFormsApp1/";

        public LoaderDownloaderService(Action<string, Color> log, Action<int, string> updateProgress)
        {
            _log = log;
            _updateProgress = updateProgress;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(120); // 2 minutes timeout for large files
        }

        /// <summary>
        /// Absolute path (BaseDir အောက်) ကို repo-relative path ပြောင်း —
        /// "Loaders\..." အောက်မှသာ ပြန်ပေးတယ် (အပြင်ဖိုင် ဆို "" — download မလုပ်ဘူး)
        /// </summary>
        public static string TryToRelative(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath)) return "";
            string baseDir = AppConfig.BaseDir.TrimEnd('\\', '/');
            if (!absolutePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) return "";
            string rel = absolutePath.Substring(baseDir.Length).TrimStart('\\', '/');
            if (!rel.StartsWith("Loaders", StringComparison.OrdinalIgnoreCase)) return "";
            if (rel.Contains("..")) return ""; // path traversal မလား
            return rel;
        }

        /// <summary>
        /// Downloads a loader file if it doesn't exist locally.
        /// </summary>
        /// <param name="relativePath">e.g., "Loaders/Xiaomi/Redmi Note 10/prog_firehose.mbn"</param>
        /// <returns>Local path if successful, null if failed.</returns>
        public async Task<string> DownloadLoaderIfNeededAsync(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;

            string localPath = Path.Combine(AppConfig.BaseDir, relativePath);

            // If already downloaded, just return it
            if (IOFile.Exists(localPath))
            {
                return localPath;
            }

            // URL-safe ဖြစ်အောင် segment တစ်ခုချင်းစီ encode (နေရာလွတ်/() တွေ ပါလို့)
            string urlPath = string.Join("/", relativePath.Replace("\\", "/").Split('/').Select(Uri.EscapeDataString));
            string downloadUrl = GITHUB_RAW_BASE + urlPath;
            _log("☁️ Loader not found locally. Downloading from GitHub...", Color.FromArgb(100, 181, 246));
            _log($"🔗 {Path.GetFileName(localPath)}", Color.FromArgb(150, 150, 150));

            try
            {
                string dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Partial file ကျန်နေရင် အရင်ရှင်း
                string tmpPath = localPath + ".part";
                if (IOFile.Exists(tmpPath)) { try { IOFile.Delete(tmpPath); } catch { } }

                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                int percent = (int)((totalRead * 100) / totalBytes);
                                _updateProgress(percent, $"Downloading Loader... {percent}%");
                            }
                        }
                    }
                }

                IOFile.Move(tmpPath, localPath, overwrite: true);
                _updateProgress(100, "Loader Ready!");
                _log($"✅ Loader downloaded successfully: {Path.GetFileName(localPath)}", Color.FromArgb(0, 230, 118));
                return localPath;
            }
            catch (Exception ex)
            {
                _log($"❌ Download failed: {ex.Message}", Color.FromArgb(239, 83, 80));
                // Clean up partial/corrupted file
                try { if (IOFile.Exists(localPath)) IOFile.Delete(localPath); } catch { }
                try { if (IOFile.Exists(localPath + ".part")) IOFile.Delete(localPath + ".part"); } catch { }
                return null;
            }
        }
    }
}
