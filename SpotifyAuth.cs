using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChillWithYou_SpotifyMod
{
    // จัดการ OAuth 2.0 Authorization Code + PKCE (ไม่ต้องใช้ client secret เลย ปลอดภัยสำหรับ mod ฝั่ง client)
    internal static class SpotifyAuth
    {
        // ⚠️ ใส่ Client ID ของท่านเองจาก https://developer.spotify.com/dashboard
        private const string ClientId = "ENTER_YOUR_CLIENT_ID";

        // ⚠️ ต้องไปเพิ่ม URI นี้ใน "Redirect URIs" ของ Spotify App settings ให้ตรงเป๊ะ
        private const string RedirectUri = "http://127.0.0.1:8901/callback/";

        // playlist-read-private/collaborative จำเป็นสำหรับอ่าน track list ของ playlist ส่วนตัว
        // (Daily Mix / Discover Weekly ยังไงก็อ่านไม่ได้ - Spotify ตัด API access ไปตั้งแต่ปลายปี 2024)
        private const string Scopes =
            "user-read-playback-state user-modify-playback-state user-read-currently-playing " +
            "playlist-read-private playlist-read-collaborative";

        private static readonly HttpClient Http = new HttpClient();

        public static string AccessToken { get; private set; }
        public static DateTime TokenExpiresAt { get; private set; } = DateTime.MinValue;

        // true ตั้งแต่ครั้งแรกที่ login/refresh สำเร็จ จนกว่า refresh token จะถูกลบ/ปฏิเสธ
        public static bool IsLoggedIn { get; private set; }

        // ----- เรียกจาก SpotifyButtonsInjector ตอนกดปุ่ม "Connect Spotify" -----
        // fire-and-forget: เปิด browser ให้ login แล้วยิง callback กลับเมื่อจบ
        public static void StartLogin(Action onSuccess, Action<string> onFailed)
        {
            _ = StartLoginInternal(onSuccess, onFailed);
        }

        private static async Task StartLoginInternal(Action onSuccess, Action<string> onFailed)
        {
            try
            {
                bool ok = await LoginWithBrowserAsync();
                if (ok)
                {
                    onSuccess?.Invoke();
                }
                else
                {
                    onFailed?.Invoke("เปิด browser หรือรับ callback ไม่สำเร็จ");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log($"[SpotifyAuth] StartLogin exception: {ex.Message}");
                onFailed?.Invoke(ex.Message);
            }
        }

        // กัน refresh ซ้อนกัน: เดิม SpotifyApi/SpotifyWebApi/SpotifySearchApi ต่างมี EnsureValidTokenAsync
        // ของตัวเอง พอ token หมดอายุตอนที่หลายตัวกำลังยิง API อยู่ ทุกตัวจะเห็นว่าหมดอายุพร้อมกัน
        // แล้วสั่ง refresh พร้อมกัน - ตัวที่มาทีหลังใช้ refresh token ที่ถูกใช้ไปแล้ว จึงโดนปฏิเสธ
        // และ (ร้ายกว่านั้น) วิ่งเข้า path ที่ลบ refresh token ที่เก็บไว้ทิ้ง
        private static readonly SemaphoreSlim RefreshLock = new SemaphoreSlim(1, 1);

        // จุดเดียวที่ทุก API call ควรเรียกก่อนยิง เพื่อให้แน่ใจว่ามี access token ที่ใช้ได้
        // คืน false = ต้องให้ user กด Connect login ใหม่
        public static async Task<bool> EnsureValidTokenAsync()
        {
            if (HasUsableToken())
                return true;

            await RefreshLock.WaitAsync();
            try
            {
                // ระหว่างที่รอคิว อีกเธรดอาจ refresh เสร็จไปแล้ว - เช็คซ้ำก่อนยิงจริง
                if (HasUsableToken())
                    return true;

                await RefreshAccessToken();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                RefreshLock.Release();
            }
        }

        // เช็ค AccessToken ด้วย ไม่ใช่แค่เวลาหมดอายุ - กันเคสที่เวลายังไม่หมดแต่ token ว่าง
        // ซึ่งจะทำให้ยิง header เป็น "Bearer " เปล่าๆ แล้วโดน 401 "Access token missing"
        private static bool HasUsableToken() =>
            !string.IsNullOrEmpty(AccessToken) && DateTime.UtcNow < TokenExpiresAt;

        // ให้ผู้เรียกแยกได้ว่า 401 ที่เจอเป็นเพราะ token เสีย หรือเป็นอาการชั่วคราวฝั่ง Spotify
        public static bool HasUsableTokenPublic => HasUsableToken();

        // ----- เรียกจาก EnsureValidTokenAsync() เมื่อ token หมดอายุ -----
        // ใช้ refresh token ที่เก็บไว้ ขอ access token ใหม่แบบเงียบๆ ไม่เปิด browser
        // ถ้าไม่มี/ไม่ผ่าน จะ throw เพื่อให้ผู้เรียกรู้ว่าต้องพา user ไป login ใหม่ (ผ่านปุ่ม Connect)
        public static async Task RefreshAccessToken()
        {
            string storedRefreshToken = SpotifyTokenStore.LoadRefreshToken();
            if (string.IsNullOrEmpty(storedRefreshToken))
            {
                IsLoggedIn = false;
                throw new InvalidOperationException("ไม่มี refresh token ที่เก็บไว้ ต้อง login ใหม่");
            }

            bool refreshed = await RefreshAccessTokenInternal(storedRefreshToken);
            if (!refreshed)
            {
                IsLoggedIn = false;
                SpotifyTokenStore.TryDelete(); // token เก่าใช้ไม่ได้แล้ว เคลียร์ทิ้งกันค้าง
                throw new InvalidOperationException("Refresh token ถูกปฏิเสธ (อาจถูก revoke) ต้อง login ใหม่");
            }
        }

        // ----- เรียกครั้งเดียวตอน plugin เริ่มทำงาน (เช่นใน Plugin.Awake) -----
        // ลอง resume session จาก refresh token ที่เก็บไว้แบบเงียบๆ โดยไม่ throw ออกไปให้ caller ต้อง try/catch
        public static async Task<bool> TryResumeSessionAsync()
        {
            try
            {
                await RefreshAccessToken();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ----- ขั้นแรก: เปิด browser ให้ user login (ทำครั้งเดียว จนกว่า refresh token จะถูก revoke) -----
        private static async Task<bool> LoginWithBrowserAsync()
        {
            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = GenerateCodeChallenge(codeVerifier);
            string state = Guid.NewGuid().ToString("N");

            var authUrl = "https://accounts.spotify.com/authorize" +
                          $"?client_id={ClientId}" +
                          "&response_type=code" +
                          $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                          $"&code_challenge_method=S256" +
                          $"&code_challenge={codeChallenge}" +
                          $"&scope={Uri.EscapeDataString(Scopes)}" +
                          $"&state={state}";

            using var listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri);
            listener.Start();

            Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });
            UnityEngine.Debug.Log("[SpotifyAuth] เปิด browser ให้ login Spotify แล้ว รอ callback...");

            HttpListenerContext ctx = await listener.GetContextAsync();
            NameValueCollection query = ctx.Request.QueryString;

            string returnedState = query["state"];
            string code = query["code"];
            string error = query["error"];

            string responseHtml = error == null
                ? "<html><body><h2>Login successful, you may return to the game now.</h2></body></html>"
                : "<html><body><h2>Login failed, please try again within the game.</h2></body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
            ctx.Response.ContentLength64 = buffer.Length;
            await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
            listener.Stop();

            if (error != null || returnedState != state || string.IsNullOrEmpty(code))
            {
                UnityEngine.Debug.Log($"[SpotifyAuth] Login ไม่สำเร็จ: {error ?? "state mismatch หรือไม่มี code"}");
                return false;
            }

            return await ExchangeCodeForTokensAsync(code, codeVerifier);
        }

        private static async Task<bool> ExchangeCodeForTokensAsync(string code, string codeVerifier)
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("client_id", ClientId),
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "authorization_code"),
                new System.Collections.Generic.KeyValuePair<string, string>("code", code),
                new System.Collections.Generic.KeyValuePair<string, string>("redirect_uri", RedirectUri),
                new System.Collections.Generic.KeyValuePair<string, string>("code_verifier", codeVerifier),
            });

            return await SendTokenRequestAsync(form);
        }

        // ----- ใช้ refresh token ที่เก็บไว้ ขอ access token ใหม่แบบเงียบๆ -----
        private static async Task<bool> RefreshAccessTokenInternal(string refreshToken)
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("client_id", ClientId),
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "refresh_token"),
                new System.Collections.Generic.KeyValuePair<string, string>("refresh_token", refreshToken),
            });

            return await SendTokenRequestAsync(form);
        }

        private static async Task<bool> SendTokenRequestAsync(FormUrlEncodedContent form)
        {
            try
            {
                HttpResponseMessage resp = await Http.PostAsync("https://accounts.spotify.com/api/token", form);
                string body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    UnityEngine.Debug.Log($"[SpotifyAuth] Token request failed ({resp.StatusCode}): {body}");
                    return false;
                }

                JObject json = JObject.Parse(body);
                AccessToken = json.Value<string>("access_token");
                int expiresIn = json.Value<int?>("expires_in") ?? 3600;
                // เผื่อเวลาไว้ 60 วิ กันชนขอบพอดี
                TokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60);
                IsLoggedIn = true;

                // Spotify จะส่ง refresh_token ใหม่กลับมาเฉพาะบางครั้ง ถ้าไม่ส่งมาให้ใช้ตัวเดิมต่อ
                string newRefreshToken = json.Value<string>("refresh_token");
                if (!string.IsNullOrEmpty(newRefreshToken))
                    SpotifyTokenStore.SaveRefreshToken(newRefreshToken);

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log($"[SpotifyAuth] Token request exception: {ex.Message}");
                return false;
            }
        }

        // ----- PKCE helpers -----
        private static string GenerateCodeVerifier()
        {
            byte[] bytes = new byte[64];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string GenerateCodeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] input) =>
            Convert.ToBase64String(input)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
    }
}