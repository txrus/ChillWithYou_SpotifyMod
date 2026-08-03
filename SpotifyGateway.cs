// SpotifyGateway.cs
// ศูนย์กลางการยิง request ไป Spotify Web API - ครอบ "envelope" เดียวให้ทุกโมดูล:
// rate-limit gate, ต่อ token, ใส่ Bearer, retry 401/403 ชั่วคราว, จับ 429, log error.
// SpotifyApi / SpotifyWebApi / SpotifySearchApi เหลือหน้าที่แค่ประกอบ path + parse JSON
// เดิม envelope นี้ถูก copy อยู่ในทั้งสามโมดูล (แต่ละตัวมี HttpClient + 429 handling ของตัวเอง)
using System;
using System.Collections.Generic;
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

        // GET {path} แล้วคืน JObject; null เมื่อ blocked / ยังไม่ login / 429 / non-2xx / 204 / body ว่าง
        public static async Task<JObject> GetJsonAsync(string path) => (await GetJsonOrNotFoundAsync(path)).Json;

        // เหมือน GetJsonAsync แต่บอกเพิ่มว่าพลาดเพราะ 404 หรือเปล่า - ผู้เรียกจะได้แยก "ไม่มีอยู่จริง /
        // เข้าถึงไม่ได้ถาวร" ออกจาก "พลาดชั่วคราว (429, เน็ตสะดุด)" แล้วเลิก retry สิ่งที่ไม่มีวันสำเร็จ
        public static Task<(JObject Json, bool NotFound)> GetJsonOrNotFoundAsync(string path) =>
            GetParsedAsync(path, JObject.Parse);

        // แกนของ GET - แยกฟังก์ชัน parse ออกมาเผื่อ endpoint ที่ตอบเป็นทรงอื่น (เคย
        // มี GetJsonArrayAsync สำหรับ /me/tracks/contains ที่ตอบเป็น array - ถอดออกไป
        // พร้อมฟีเจอร์คลังเพลงเพราะ endpoint โดน 403 ใน development mode)
        private static async Task<(T Value, bool NotFound)> GetParsedAsync<T>(string path, Func<string, T> parse) where T : class
        {
            (HttpResponseMessage resp, int status) = await SendCoreAsync(HttpMethod.Get, path, null);
            bool notFound = status == (int)HttpStatusCode.NotFound;
            if (resp == null) return (null, notFound);
            try
            {
                if (resp.StatusCode == HttpStatusCode.NoContent) return (null, false); // 204 = ไม่มี active device
                string json = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json)) return (null, false);
                return (parse(json), false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyGateway] GET {path} parse error: {ex.Message}");
                return (null, false);
            }
            finally { resp.Dispose(); }
        }

        // ยิง method ใดๆ พร้อม body (ไม่บังคับ); คืน true เฉพาะตอน 2xx - สำหรับคำสั่งที่ไม่อ่าน body
        public static async Task<bool> SendAsync(HttpMethod method, string path, string jsonBody = null)
        {
            (HttpResponseMessage resp, int _) = await SendCoreAsync(method, path, jsonBody);
            if (resp == null) return false;
            resp.Dispose();
            return true; // SendCoreAsync คืน non-null เฉพาะตอน 2xx เท่านั้น
        }

        // โหลด bytes จาก URL เต็ม (ปกอัลบั้มบน CDN ของ Spotify - ไม่ต้อง auth/ไม่นับ rate limit); null เมื่อพลาด
        public static async Task<byte[]> GetImageAsync(string absoluteUrl)
        {
            if (string.IsNullOrEmpty(absoluteUrl)) return null;
            try { return await Http.GetByteArrayAsync(absoluteUrl); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[SpotifyGateway] image download failed: {Describe(ex)}"); return null; }
        }

        // HttpRequestException ห่อสาเหตุจริงไว้ข้างในเสมอ แล้วรายงานตัวเองว่า "An error occurred while
        // sending the request" ซึ่งไม่บอกอะไรเลย - ต้องแกะ InnerException ออกมาถึงจะแยกออกว่าเป็น DNS
        // (SocketException "No such host is known"), TLS, หรือ timeout
        private static string Describe(Exception ex)
        {
            var parts = new List<string>();
            for (Exception e = ex; e != null; e = e.InnerException)
                parts.Add($"{e.GetType().Name}: {e.Message}");
            return string.Join(" <- ", parts);
        }

        // เลือก URL รูปจาก array "images" ของ Spotify - เกณฑ์การเลือกทั้งหมด (รวมเหตุผลเรื่อง
        // ขนาด texture) อยู่ที่ SpotifyGatewayPolicy.PickImageUrl ซึ่งทดสอบได้ ตรงนี้แค่แปลง JToken
        public static string PickImageUrl(JToken images, int minWidth)
        {
            if (!(images is JArray arr) || arr.Count == 0) return null;

            var pairs = new List<(string Url, int? Width)>(arr.Count);
            foreach (JToken img in arr)
                pairs.Add((img?.Value<string>("url"), img?.Value<int?>("width")));

            return SpotifyGatewayPolicy.PickImageUrl(pairs, minWidth);
        }

        // หัวใจของ envelope: คืน HttpResponseMessage 2xx ที่ผ่าน retry แล้ว หรือ null เมื่อ
        // blocked / ยังไม่ login / 429 (report แล้ว) / non-2xx (log แล้ว) / exception
        // ผู้เรียกที่ได้ resp non-null กลับไปรับผิดชอบ Dispose เอง
        // Status = HTTP status ที่ได้จริงจากรอบสุดท้าย หรือ 0 เมื่อยังไม่ทันได้ยิง (blocked / ไม่ login / exception)
        private static async Task<(HttpResponseMessage Resp, int Status)> SendCoreAsync(HttpMethod method, string path, string jsonBody)
        {
            if (SpotifyRateLimiter.IsBlocked)
            {
                Plugin.Log.LogInfo($"[SpotifyGateway] ข้าม {method} {path}: ยังโดน rate limit อยู่อีก {SpotifyRateLimiter.RemainingBlock.TotalSeconds:F0} วิ");
                return (null, 0);
            }

            if (!await SpotifyAuth.EnsureValidTokenAsync())
            {
                Plugin.Log.LogWarning($"[SpotifyGateway] {method} {path}: ยังไม่ได้ login / refresh token พลาด");
                return (null, 0);
            }

            try
            {
                HttpResponseMessage resp = await SendOnceAsync(method, path, jsonBody);

                // "response แบบนี้ + token สภาพนี้ ควรทำอะไรต่อ" เป็นกฎของ SpotifyGatewayPolicy
                // (ทดสอบได้บน bench) - ตรงนี้เหลือแค่ plumbing: ถามซ้ำหลังทุกการยิง แล้วทำตามคำตอบ
                //
                // เคส RetryTransientAuth: Spotify ปฏิเสธเป็นครั้งคราวเหมือนไม่ได้ login ทั้งที่ token
                // ใช้ได้จริง (GET ได้ 401 "Access token missing", คำสั่งควบคุมได้ 403 PREMIUM_REQUIRED)
                // - retry ให้แทนผู้ใช้ตามระยะในตาราง จนหมดโควตาแล้วปล่อยเข้าเคส Fail ตามปกติ
                for (int attempt = 1;
                     attempt <= SpotifyGatewayPolicy.TransientAuthRetryDelaysMs.Length
                         && Classify(resp) == GatewayAction.RetryTransientAuth;
                     attempt++)
                {
                    int delay = SpotifyGatewayPolicy.TransientAuthRetryDelaysMs[attempt - 1];
                    Plugin.Log.LogInfo($"[SpotifyGateway] {method} {path} โดน {(int)resp.StatusCode} ทั้งที่ token ยังดี - " +
                        $"รอ {delay}ms แล้วลองใหม่ (ครั้งที่ {attempt}/{SpotifyGatewayPolicy.TransientAuthRetryDelaysMs.Length})");
                    resp.Dispose();
                    await Task.Delay(delay);
                    resp = await SendOnceAsync(method, path, jsonBody);
                    if (resp.IsSuccessStatusCode)
                        Plugin.Log.LogInfo($"[SpotifyGateway] {method} {path} retry ครั้งที่ {attempt} ผ่านแล้ว");
                }

                int status = (int)resp.StatusCode;
                switch (Classify(resp))
                {
                    case GatewayAction.Succeed:
                        return (resp, status); // 2xx - ผู้เรียก Dispose เอง

                    case GatewayAction.RateLimited:
                        SpotifyRateLimiter.ReportTooManyRequests(resp);
                        resp.Dispose();
                        return (null, status);

                    default: // Fail และ RetryTransientAuth ที่หมดโควตา retry แล้ว
                        string body = await resp.Content.ReadAsStringAsync();
                        Plugin.Log.LogWarning($"[SpotifyGateway] {method} {path} failed: {resp.StatusCode} - {body}");
                        // ถ้ายัง 401 อยู่หลัง retry แสดงว่าไม่ใช่อาการชั่วคราวแล้ว - log สภาพ token ไว้สืบต่อ
                        if (resp.StatusCode == HttpStatusCode.Unauthorized)
                            Plugin.Log.LogWarning(
                                $"[SpotifyGateway] token ตอนยิง: len={SpotifyAuth.AccessToken?.Length ?? -1}, " +
                                $"เหลืออายุ={(SpotifyAuth.TokenExpiresAt - DateTime.UtcNow).TotalSeconds:F0}s, IsLoggedIn={SpotifyAuth.IsLoggedIn}");
                        resp.Dispose();
                        return (null, status);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyGateway] {method} {path} exception: {Describe(ex)}");
                return (null, 0);
            }
        }

        // สะพานจากโลก HttpResponseMessage เข้าเกณฑ์ pure ของ policy - อ่านสภาพ token สดทุกครั้งที่ถาม
        private static GatewayAction Classify(HttpResponseMessage resp) =>
            SpotifyGatewayPolicy.Classify((int)resp.StatusCode, SpotifyAuth.HasUsableTokenPublic);

        private static async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, string jsonBody)
        {
            var request = new HttpRequestMessage(method, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SpotifyAuth.AccessToken);
            if (jsonBody != null)
                request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            return await Http.SendAsync(request);
        }

    }
}
