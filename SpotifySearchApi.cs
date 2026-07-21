// SpotifySearchApi.cs
// ค้นหาผ่าน GET /v1/search รองรับ 4 ประเภท: track, artist, album, playlist
// อ้างอิง: https://developer.spotify.com/documentation/web-api/reference/search
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChillWithYou_SpotifyMod
{
    public class SearchTrackResult
    {
        public string Id;
        public string Title;
        public string Artist;
        public int DurationMs;
        public string AlbumCoverUrl;
    }

    public class SearchArtistResult
    {
        public string Id;
        public string Name;
        public string ImageUrl;
    }

    public class SearchAlbumResult
    {
        public string Id;
        public string Name;
        public string ArtistName;
        public string CoverUrl;
    }

    public class SearchPlaylistResult
    {
        public string Id;
        public string Name;
        public string OwnerName;
        public string CoverUrl;
    }

    public class SpotifySearchResults
    {
        public List<SearchTrackResult> Tracks = new List<SearchTrackResult>();
        public List<SearchArtistResult> Artists = new List<SearchArtistResult>();
        public List<SearchAlbumResult> Albums = new List<SearchAlbumResult>();
        public List<SearchPlaylistResult> Playlists = new List<SearchPlaylistResult>();
    }

    internal static class SpotifySearchApi
    {
        private static readonly HttpClient Http = new HttpClient();

        private static async Task<bool> EnsureValidTokenAsync()
        {
            if (DateTime.UtcNow < SpotifyAuth.TokenExpiresAt)
                return true;

            try
            {
                await SpotifyAuth.RefreshAccessToken();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // limitPerType: จำนวนผลลัพธ์สูงสุดต่อประเภท - เพดานของ Development Mode ลดจาก 50 เหลือ 10
        // ตั้งแต่ Spotify Web API รอบ ก.พ. 2026 (ค่าที่ใช้จริงคือ 5 เลยยังไม่กระทบ)
        public static async Task<SpotifySearchResults> SearchAsync(string query, int limitPerType = 5)
        {
            var results = new SpotifySearchResults();

            if (string.IsNullOrWhiteSpace(query))
                return results;

            if (SpotifyRateLimiter.IsBlocked)
            {
                Plugin.Log.LogInfo($"[SpotifySearchApi] ข้าม Search: ยังโดน rate limit อยู่อีก {SpotifyRateLimiter.RemainingBlock.TotalSeconds:F0} วิ");
                return results;
            }

            if (!await EnsureValidTokenAsync())
            {
                Plugin.Log.LogWarning("[SpotifySearchApi] Search: not logged in");
                return results;
            }

            try
            {
                string encodedQuery = Uri.EscapeDataString(query);
                string url = $"https://api.spotify.com/v1/search?q={encodedQuery}&type=track,artist,album,playlist&limit={limitPerType}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"Bearer {SpotifyAuth.AccessToken}");
                HttpResponseMessage resp = await Http.SendAsync(request);

                if (resp.StatusCode == (System.Net.HttpStatusCode)429)
                {
                    SpotifyRateLimiter.ReportTooManyRequests(resp);
                    return results;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    string errBody = await resp.Content.ReadAsStringAsync();
                    Plugin.Log.LogWarning($"[SpotifySearchApi] Search failed: {resp.StatusCode} - {errBody}");
                    return results;
                }

                string json = await resp.Content.ReadAsStringAsync();
                JObject obj = JObject.Parse(json);

                // --- Tracks ---
                if (obj["tracks"]?["items"] is JArray trackItems)
                {
                    foreach (JToken t in trackItems)
                    {
                        if (!(t is JObject track)) continue;
                        JArray artists = track["artists"] as JArray;
                        string artist = artists != null && artists.Count > 0
                            ? string.Join(", ", artists.Select(a => (string)a["name"]))
                            : "Unknown Artist";

                        results.Tracks.Add(new SearchTrackResult
                        {
                            Id = (string)track["id"],
                            Title = (string)track["name"],
                            Artist = artist,
                            DurationMs = (int?)track["duration_ms"] ?? 0,
                            AlbumCoverUrl = track["album"]?["images"] is JArray trackImages && trackImages.Count > 0
                                ? trackImages[0]?.Value<string>("url")
                                : null,
                        });
                    }
                }

                // --- Artists ---
                if (obj["artists"]?["items"] is JArray artistItems)
                {
                    foreach (JToken t in artistItems)
                    {
                        if (!(t is JObject a)) continue;
                        results.Artists.Add(new SearchArtistResult
                        {
                            Id = (string)a["id"],
                            Name = (string)a["name"],
                            ImageUrl = a["images"] is JArray artistImages && artistImages.Count > 0
                                ? artistImages[0]?.Value<string>("url")
                                : null,
                        });
                    }
                }

                // --- Albums ---
                if (obj["albums"]?["items"] is JArray albumItems)
                {
                    foreach (JToken t in albumItems)
                    {
                        if (!(t is JObject al)) continue;
                        JArray artists = al["artists"] as JArray;
                        string artist = artists != null && artists.Count > 0
                            ? string.Join(", ", artists.Select(a => (string)a["name"]))
                            : "Unknown Artist";

                        results.Albums.Add(new SearchAlbumResult
                        {
                            Id = (string)al["id"],
                            Name = (string)al["name"],
                            ArtistName = artist,
                            CoverUrl = al["images"] is JArray albumImages && albumImages.Count > 0
                                ? albumImages[0]?.Value<string>("url")
                                : null,
                        });
                    }
                }

                // --- Playlists ---
                if (obj["playlists"]?["items"] is JArray playlistItems)
                {
                    foreach (JToken t in playlistItems)
                    {
                        if (!(t is JObject p)) continue; // Spotify บางทีคืน null literal ใน array สำหรับ playlist ที่ถูกลบ/private
                        results.Playlists.Add(new SearchPlaylistResult
                        {
                            Id = (string)p["id"],
                            Name = (string)p["name"],
                            OwnerName = (string)p["owner"]?["display_name"],
                            CoverUrl = p["images"] is JArray playlistImages && playlistImages.Count > 0
                                ? playlistImages[0]?.Value<string>("url")
                                : null,
                        });
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifySearchApi] Search exception: {ex}");
                return results;
            }
        }
    }
}