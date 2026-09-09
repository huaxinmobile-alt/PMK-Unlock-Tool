#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IOFile = System.IO.File;

namespace WinFormsApp1
{
    // Gmail (Google OAuth) တစ်ခါ register စနစ် —
    // browser မှာ Google ဝင်တာ (PKCE + loopback), password ကို ဒီ tool က ဘယ်တော့မှ မမြင်ရဘူး
    public static class GoogleAuth
    {
        // Placeholder — အစစ်ကို google_client.json (app folder ထဲ) ဒါမှမဟုတ် ဒီ constant မှာ ထည့်ရတယ်
        public const string DefaultClientId = "REPLACE_ME.apps.googleusercontent.com";
        public const string AllowlistUrl =
            "https://raw.githubusercontent.com/huaxinmobile-alt/PMK-Unlock-Tool/main/update/allowed_emails.json";
        private const string EmailSalt = "pmk-allow-v1:"; // tools/add_buyer.py နဲ့ တူရမယ်
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private static string ConfigPath => Path.Combine(AppConfig.BaseDir, "google_client.json");

        // Client ID — google_client.json ထဲက value က code constant ထက် ဦးစား
        public static string ClientId
        {
            get
            {
                try
                {
                    if (IOFile.Exists(ConfigPath))
                    {
                        string json = IOFile.ReadAllText(ConfigPath);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("client_id", out var el))
                        {
                            string v = el.GetString();
                            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                        }
                    }
                }
                catch { /* json ပျက်နေရင် constant သုံးမယ် */ }
                return DefaultClientId;
            }
        }

        public static bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !ClientId.StartsWith("REPLACE_ME");

