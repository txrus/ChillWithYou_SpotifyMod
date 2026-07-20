using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace ChillWithYou_SpotifyMod
{
    public static class SpotifyApiClient
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // 🌟 Cache รายชื่อ playlist ทั้งหมดของ user ไว้ ดึงครั้งเดียวจบ ไม่ต้องยิงซ้ำทุกครั้งที่เปิดหน้า browse
        // Spotify limit สูงสุดต่อ request คือ 50 รายการ (ถ้าไม่ระบุ limit จะได้ default แค่ 20)
        // ถ้า user มี playlist เกิน 50 ต้อง paginate ต่อด้วย offset จนกว่าจะครบ
        private static List<PlaylistInfo> _cachedUserPlaylists;

        // ==========================================
        // 1. กระบวนท่าหลัก: ดึงรายชื่อเพลงจาก Playlist
        // ==========================================
        public static async Task<List<TrackInfo>> GetPlaylistTracksAsync(string playlistId, string accessToken)
        {
            List<TrackInfo> trackList = new List<TrackInfo>();
            string url = $"https://api.spotify.com/v1/playlists/{playlistId}/tracks?limit=50";

            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                HttpResponseMessage response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                SpotifyPlaylistResponse playlistData = JsonConvert.DeserializeObject<SpotifyPlaylistResponse>(jsonResponse);

                if (playlistData?.items != null)
                {
                    foreach (var item in playlistData.items)
                    {
                        if (item.track == null) continue;

                        string artistName = item.track.artists != null && item.track.artists.Count > 0
                                            ? item.track.artists[0].name
                                            : "Unknown Artist";

                        trackList.Add(new TrackInfo
                        {
                            Id = item.track.id,
                            Name = item.track.name,
                            Artist = artistName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyAPI] เกิดข้อผิดพลาดในการดึง Playlist: {ex.Message}");
            }

            return trackList;
        }

        // ==========================================
        // 2. ดึง Playlist ทั้งหมดของ user (paginate จนครบ) - ทำครั้งเดียวแล้ว cache ไว้
        // ==========================================
        public static async Task<List<PlaylistInfo>> GetMyPlaylistsAsync(string accessToken, bool forceRefresh = false)
        {
            // ถ้าเคยดึงมาแล้วและไม่ได้บังคับ refresh ใหม่ -> คืนของที่ cache ไว้ทันที ไม่ยิง API เลย
            if (!forceRefresh && _cachedUserPlaylists != null)
            {
                Plugin.Log.LogInfo($"[SpotifyAPI] ใช้ playlist ที่ cache ไว้ ({_cachedUserPlaylists.Count} รายการ) ไม่ยิง API ซ้ำ");
                return _cachedUserPlaylists;
            }

            List<PlaylistInfo> userPlaylists = new List<PlaylistInfo>();
            const int pageSize = 50; // ค่าสูงสุดที่ Spotify อนุญาตต่อ request (default ถ้าไม่ระบุคือ 20)
            int offset = 0;

            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                while (true)
                {
                    string url = $"https://api.spotify.com/v1/me/playlists?limit={pageSize}&offset={offset}";
                    HttpResponseMessage response = await _httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        Plugin.Log.LogError($"[SpotifyAPI] API ตอบกลับมาว่าพลาด! Status: {response.StatusCode}, Content: {errorContent}");
                        break;
                    }

                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    SpotifyUserPlaylistsResponse data = JsonConvert.DeserializeObject<SpotifyUserPlaylistsResponse>(jsonResponse);

                    if (data?.items == null || data.items.Count == 0)
                        break; // ไม่มีข้อมูลเพิ่มแล้ว จบ

                    foreach (var item in data.items)
                    {
                        userPlaylists.Add(new PlaylistInfo
                        {
                            Id = item.id,
                            Name = item.name
                        });
                    }

                    // ถ้าหน้านี้ได้น้อยกว่า pageSize แปลว่าหมดแล้ว ไม่ต้อง fetch หน้าถัดไป
                    if (data.items.Count < pageSize)
                        break;

                    offset += pageSize;
                }

                Plugin.Log.LogInfo($"[SpotifyAPI] ดึง playlist ทั้งหมดสำเร็จ {userPlaylists.Count} รายการ (paginate {offset / pageSize + 1} รอบ)");
                _cachedUserPlaylists = userPlaylists; // เก็บ cache ไว้ใช้ครั้งต่อไปโดยไม่ยิงซ้ำ
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyAPI] ดึงข้อมูลรายชื่อ Playlist ล้มเหลว: {ex.Message}");
            }

            return userPlaylists;
        }
    } // สิ้นสุดขอบเขตของคลาส SpotifyApiClient

    // ==========================================
    // คลาสข้อมูล (Data Models) ทั้งหมด
    // สำหรับรองรับโครงสร้าง JSON ของ Spotify
    // ==========================================

    public class SpotifyPlaylistResponse
    {
        public List<PlaylistItem> items { get; set; }
    }

    public class PlaylistItem
    {
        public SpotifyTrack track { get; set; }
    }

    public class SpotifyTrack
    {
        public string id { get; set; }
        public string name { get; set; }
        public List<SpotifyArtist> artists { get; set; }
    }

    public class SpotifyArtist
    {
        public string name { get; set; }
    }

    public class SpotifyUserPlaylistsResponse
    {
        [JsonProperty("items")]
        public List<SpotifyPlaylist> items { get; set; }
    }

    public class SpotifyPlaylist
    {
        [JsonProperty("id")]
        public string id { get; set; }

        [JsonProperty("name")]
        public string name { get; set; }
    }
}