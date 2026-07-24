// SpotifySearchApi.cs
// ค้นหาผ่าน GET /v1/search รองรับ 4 ประเภท: track, artist, album, playlist
// อ้างอิง: https://developer.spotify.com/documentation/web-api/reference/search
// ยิงผ่าน SpotifyGateway (envelope token/bearer/429/retry รวมอยู่ที่นั่น) เหลือแค่ประกอบ query + parse
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChillWithYou_SpotifyMod
{
    // Search*Result / SpotifySearchResults ย้ายไป SpotifyModels.cs

    internal static class SpotifySearchApi
    {
        // limitPerType: จำนวนผลลัพธ์สูงสุดต่อประเภท - เพดานของ Development Mode ลดจาก 50 เหลือ 10
        // ตั้งแต่ Spotify Web API รอบ ก.พ. 2026 (ค่าที่ใช้จริงคือ 5 เลยยังไม่กระทบ)
        public static async Task<SpotifySearchResults> SearchAsync(string query, int limitPerType = 5)
        {
            var results = new SpotifySearchResults();

            if (string.IsNullOrWhiteSpace(query))
                return results;

            try
            {
                string encodedQuery = Uri.EscapeDataString(query);
                JObject obj = await SpotifyGateway.GetJsonAsync(
                    $"search?q={encodedQuery}&type=track,artist,album,playlist&limit={limitPerType}");
                if (obj == null) return results; // blocked / ยังไม่ login / error - gateway log ให้แล้ว

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