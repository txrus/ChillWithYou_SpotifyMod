// SpotifyApi.cs
// เวอร์ชัน Web API ล้วน (ถอด SMTC ออกทั้งหมด เพราะ Mono ไม่รองรับ WinRT)
// ต้อง login ผ่าน SpotifyAuth ก่อนถึงจะใช้ปุ่มควบคุมได้ทุกปุ่ม
// ตัด shuffle/repeat ออกตามที่ตกลงกัน + แก้ปัญหา "Restriction violated" ตอนสั่ง play
// ด้วยการระบุ device_id ให้ชัดเจนทุกครั้ง (ไม่งั้น Spotify บางทีงงว่าจะสั่ง play ที่ไหน)
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChillWithYou_SpotifyMod
{
    public class SpotifyNowPlayingInfo
    {
        public string TrackId;
        public string Title;
        public string Artist;
        public bool IsPlaying;
        public TimeSpan Position;
        public TimeSpan Duration;
        public byte[] ThumbnailBytes; // ปกอัลบั้ม โหลดจาก URL ของ Spotify
        public string PlaylistContextId; // parse จาก context.uri ของ /me/player call เดียวกันนี้เลย
                                         // ไม่ต้องยิง endpoint แยกเพื่อเช็คว่า playlist เปลี่ยนไหม
                                         // null เมื่อเล่นจาก context ที่ไม่ใช่ playlist (artist/album) - ดู ContextUri
        public string ContextUri;        // context.uri ดิบ เช่น spotify:artist:xxx / spotify:album:xxx
                                         // ใช้เช็คว่า context เปลี่ยนไหม แทน PlaylistContextId ที่เห็นแค่ playlist
    }

    internal static class SpotifyApi
    {
        private static readonly HttpClient Http = new HttpClient();

        // จำ device_id ล่าสุดที่เห็นตอน poll ข้อมูลเพลง เอาไว้ใช้ตอนสั่ง play/pause
        // เพราะ Spotify เจอ "Restriction violated" บ่อยถ้าไม่ระบุ device_id ให้ชัดเจน
        private static string _lastKnownDeviceId;
        public static string LastKnownDeviceId => _lastKnownDeviceId;

        private static string _loggedDeviceId; // device ตัวล่าสุดที่ log ไปแล้ว - กัน log ซ้ำทุกรอบ poll

        // cache ปกอัลบั้มตาม URL - refresh รอบใหม่ที่เพลง/ปกเดิมไม่ต้องโหลดรูปซ้ำ
        private static string _lastCoverUrl;
        private static byte[] _lastCoverBytes;

        // เปิดให้ SpotifyButtonsInjector เรียกใช้ตอนสั่ง play track/context จาก search results โดยตรง
        public static async Task SendPlayBody(string path, string jsonBody)
            => await SendAsync(HttpMethod.Put, path, jsonBody);

        // รวมไว้ที่ SpotifyAuth แล้ว - มี lock กัน refresh ซ้อนกันจากหลายคลาสพร้อมกัน
        private static Task<bool> EnsureValidTokenAsync() => SpotifyAuth.EnsureValidTokenAsync();

        private static async Task<HttpResponseMessage> SendNowPlayingRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player");
            request.Headers.Add("Authorization", $"Bearer {SpotifyAuth.AccessToken}");
            return await Http.SendAsync(request);
        }

        // log ว่า token ที่ถืออยู่เป็นของบัญชีไหน - ใช้ไล่เคสที่ล็อกอินคนละบัญชีกับที่ตั้งใจ
        // (เช็คว่าเป็น Premium ผ่าน API ไม่ได้แล้ว Spotify ตัด field product ออกตั้งแต่รอบ ก.พ. 2026)
        public static async Task LogCurrentUser()
        {
            if (!await EnsureValidTokenAsync()) return;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
                request.Headers.Add("Authorization", $"Bearer {SpotifyAuth.AccessToken}");
                HttpResponseMessage resp = await Http.SendAsync(request);
                string body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    Plugin.Log.LogWarning($"[SpotifyApi] /me failed: {resp.StatusCode} - {body}");
                    return;
                }
                JObject me = JObject.Parse(body);
                Plugin.Log.LogInfo($"[SpotifyApi] login เป็นบัญชี: id='{me.Value<string>("id")}' " +
                    $"name='{me.Value<string>("display_name")}'");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SpotifyApi] /me exception: {ex.Message}");
            }
        }

        // คืน true เมื่อ Spotify รับคำสั่งจริง เพื่อให้ UI สลับสถานะเองได้โดยไม่ต้องยิง GET ตามหลัง
        public static async Task<bool> PlayPause(bool isCurrentlyPlaying)
        {
            if (!await EnsureValidTokenAsync()) { Plugin.Log.LogWarning("[SpotifyApi] Not logged in"); return false; }

            string endpoint = isCurrentlyPlaying ? "pause" : "play";
            string path = $"me/player/{endpoint}";
            if (!string.IsNullOrEmpty(_lastKnownDeviceId))
                path += $"?device_id={_lastKnownDeviceId}";

            return await SendAsync(HttpMethod.Put, path, jsonBody: "{}");
        }

        public static async Task Next()
        {
            if (!await EnsureValidTokenAsync()) return;
            string path = "me/player/next" + DeviceQuery("?");
            await SendAsync(HttpMethod.Post, path);
        }

        public static async Task Previous()
        {
            if (!await EnsureValidTokenAsync()) return;
            string path = "me/player/previous" + DeviceQuery("?");
            await SendAsync(HttpMethod.Post, path);
        }

        private static string DeviceQuery(string prefix) =>
            string.IsNullOrEmpty(_lastKnownDeviceId) ? "" : $"{prefix}device_id={_lastKnownDeviceId}";

        // เล่นเพลงเดียวจาก URI ตรงๆ (ใช้กับผลลัพธ์ search ประเภท track)
        public static async Task PlayTrackUri(string trackUri)
        {
            if (!await EnsureValidTokenAsync()) { Plugin.Log.LogWarning("[SpotifyApi] Not logged in"); return; }

            string path = "me/player/play";
            if (!string.IsNullOrEmpty(_lastKnownDeviceId))
                path += $"?device_id={_lastKnownDeviceId}";

            string body = $"{{\"uris\":[\"{trackUri}\"]}}";
            await SendAsync(HttpMethod.Put, path, jsonBody: body);
        }

        // เล่นทั้ง context (album/playlist/artist) จาก URI - ใช้กับผลลัพธ์ search ประเภท album/playlist/artist
        // artist URI จะเล่นเป็น top tracks/radio ของศิลปินนั้น (พฤติกรรมมาตรฐานของ Spotify)
        public static async Task PlayContextUri(string contextUri)
        {
            if (!await EnsureValidTokenAsync()) { Plugin.Log.LogWarning("[SpotifyApi] Not logged in"); return; }

            string path = "me/player/play";
            if (!string.IsNullOrEmpty(_lastKnownDeviceId))
                path += $"?device_id={_lastKnownDeviceId}";

            string body = $"{{\"context_uri\":\"{contextUri}\"}}";
            await SendAsync(HttpMethod.Put, path, jsonBody: body);
        }

        public static async Task<SpotifyNowPlayingInfo> GetCurrentlyPlaying()
        {
            if (SpotifyRateLimiter.IsBlocked)
            {
                Plugin.Log.LogInfo($"[SpotifyApi] ข้าม GetCurrentlyPlaying: ยังโดน rate limit อยู่อีก {SpotifyRateLimiter.RemainingBlock.TotalSeconds:F0} วิ");
                return null;
            }

            if (!await EnsureValidTokenAsync())
            {
                Plugin.Log.LogWarning("[SpotifyApi] GetCurrentlyPlaying: not logged in / token refresh failed");
                return null;
            }

            try
            {
                HttpResponseMessage resp = await SendNowPlayingRequest();

                // Spotify ตอบ 401 "Access token missing" เป็นครั้งคราวทั้งที่ token ยังใช้ได้จริง
                // (ยืนยันจาก log: len=248, เหลืออายุ ~58 นาที) มักโผล่ตอนยิงถี่ๆ หลังสั่งเล่นเพลง
                // ซึ่ง RefreshAfterPlay ยิง 4 รอบใน ~1.8 วิ - ลองใหม่ 1 ครั้งก่อนยอมแพ้
                // ไม่ refresh token เพราะไม่ได้หมดอายุ การ refresh จะยิ่งเปลืองและไม่แก้อะไร
                if (resp.StatusCode == HttpStatusCode.Unauthorized && SpotifyAuth.HasUsableTokenPublic)
                {
                    Plugin.Log.LogInfo("[SpotifyApi] เจอ 401 ทั้งที่ token ยังดีอยู่ - ลองใหม่อีกครั้ง");
                    resp.Dispose();
                    await Task.Delay(250);
                    resp = await SendNowPlayingRequest();
                }

                if (resp.StatusCode == (HttpStatusCode)429)
                {
                    SpotifyRateLimiter.ReportTooManyRequests(resp);
                    return null;
                }

                if (resp.StatusCode == HttpStatusCode.NoContent)
                {
                    Plugin.Log.LogInfo("[SpotifyApi] GetCurrentlyPlaying: 204 No Content (ไม่มี active device อยู่ตอนนี้)");
                    return null;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    string errBody = await resp.Content.ReadAsStringAsync();
                    Plugin.Log.LogWarning($"[SpotifyApi] GetCurrentlyPlaying failed: {resp.StatusCode} - {errBody}");
                    // ถ้ายัง 401 อยู่หลัง retry แสดงว่าไม่ใช่อาการชั่วคราวแล้ว - log สภาพ token ไว้สืบต่อ
                    if (resp.StatusCode == HttpStatusCode.Unauthorized)
                        Plugin.Log.LogWarning(
                            $"[SpotifyApi] token ตอนยิง: len={SpotifyAuth.AccessToken?.Length ?? -1}, " +
                            $"เหลืออายุ={(SpotifyAuth.TokenExpiresAt - DateTime.UtcNow).TotalSeconds:F0}s, " +
                            $"IsLoggedIn={SpotifyAuth.IsLoggedIn}");
                    return null;
                }

                string json = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json))
                {
                    Plugin.Log.LogInfo("[SpotifyApi] GetCurrentlyPlaying: response ว่างเปล่า");
                    return null;
                }

                JObject obj = JObject.Parse(json);

                // เก็บ device_id ไว้ใช้ตอนสั่ง play/pause/next/prev ครั้งถัดไป
                // ใช้ "as JObject" ก่อน เพราะถ้า field เป็น JSON null จริงๆ Newtonsoft จะคืน JValue (ไม่ใช่ C# null)
                // แล้ว ?["key"] บน JValue จะ throw InvalidOperationException แทนที่จะคืน null เฉยๆ
                JObject device = obj["device"] as JObject;
                string deviceId = (string)device?["id"];
                if (!string.IsNullOrEmpty(deviceId))
                    _lastKnownDeviceId = deviceId;

                // log ตอนสลับอุปกรณ์เท่านั้น กัน log ท่วมจาก poll ทุกรอบ
                // is_restricted=true คือ Spotify ห้ามสั่งควบคุมอุปกรณ์ตัวนี้ผ่าน Web API
                if (device != null && deviceId != _loggedDeviceId)
                {
                    _loggedDeviceId = deviceId;
                    Plugin.Log.LogInfo($"[SpotifyApi] device: name='{(string)device["name"]}' " +
                        $"type='{(string)device["type"]}' active={device["is_active"]} " +
                        $"restricted={device["is_restricted"]} private={device["is_private_session"]}");
                }

                // "item" เป็น JSON null ได้ (เช่นตอนไม่ได้เล่นอะไรอยู่) ซึ่ง Newtonsoft จะคืน JValue ไม่ใช่ C# null
                // ต้องเช็คด้วย "as JObject" ไม่งั้นจะหลุดไปเข้า item["..."] แล้ว throw ทีหลัง
                if (!(obj["item"] is JObject item))
                {
                    Plugin.Log.LogInfo("[SpotifyApi] GetCurrentlyPlaying: ไม่มี item (ไม่ได้เล่นอะไรอยู่)");
                    return null;
                }

                JArray artists = item["artists"] as JArray;
                string artist = artists != null && artists.Count > 0
                    ? string.Join(", ", artists.Select(a => (string)a["name"]))
                    : null;

                byte[] thumbBytes = null;
                string coverUrl = null;
                if ((item["album"] as JObject)?["images"] is JArray coverImages && coverImages.Count > 0)
                    coverUrl = coverImages[0]?.Value<string>("url");
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    if (coverUrl == _lastCoverUrl && _lastCoverBytes != null)
                    {
                        thumbBytes = _lastCoverBytes; // ปกเดิม ใช้ของที่โหลดไว้แล้ว
                    }
                    else
                    {
                        try
                        {
                            thumbBytes = await Http.GetByteArrayAsync(coverUrl);
                            _lastCoverUrl = coverUrl;
                            _lastCoverBytes = thumbBytes;
                        }
                        catch (Exception ex) { Plugin.Log.LogWarning($"[SpotifyApi] Cover download failed: {ex.Message}"); }
                    }
                }

                // เอา playlist id มาจาก context.uri ของ response นี้เลย (ถ้าเล่นจาก playlist อยู่)
                // ไม่ต้องยิง /me/player/currently-playing แยกอีกรอบเพื่อเช็คแค่เรื่องนี้
                string playlistContextId = null;
                string contextUri = (string)(obj["context"] as JObject)?["uri"];
                const string playlistPrefix = "spotify:playlist:";
                if (!string.IsNullOrEmpty(contextUri) && contextUri.StartsWith(playlistPrefix))
                    playlistContextId = contextUri.Substring(playlistPrefix.Length);

                return new SpotifyNowPlayingInfo
                {
                    TrackId = (string)item["id"],
                    Title = (string)item["name"],
                    Artist = artist,
                    IsPlaying = (bool?)obj["is_playing"] ?? false,
                    Position = TimeSpan.FromMilliseconds((int?)obj["progress_ms"] ?? 0),
                    Duration = TimeSpan.FromMilliseconds((int?)item["duration_ms"] ?? 0),
                    ThumbnailBytes = thumbBytes,
                    PlaylistContextId = playlistContextId,
                    ContextUri = contextUri,
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyApi] GetCurrentlyPlaying exception: {ex}");
                return null;
            }
        }

        // คืน true เฉพาะตอน Spotify ตอบ 2xx - ผู้เรียกส่วนใหญ่ไม่สนผลลัพธ์ ยกเว้น PlayPause
        private static async Task<bool> SendAsync(HttpMethod method, string path, string jsonBody = null)
        {
            if (SpotifyRateLimiter.IsBlocked)
            {
                Plugin.Log.LogInfo($"[SpotifyApi] ข้าม {method} {path}: ยังโดน rate limit อยู่อีก {SpotifyRateLimiter.RemainingBlock.TotalSeconds:F0} วิ");
                return false;
            }

            try
            {
                var request = new HttpRequestMessage(method, $"https://api.spotify.com/v1/{path}");
                request.Headers.Add("Authorization", $"Bearer {SpotifyAuth.AccessToken}");

                if (jsonBody != null)
                    request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

                HttpResponseMessage resp = await Http.SendAsync(request);

                if (resp.StatusCode == (HttpStatusCode)429)
                {
                    SpotifyRateLimiter.ReportTooManyRequests(resp);
                    return false;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    Plugin.Log.LogWarning($"[SpotifyApi] {method} {path} failed: {resp.StatusCode} - {body}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyApi] {method} {path} exception: {ex}");
                return false;
            }
        }
    }
}