        // email ကို public allowlist ထဲ ဝှက်ထားတဲ့ hash
        public static string HashEmail(string email) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(EmailSalt + (email ?? "").Trim().ToLowerInvariant())))
                .ToLowerInvariant();

        // ================= Allowlist (ခွင့်ပြုစာရင်း) =================
        public static async Task<bool> IsEmailAllowedAsync(string email, string manifestUrlOverride = null)
        {
            try
            {
                string url = manifestUrlOverride ?? AllowlistUrl;
                using var resp = await Http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return false;
                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return false;
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("hashes", out var arr)) return false;
                string mine = HashEmail(email);
                return arr.EnumerateArray().Any(e => e.GetString() == mine);
            }
            catch { return false; }
        }

        // ================= OAuth flow (အပြည့်အစုံ) =================
        // အောင်ရင် verified Gmail (Email) ပြန် — မအောင်ရင် Email = null + Reason
        public static async Task<(string Email, string Reason)> AuthorizeAndGetEmailAsync()
        {
            if (!IsConfigured) return (null, "Google Client ID မထည့်ရသေးဘူး — PMK ကို ဆက်သွယ်ပါ");

            string verifier = Base64Url(RandomNumberGenerator.GetBytes(40));
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = Base64Url(RandomNumberGenerator.GetBytes(16));

            // Loopback port အလွတ် ရွေး
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
            string redirect = $"http://127.0.0.1:{port}/";

            using var server = new TcpListener(IPAddress.Loopback, port);
            server.Start();

            string url =
                "https://accounts.google.com/o/oauth2/v2/auth" +
                "?client_id=" + Uri.EscapeDataString(ClientId) +
                "&redirect_uri=" + Uri.EscapeDataString(redirect) +
                "&response_type=code" +
                "&scope=" + Uri.EscapeDataString("openid email") +
                "&code_challenge=" + challenge +
                "&code_challenge_method=S256" +
                "&state=" + state +
                "&access_type=online" +
                "&prompt=select_account";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) { return (null, "Browser မဖွင့်နိုင်ဘူး: " + ex.Message); }

            var (code, gotState, error) = await WaitForCallbackAsync(server, TimeSpan.FromSeconds(180)).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(error)) return (null, "Google မှာ ဖျက်သိမ်းလိုက်တယ် (access denied)");
            if (string.IsNullOrEmpty(code)) return (null, "Google login အချိန်ကုန်သွားတယ် — ပြန်စမ်းပါ");
            if (!string.IsNullOrEmpty(state) && state != gotState) return (null, "Security state မကိုက်ဘူး — ပြန်စမ်းပါ");

            // Code → id_token
            string idToken;
            try
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["client_id"] = ClientId,
                    ["redirect_uri"] = redirect,
                    ["grant_type"] = "authorization_code",
                    ["code_verifier"] = verifier
                });
                using var resp = await Http.PostAsync("https://oauth2.googleapis.com/token", form).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return (null, "Google token မရဘူး (" + (int)resp.StatusCode + ")");
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                idToken = doc.RootElement.TryGetProperty("id_token", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(idToken)) return (null, "Google id_token မပါဘူး");
            }
            catch (Exception ex) { return (null, "Token exchange error: " + ex.Message); }

            // id_token ကို Google tokeninfo နဲ့ စစ် (email_verified + aud)
            try
            {
                using var resp = await Http.GetAsync("https://oauth2.googleapis.com/tokeninfo?id_token=" + Uri.EscapeDataString(idToken)).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return (null, "Google verify မအောင်ဘူး (" + (int)resp.StatusCode + ")");
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                var r = doc.RootElement;
                string aud = r.TryGetProperty("aud", out var a) ? a.GetString() : "";
                string email = r.TryGetProperty("email", out var em) ? em.GetString() : "";
                string verified = r.TryGetProperty("email_verified", out var ev) ? ev.GetString() : "";
                if (aud != ClientId) return (null, "Token က ဒီ tool အတွက် မဟုတ်ဘူး");
                if (verified != "true" || string.IsNullOrWhiteSpace(email)) return (null, "Email ကို အတည်မပြုရသေးဘူး");
                return (email, "");
            }
            catch (Exception ex) { return (null, "Verify error: " + ex.Message); }
        }

        // Browser callback — အသေးစား local HTTP server (admin ခွင့်ပြုချက် မလို)
        private static async Task<(string code, string state, string error)> WaitForCallbackAsync(TcpListener server, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                using var client = await server.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
                using var ns = client.GetStream();
                byte[] buf = new byte[8192];
                int read = await ns.ReadAsync(buf, cts.Token).ConfigureAwait(false);
                string req = Encoding.ASCII.GetString(buf, 0, Math.Max(read, 0));

                string code = "", state = "", error = "";
                int q = req.IndexOf('?');
                if (q >= 0)
                {
                    int end = req.IndexOf(' ', q);
                    string query = end < 0 ? req.Substring(q + 1) : req.Substring(q + 1, end - q - 1);
                    foreach (string pair in query.Split('&'))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq < 0) continue;
                        string k = Uri.UnescapeDataString(pair.Substring(0, eq));
                        string v = Uri.UnescapeDataString(pair.Substring(eq + 1));
                        if (k == "code") code = v;
                        else if (k == "state") state = v;
                        else if (k == "error") error = v;
                    }
                }

                string body =
                    "<html><body style='background:#0e1621;color:#e8eef7;font-family:Segoe UI,Arial,sans-serif;text-align:center;padding-top:60px'>" +
                    "<h2 style='color:#81c784'>✅ Done</h2>" +
                    "<p>PMK Unlock Tool ကို ပြန်ဖွင့်ပါ — ဒီ tab ပိတ်လို့ရပါပြီ</p></body></html>";
                string respHeader = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " +
                                    Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n";
                byte[] resp = Encoding.UTF8.GetBytes(respHeader + body);
                await ns.WriteAsync(resp, cts.Token).ConfigureAwait(false);
                return (code, state, error);
            }
            catch (OperationCanceledException)
            {
                return ("", "", "");
            }
        }

        private static string Base64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
