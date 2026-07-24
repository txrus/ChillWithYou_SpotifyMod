using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChillWithYou_SpotifyMod
{
    // PlaylistTrackInfo / PlaylistInfo / UserPlaylistInfo ย้ายไป SpotifyModels.cs

    // ยิงผ่าน SpotifyGateway ทั้งหมด (envelope token/bearer/429/retry รวมอยู่ที่นั่น)
    // ไฟล์นี้เหลือหน้าที่แค่ประกอบ path, cache ผลลัพธ์, และ parse JSON เป็นชนิดข้อมูลข้างบน
    internal static class SpotifyWebApi
    {
        // --- 🌟 ตัวแปรใหม่สำหรับจดจำข้อมูล (Cache) ---
        private static string _lastPlaylistId = null;
        private static PlaylistInfo _cachedPlaylistInfo = null;

        // รายชื่อ playlist ของ user - cache ไว้ทั้ง session เพราะแทบไม่เปลี่ยนระหว่างเล่นเกม
        private static List<UserPlaylistInfo> _cachedMyPlaylists = null;

        // รายการอัลบั้มต่อศิลปิน - กาง/หุบซ้ำในเซสชันเดียวไม่ต้องยิง API ใหม่
        private static readonly Dictionary<string, List<ArtistAlbumInfo>> _cachedArtistAlbums =
            new Dictionary<string, List<ArtistAlbumInfo>>();


        // ล้างความจำ playlist ล่าสุด - ใช้ตอนผู้ใช้กดปุ่ม refresh เพื่อบังคับดึงข้อมูล/คิวใหม่จริงๆ
        public static void InvalidateCache()
        {
            _lastPlaylistId = null;
            _cachedPlaylistInfo = null;
            _cachedMyPlaylists = null;
            _cachedArtistAlbums.Clear();
        }

        // playlistId มาจาก SpotifyNowPlayingInfo.PlaylistContextId ที่ SpotifyApi.GetCurrentlyPlaying()
        // parse ไว้ให้แล้ว (จาก /me/player call เดียวกัน) ไม่ต้องยิง endpoint แยกมาหา playlist id เอง
        public static async Task<PlaylistInfo> GetCurrentPlaylistAsync(string playlistId, int maxTracks = 10)
        {
            // ตอนโดน rate limit คืน playlist เดิมที่ cache ไว้ ไม่ปล่อยให้ UI ว่าง
            if (SpotifyRateLimiter.IsBlocked)
            {
                Plugin.Log.LogInfo($"[SpotifyWebApi] ข้าม GetCurrentPlaylist: ยังโดน rate limit อยู่อีก {SpotifyRateLimiter.RemainingBlock.TotalSeconds:F0} วิ");
                return _cachedPlaylistInfo;
            }

            try
            {
                if (string.IsNullOrEmpty(playlistId))
                {
                    // ไม่ได้เล่นเพลงจาก Playlist ตอนนี้ (เช่น เล่นจาก album/liked songs) -> ล้างความจำทิ้ง
                    _lastPlaylistId = null;
                    _cachedPlaylistInfo = null;
                    return null;
                }

                // 🌟 ถ้าเป็น ID เดิม ให้คืนค่าที่จำไว้ทันที (ไม่ยิง API ซ้ำ)
                if (playlistId == _lastPlaylistId && _cachedPlaylistInfo != null)
                {
                    return _cachedPlaylistInfo;
                }

                Plugin.Log.LogInfo($"[SpotifyWebApi] พบ Playlist ใหม่! กำลังโหลดข้อมูลสำหรับ ID: {playlistId}");

                // ดึง meta + track list ใน call เดียวผ่าน /playlists/{id}?fields=...
                // เหตุผล: endpoint แยก /playlists/{id}/tracks โดน 403 กับ app ใหม่ใน development mode
                // แต่ endpoint หลัก /playlists/{id} ยังใช้ได้ปกติ (แถมประหยัด API call ลงครึ่งนึงด้วย)
                (string name, string coverUrl, List<PlaylistTrackInfo> tracks) = await GetPlaylistFullAsync(playlistId, maxTracks);

                if (string.IsNullOrEmpty(name))
                {
                    // fetch พลาด (เช่น 429 TooManyRequests) -> อย่า cache ค้างเป็น "Unknown Playlist" ตลอดไป
                    // ไม่อัปเดต _lastPlaylistId เพื่อให้รอบ poll ถัดไปลองโหลดใหม่อีกครั้งโดยอัตโนมัติ
                    Plugin.Log.LogWarning("[SpotifyWebApi] Playlist fetch พลาด จะลองใหม่รอบถัดไป (ไม่ cache ค้างไว้เป็น Unknown Playlist)");
                    return _cachedPlaylistInfo; // คืนของเก่าไปก่อน (null หรือ playlist ก่อนหน้า) ระหว่างรอ retry
                }

                if (tracks != null && tracks.Count == 0)
                {
                    // Spotify ตัด track list ออกจาก response (ข้อจำกัด development mode app)
                    // -> ใช้คิวเพลงจาก /me/player/queue แทน ได้เพลงปัจจุบัน + เพลงถัดไปของ context เดียวกันนี้แหละ
                    Plugin.Log.LogInfo("[SpotifyWebApi] Track list โดนตัดจาก response - ใช้คิวเพลงจาก /me/player/queue แทน");
                    List<PlaylistTrackInfo> queueTracks = await GetQueueTracksAsync(maxTracks);
                    if (queueTracks != null && queueTracks.Count > 0)
                        tracks = queueTracks;
                }

                byte[] coverBytes = await SpotifyGateway.GetImageAsync(coverUrl);

                // 🌟 อัปเดตความจำด้วยข้อมูลใหม่ล่าสุด (เฉพาะตอน meta fetch สำเร็จเท่านั้น)
                _lastPlaylistId = playlistId;
                _cachedPlaylistInfo = new PlaylistInfo
                {
                    Id = playlistId,
                    Name = name,
                    CoverImageBytes = coverBytes,
                    Tracks = tracks,
                    ContextUri = $"spotify:playlist:{playlistId}"
                };

                return _cachedPlaylistInfo;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyWebApi] GetCurrentPlaylist exception: {ex}");
                return null;
            }
        }

        // ดึงรายชื่อ playlist ของ user เอง (GET /me/playlists - ใช้ scope playlist-read-private ที่ขอไว้แล้ว
        // และ endpoint นี้ไม่โดนบล็อกใน development mode ต่างจากการอ่าน track list ของ playlist)
        // คืน null = โหลดพลาดและไม่มี cache เก่าให้ใช้ / กด ↻ ที่ header จะล้าง cache ผ่าน InvalidateCache
        public static async Task<List<UserPlaylistInfo>> GetMyPlaylistsAsync(int limit = 20)
        {
            if (_cachedMyPlaylists != null)
                return _cachedMyPlaylists;

            JObject obj = await SpotifyGateway.GetJsonAsync($"me/playlists?limit={limit}");
            if (obj == null) return null; // blocked / ยังไม่ login / error - gateway log ให้แล้ว

            var playlists = new List<UserPlaylistInfo>();
            if (obj["items"] is JArray items)
            {
                foreach (JToken it in items)
                {
                    if (!(it is JObject p)) continue; // Spotify ส่ง item เป็น null มาได้เป็นครั้งคราว

                    playlists.Add(new UserPlaylistInfo
                    {
                        Id = (string)p["id"],
                        Name = (string)p["name"],
                        TrackCount = (int?)(p["tracks"] as JObject)?["total"] ?? 0,
                        // แถวโชว์รูปแค่ 32px - เอาตัวเล็กสุดที่ยังไม่ต่ำกว่า 64 พอ
                        ImageUrl = SpotifyGateway.PickImageUrl(p["images"], minWidth: 64),
                    });
                }
            }

            _cachedMyPlaylists = playlists;
            return playlists;
        }

        // รายการอัลบั้มของศิลปิน (GET /artists/{id}/albums) สำหรับกางดูใต้แถวศิลปินในผลค้นหา
        // เพดาน limit ของ endpoint นี้คือ 10 ต่อหน้า (ลดจาก 50 รอบเดียวกับที่ /search โดนลด)
        // ขอทีเดียว 50 จะได้ 400 กลับมา จึงไล่ทีละหน้าด้วย offset จนครบ maxAlbums หรือหมดรายการ
        // คืน null = หน้าแรกโหลดไม่ได้ (ผู้เรียกแสดงข้อความแจ้งแทนรายการ และไม่ cache ไว้
        // เพื่อให้กดใหม่แล้วลองอีกครั้งได้) / พลาดกลางทางใช้เท่าที่ได้มาแล้วดีกว่าทิ้งทั้งชุด
        public static async Task<List<ArtistAlbumInfo>> GetArtistAlbumsAsync(string artistId, int maxAlbums = 50)
        {
            if (string.IsNullOrEmpty(artistId)) return null;
            if (_cachedArtistAlbums.TryGetValue(artistId, out List<ArtistAlbumInfo> cached))
                return cached;

            const int PageSize = 10;
            var albums = new List<ArtistAlbumInfo>();
            // Spotify คืนอัลบั้มเดียวกันซ้ำหลายรายการ (คนละ market / reissue) - เอาชื่อละครั้งพอ
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int offset = 0; offset < maxAlbums; offset += PageSize)
            {
                JObject obj = await SpotifyGateway.GetJsonAsync(
                    $"artists/{artistId}/albums?include_groups=album,single&limit={PageSize}&offset={offset}");
                if (obj == null)
                    return albums.Count > 0 ? albums : null;

                if (!(obj["items"] is JArray items) || items.Count == 0) break;

                foreach (JToken it in items)
                {
                    if (!(it is JObject al)) continue;

                    string name = (string)al["name"];
                    if (string.IsNullOrEmpty(name) || !seenNames.Add(name)) continue;

                    string releaseDate = (string)al["release_date"];
                    albums.Add(new ArtistAlbumInfo
                    {
                        Id = (string)al["id"],
                        Name = name,
                        TrackCount = (int?)al["total_tracks"] ?? 0,
                        // ปกโชว์แค่ 34px ในแถว แต่ใช้ต่อเป็นปก header ตอนกดเข้าไปดูเพลงในอัลบั้มด้วย
                        CoverUrl = SpotifyGateway.PickImageUrl(al["images"], minWidth: 160),
                        // release_date เป็น "2003", "2003-05" หรือ "2003-05-20" ตาม precision ที่ Spotify มี
                        ReleaseYear = releaseDate != null && releaseDate.Length >= 4
                            ? releaseDate.Substring(0, 4)
                            : null,
                    });
                }

                // หน้าสุดท้ายแล้ว (ได้ไม่เต็มหน้า หรือ next เป็น null) - ไม่ต้องยิงหน้าถัดไปให้เปลือง
                if (items.Count < PageSize || obj["next"] == null || obj["next"].Type == JTokenType.Null)
                    break;
            }

            _cachedArtistAlbums[artistId] = albums;
            return albums;
        }

        // ดึงชื่อ + ปก + track list ใน call เดียวจาก /playlists/{id} (endpoint แยก /tracks โดน 403 กับ app ใหม่)
        // name == null แปลว่า fetch พลาดทั้งก้อน ให้ผู้เรียก retry รอบหน้า
        private static async Task<(string name, string coverUrl, List<PlaylistTrackInfo> tracks)> GetPlaylistFullAsync(string playlistId, int maxTracks)
        {
            JObject obj = await SpotifyGateway.GetJsonAsync(
                $"playlists/{playlistId}?fields=name,images,tracks.items(track(id,name,duration_ms,artists(name)))");
            if (obj == null) return (null, null, null); // null = สัญญาณว่า "พลาด" ให้ผู้เรียก retry แทนที่จะ cache ค้าง

            string name = obj["name"]?.ToString();

            string coverUrl = null;
            if (obj["images"] is JArray images && images.Count > 0)
                coverUrl = (string)images[0]["url"];

            var tracks = new List<PlaylistTrackInfo>();
            if ((obj["tracks"] as JObject)?["items"] is JArray items)
            {
                foreach (JToken it in items)
                {
                    if (tracks.Count >= maxTracks) break; // fields param จำกัดจำนวนไม่ได้ เลยตัดเองฝั่งนี้

                    if (!(it["track"] is JObject track)) continue; // track เป็น null ได้ (เพลงโดนลบ/local file)

                    JArray artists = track["artists"] as JArray;
                    string artist = artists != null && artists.Count > 0
                        ? string.Join(", ", artists.Select(a => (string)a["name"]))
                        : "Unknown Artist";

                    tracks.Add(new PlaylistTrackInfo
                    {
                        Id = (string)track["id"],
                        Title = (string)track["name"],
                        Artist = artist,
                        DurationMs = (int?)track["duration_ms"] ?? 0,
                    });
                }
            }

            return (name, coverUrl, tracks);
        }

        // สร้าง PlaylistInfo จากคิวเพลง สำหรับ context ที่ไม่ใช่ playlist (artist/album)
        // อ่านรายชื่อเพลงของ context พวกนี้ตรงๆ ไม่ได้แล้ว (/artists/{id}/top-tracks ถูกตัดจาก
        // development mode ตั้งแต่รอบ ก.พ. 2026 - ส่วน /artists/{id}/albums ยังลองใช้อยู่ใน
        // GetArtistAlbumsAsync และมีทางลงถ้าโดนบล็อก) แต่ /me/player/queue
        // เป็น playback endpoint ที่ไม่โดนตัด และไม่สนว่า context เป็นชนิดไหน เลยใช้แทนได้
        // ไม่ cache เพราะฝั่งผู้เรียกยิงเฉพาะตอน context เปลี่ยนอยู่แล้ว
        // คืน null = โหลดพลาด -> ผู้เรียกไม่ควร commit ว่าโหลดแล้ว จะได้ retry รอบหน้า
        public static async Task<PlaylistInfo> GetContextQueueAsync(string contextUri, string displayName, byte[] coverBytes, int maxTracks)
        {
            if (string.IsNullOrEmpty(contextUri)) return null;
            try
            {
                List<PlaylistTrackInfo> tracks = await GetQueueTracksAsync(maxTracks);
                if (tracks == null || tracks.Count == 0) return null;

                return new PlaylistInfo
                {
                    Id = contextUri,
                    Name = string.IsNullOrEmpty(displayName) ? "Now playing" : displayName,
                    Tracks = tracks,
                    // /me/player/queue ไม่ให้ภาพของตัว context มา ผู้เรียกเลยส่งปกที่โหลดไว้แล้วมาให้
                    // (ใช้กับ album ที่ทุกเพลงใช้ปกเดียวกันอยู่แล้ว ส่วน artist ส่ง null มา)
                    CoverImageBytes = coverBytes,
                    ContextUri = contextUri
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyWebApi] GetContextQueue exception: {ex}");
                return null;
            }
        }

        // รายชื่อเพลงของอัลบั้ม สำหรับกดอัลบั้มจากผลค้นหาแล้วดูเพลงก่อนตัดสินใจเล่น
        // /albums/{id}/tracks ไม่ให้ภาพปกมาด้วย ผู้เรียกเลยส่ง coverUrl ที่ได้จากผลค้นหามาให้โหลดต่อ
        // คืน null = โหลดพลาด (gateway log ให้แล้ว) -> ผู้เรียกไม่ควรล้างของเดิมบนจอทิ้ง
        public static async Task<PlaylistInfo> GetAlbumTracksAsync(string albumId, string albumName, string coverUrl, int maxTracks = 20)
        {
            if (string.IsNullOrEmpty(albumId)) return null;

            JObject obj = await SpotifyGateway.GetJsonAsync($"albums/{albumId}/tracks?limit={maxTracks}");
            if (obj == null) return null;

            var tracks = new List<PlaylistTrackInfo>();
            if (obj["items"] is JArray items)
            {
                foreach (JToken it in items)
                {
                    if (!(it is JObject t)) continue; // Spotify ส่ง item เป็น null มาได้เป็นครั้งคราว

                    JArray artists = t["artists"] as JArray;
                    tracks.Add(new PlaylistTrackInfo
                    {
                        Id = (string)t["id"],
                        Title = (string)t["name"],
                        Artist = artists != null && artists.Count > 0
                            ? string.Join(", ", artists.Select(a => (string)a["name"]))
                            : "Unknown Artist",
                        DurationMs = (int?)t["duration_ms"] ?? 0,
                    });
                }
            }

            return new PlaylistInfo
            {
                Id = albumId,
                Name = albumName,
                Tracks = tracks,
                CoverImageBytes = await SpotifyGateway.GetImageAsync(coverUrl),
                ContextUri = $"spotify:album:{albumId}"
            };
        }

        // ดึงเพลงปัจจุบัน + คิวถัดไปจาก /me/player/queue (ใช้ scope user-read-playback-state ที่มีอยู่แล้ว)
        // ใช้เป็น fallback เวลาอ่าน track list ของ playlist ตรงๆ ไม่ได้ (app ใหม่ใน development mode
        // โดน Spotify บล็อกทั้ง endpoint /tracks (403) และ field tracks ใน /playlists/{id} (โดนตัดเงียบๆ))
        // คืน null = โหลดพลาด
        private static async Task<List<PlaylistTrackInfo>> GetQueueTracksAsync(int maxTracks)
        {
            JObject obj = await SpotifyGateway.GetJsonAsync("me/player/queue");
            if (obj == null) return null;

            var tracks = new List<PlaylistTrackInfo>();
            // repeat + playlist สั้นกว่าหน้าต่างคิว (~20) ทำให้ /me/player/queue วนกลับไปต้น context
            // -> เพลงเดิมโผล่ซ้ำ แสดงเพลงละครั้งพอ (dedupe ตาม track id)
            var seenIds = new HashSet<string>();

            // เพลงที่กำลังเล่นอยู่ขึ้นเป็นแถวแรก แล้วตามด้วยคิวถัดไป
            if (obj["currently_playing"] is JObject current)
                AddQueueTrack(tracks, seenIds, current);

            if (obj["queue"] is JArray queue)
            {
                foreach (JToken t in queue)
                {
                    if (tracks.Count >= maxTracks) break;
                    if (t is JObject qt) AddQueueTrack(tracks, seenIds, qt);
                }
            }

            return tracks;
        }

        private static void AddQueueTrack(List<PlaylistTrackInfo> tracks, HashSet<string> seenIds, JObject track)
        {
            // ข้าม episode/podcast - เอาเฉพาะ track ("type" อาจไม่มากับ response เสมอ เลยเช็คแบบยอมรับ null)
            string type = (string)track["type"];
            if (type != null && type != "track") return;

            // เพลงซ้ำจากการวนคิว (repeat) - ข้าม เพลงที่ไม่มี id (local file) ปล่อยผ่านเพราะ dedupe ไม่ได้
            string id = (string)track["id"];
            if (id != null && !seenIds.Add(id)) return;

            JArray artists = track["artists"] as JArray;
            string artist = artists != null && artists.Count > 0
                ? string.Join(", ", artists.Select(a => (string)a["name"]))
                : "Unknown Artist";

            tracks.Add(new PlaylistTrackInfo
            {
                Id = id,
                Title = (string)track["name"],
                Artist = artist,
                DurationMs = (int?)track["duration_ms"] ?? 0,
            });
        }
    }
}
