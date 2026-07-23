// SpotifyGateway.cs
// ศูนย์กลางการยิง request ไป Spotify Web API - ครอบ "envelope" เดียวให้ทุกโมดูล:
// rate-limit gate, ต่อ token, ใส่ Bearer, retry 401/403 ชั่วคราว, จับ 429, log error.
// SpotifyApi / SpotifyWebApi / SpotifySearchApi เหลือหน้าที่แค่ประกอบ path + parse JSON
// เดิม envelope นี้ถูก copy อยู่ในทั้งสามโมดูล (แต่ละตัวมี HttpClient + 429 handling ของตัวเอง)
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChillWithYou_SpotifyMod
{
    internal static class SpotifyGateway
    {
        private const string BaseUrl = "https://api.spotify.com/v1/";
        private static readonly HttpClient Http = new HttpClient();

        // ระยะรอก่อน retry ตอนเจอ 401/403 ทั้งที่ token ยังดี - รวมแล้วไม่เกิน ~2 วิ
        // ถ้ายังไม่ผ่านหลังจากนี้ ปล่อยให้ผู้เรียกไปเริ่มรอบใหม่เองดีกว่าค้างรอต่อ
        private static readonly int[] UnauthorizedRetryDelaysMs = { 300, 600, 1000 };

        // GET {path} แล้วคืน JObject; null เมื่อ blocked / ยังไม่ login / 429 / non-2xx / 204 / body ว่าง
        public static async Task<JObject> GetJsonAsync(string path)
        {
            HttpResponseMessage resp = await SendCoreAsync(HttpMethod.Get, path, null);
            if (resp == null) return null;
            try
            {
                if (resp.StatusCode == HttpStatusCode.NoContent) return null; // 204 = ไม่มี active device
                string json = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyGateway] GET {path} parse error: {ex.Message}");
                return null;
            }
            finally { resp.Dispose(); }
        }

        // ยิง method ใดๆ พร้อม body (ไม่บังคับ); คืน true เฉพาะตอน 2xx - สำหรับคำสั่งที่ไม่อ่าน body
        public static async Task<bool> SendAsync(HttpMethod method, string path, string jsonBody = null)
        {
            HttpResponseMessage resp = await SendCoreAsync(method, path, jsonBody);
            if (resp == null) return false;
            resp.Dispose();
            return true; // SendCoreAsync คืน non-null เฉพาะตอน 2xx เท่านั้น
        }

        // โหลด bytes จาก URL เต็ม (ปกอัลบั้มบน CDN ของ Spotify - ไม่ต้อง auth/ไม่นับ rate limit); null เมื่อพลาด
        public static async Task<byte[]> GetImageAsync(string absoluteUrl)
        {
            if (string.IsNullOrEmpty(absoluteUrl)) return null;
            try { return await Http.GetByteArrayAsync(absoluteUrl); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[SpotifyGateway] image download failed: {ex.Message}"); return null; }
        }

        // หัวใจของ envelope: คืน HttpResponseMessage 2xx ที่ผ่าน retry แล้ว หรือ null เมื่อ
        // blocked / ยังไม่ login / 429 (report แล้ว) / non-2xx (log แล้ว) / exception
        // ผู้เรียกที่ได้ resp non-null กลับไปรับผิดชอบ Dispose เอง
        private static async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, string jsonBody)
        {
            if (SpotifyRateLimiter.IsBlocked)
            {
                Plugin.Log.LogInfo($"[SpotifyGateway] ข้าม {method} {path}: ยังโดน rate limit อยู่อีก {SpotifyRateLimiter.RemainingBlock.TotalSeconds:F0} วิ");
                return null;
            }

            if (!await SpotifyAuth.EnsureValidTokenAsync())
            {
                Plugin.Log.LogWarning($"[SpotifyGateway] {method} {path}: ยังไม่ได้ login / refresh token พลาด");
                return null;
            }

            try
            {
                HttpResponseMessage resp = await SendOnceAsync(method, path, jsonBody);

                // Spotify ปฏิเสธเป็นครั้งคราวเหมือนไม่ได้ login ทั้งที่ token ใช้ได้จริง:
                // GET ได้ 401 "Access token missing", คำสั่งควบคุมได้ 403 PREMIUM_REQUIRED
                // (มอง request ที่ไม่มี token เป็นผู้ใช้ทั่วไปที่สั่งควบคุมไม่ได้) - retry ให้แทนผู้ใช้
                // เช็ค token ก่อนเสมอ ไม่งั้นเคส token ตายจริงจะโดน retry ฟรีๆ ทุกครั้ง
                for (int attempt = 1;
                     attempt <= UnauthorizedRetryDelaysMs.Length && IsTransientAuthFailure(resp);
                     attempt++)
                {
                    int delay = UnauthorizedRetryDelaysMs[attempt - 1];
                    Plugin.Log.LogInfo($"[SpotifyGateway] {method} {path} โดน {(int)resp.StatusCode} ทั้งที่ token ยังดี - " +
                        $"รอ {delay}ms แล้วลองใหม่ (ครั้งที่ {attempt}/{UnauthorizedRetryDelaysMs.Length})");
                    resp.Dispose();
                    await Task.Delay(delay);
                    resp = await SendOnceAsync(method, path, jsonBody);
                    if (resp.IsSuccessStatusCode)
                        Plugin.Log.LogInfo($"[SpotifyGateway] {method} {path} retry ครั้งที่ {attempt} ผ่านแล้ว");
                }

                if (resp.StatusCode == (HttpStatusCode)429)
                {
                    SpotifyRateLimiter.ReportTooManyRequests(resp);
                    resp.Dispose();
                    return null;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    Plugin.Log.LogWarning($"[SpotifyGateway] {method} {path} failed: {resp.StatusCode} - {body}");
                    // ถ้ายัง 401 อยู่หลัง retry แสดงว่าไม่ใช่อาการชั่วคราวแล้ว - log สภาพ token ไว้สืบต่อ
                    if (resp.StatusCode == HttpStatusCode.Unauthorized)
                        Plugin.Log.LogWarning(
                            $"[SpotifyGateway] token ตอนยิง: len={SpotifyAuth.AccessToken?.Length ?? -1}, " +
                            $"เหลืออายุ={(SpotifyAuth.TokenExpiresAt - DateTime.UtcNow).TotalSeconds:F0}s, IsLoggedIn={SpotifyAuth.IsLoggedIn}");
                    resp.Dispose();
                    return null;
                }

                return resp; // 2xx - ผู้เรียก Dispose เอง
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyGateway] {method} {path} exception: {ex}");
                return null;
            }
        }

        private static async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, string jsonBody)
        {
            var request = new HttpRequestMessage(method, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SpotifyAuth.AccessToken);
            if (jsonBody != null)
                request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            return await Http.SendAsync(request);
        }

        // 401/403 ที่เกิดทั้งที่ token ยังใช้ได้ = อาการชั่วคราว ไม่ใช่ token เสียหรือบัญชีไม่มีสิทธิ์
        private static bool IsTransientAuthFailure(HttpResponseMessage resp) =>
            (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
            && SpotifyAuth.HasUsableTokenPublic;
    }
}
