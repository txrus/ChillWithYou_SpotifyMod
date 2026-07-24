// SpotifyApi.cs
// เวอร์ชัน Web API ล้วน (ถอด SMTC ออกทั้งหมด เพราะ Mono ไม่รองรับ WinRT)
// ต้อง login ผ่าน SpotifyAuth ก่อนถึงจะใช้ปุ่มควบคุมได้ทุกปุ่ม
// ตัด shuffle/repeat ออกตามที่ตกลงกัน + แก้ปัญหา "Restriction violated" ตอนสั่ง play
// ด้วยการระบุ device_id ให้ชัดเจนทุกครั้ง (ไม่งั้น Spotify บางทีงงว่าจะสั่ง play ที่ไหน)
//
// envelope ของการยิง request (token/bearer/429/retry/log) ย้ายไป SpotifyGateway แล้ว
// ไฟล์นี้เหลือหน้าที่แค่ประกอบ path + parse JSON ของ now-playing
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChillWithYou_SpotifyMod
{
    // SpotifyNowPlayingInfo ย้ายไป SpotifyModels.cs (ไฟล์ DTO ล้วนที่ test project link-compile ได้)

    internal static class SpotifyApi
    {
        // จำ device_id ล่าสุดที่เห็นตอน poll ข้อมูลเพลง เอาไว้ใช้ตอนสั่ง play/pause
        // เพราะ Spotify เจอ "Restriction violated" บ่อยถ้าไม่ระบุ device_id ให้ชัดเจน
        private static string _lastKnownDeviceId;

        private static string _loggedDeviceId; // device ตัวล่าสุดที่ log ไปแล้ว - กัน log ซ้ำทุกรอบ poll

        // cache ปกอัลบั้มตาม URL - refresh รอบใหม่ที่เพลง/ปกเดิมไม่ต้องโหลดรูปซ้ำ
        private static string _lastCoverUrl;
        private static byte[] _lastCoverBytes;

        // log ว่า token ที่ถืออยู่เป็นของบัญชีไหน - ใช้ไล่เคสที่ล็อกอินคนละบัญชีกับที่ตั้งใจ
        // (เช็คว่าเป็น Premium ผ่าน API ไม่ได้แล้ว Spotify ตัด field product ออกตั้งแต่รอบ ก.พ. 2026)
        public static async Task LogCurrentUser()
        {
            JObject me = await SpotifyGateway.GetJsonAsync("me");
            if (me == null) return;
            Plugin.Log.LogInfo($"[SpotifyApi] login เป็นบัญชี: id='{me.Value<string>("id")}' " +
                $"name='{me.Value<string>("display_name")}'");
        }

        // คืน true เมื่อ Spotify รับคำสั่งจริง เพื่อให้ UI สลับสถานะเองได้โดยไม่ต้องยิง GET ตามหลัง
        public static Task<bool> PlayPause(bool isCurrentlyPlaying)
        {
            string endpoint = isCurrentlyPlaying ? "pause" : "play";
            string path = $"me/player/{endpoint}" + DeviceQuery("?");
            return SpotifyGateway.SendAsync(HttpMethod.Put, path, jsonBody: "{}");
        }

        public static Task Next() => SpotifyGateway.SendAsync(HttpMethod.Post, "me/player/next" + DeviceQuery("?"));

        public static Task Previous() => SpotifyGateway.SendAsync(HttpMethod.Post, "me/player/previous" + DeviceQuery("?"));

        private static string DeviceQuery(string prefix) =>
            string.IsNullOrEmpty(_lastKnownDeviceId) ? "" : $"{prefix}device_id={_lastKnownDeviceId}";

        // เล่นเพลงเดียวจาก URI ตรงๆ (ใช้กับผลลัพธ์ search ประเภท track) - หลุด context เดิม
        public static Task<bool> PlayTrackUri(string trackUri) =>
            PlayAsync($"{{\"uris\":[\"{trackUri}\"]}}");

        // เล่นทั้ง context (album/playlist/artist) ตั้งแต่ต้น - ใช้กับผลลัพธ์ search ประเภท album/playlist/artist
        // artist URI จะเล่นเป็น top tracks/radio ของศิลปินนั้น (พฤติกรรมมาตรฐานของ Spotify)
        public static Task<bool> PlayContextUri(string contextUri) =>
            PlayAsync($"{{\"context_uri\":\"{contextUri}\"}}");

        // เล่นเพลงหนึ่งใน context โดยยังอยู่ใน context นั้น (next/prev เดินตาม playlist/album ต่อได้)
        // Spotify รับ offset เฉพาะ album/playlist - artist context ใช้ท่านี้ไม่ได้
        public static Task<bool> PlayContextAtTrackUri(string contextUri, string trackUri) =>
            PlayAsync($"{{\"context_uri\":\"{contextUri}\",\"offset\":{{\"uri\":\"{trackUri}\"}}}}");

        // ทุกคำสั่งเล่นใช้ envelope เดียวกัน: PUT me/player/play + device_id (ไม่ระบุแล้ว Spotify
        // ตอบ "Restriction violated" เป็นครั้งคราวเพราะไม่รู้ว่าจะสั่งเครื่องไหน)
        private static Task<bool> PlayAsync(string jsonBody) =>
            SpotifyGateway.SendAsync(HttpMethod.Put, "me/player/play" + DeviceQuery("?"), jsonBody);

        public static async Task<SpotifyNowPlayingInfo> GetCurrentlyPlaying()
        {
            JObject obj = await SpotifyGateway.GetJsonAsync("me/player");
            if (obj == null) return null; // blocked / ยังไม่ login / 204 / error - gateway log ให้แล้ว

            try
            {
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

                // ปกอัลบั้ม: โหลดผ่าน gateway แต่ cache ตาม URL - ปกเดิมไม่โหลดซ้ำ
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
                        thumbBytes = await SpotifyGateway.GetImageAsync(coverUrl);
                        if (thumbBytes != null)
                        {
                            _lastCoverUrl = coverUrl;
                            _lastCoverBytes = thumbBytes;
                        }
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
                Plugin.Log.LogError($"[SpotifyApi] GetCurrentlyPlaying parse exception: {ex}");
                return null;
            }
        }
    }
}
