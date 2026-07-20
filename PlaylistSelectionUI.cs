using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ChillWithYou_SpotifyMod
{
    public class PlaylistSelectionUI : MonoBehaviour
    {
        [Header("UI References")]
        public Transform playlistContentPanel;
        public GameObject playlistButtonTemplate;
        public PlaylistUIController trackController;

        [Header("Manual Refresh")]
        // ตัวแปรสำหรับผูกปุ่ม Refresh หากคุณต้องการผูกผ่านโค้ด
        public Button refreshButton;

        private List<GameObject> _spawnedPlaylists = new List<GameObject>();

        private void Awake()
        {
            // หากมีการกำหนดปุ่มผ่าน Inspector ระบบจะผูกคำสั่งคลิกให้ทันทีเมื่อเกมเริ่ม
            if (refreshButton != null)
            {
                refreshButton.onClick.AddListener(FetchPlaylistsManually);
            }
        }

        // ==========================================
        // 1. ฟังก์ชันสำหรับให้ปุ่มเรียกใช้งาน
        // ==========================================
        public void FetchPlaylistsManually()
        {
            Plugin.Log.LogInfo("[UI-Playlist] ผู้เล่นกดปุ่มโหลด Playlist เริ่มตรวจสอบ Token...");

            if (SpotifyAuth.IsLoggedIn)
            {
                LoadPlaylists();
            }
            else
            {
                Plugin.Log.LogWarning("[UI-Playlist] ยังไม่ได้ Login! ไม่สามารถดึง Playlist ได้");
            }
        }

        // ==========================================
        // 2. กระบวนการดึงข้อมูลและแสดงผล
        // ==========================================
        private async void LoadPlaylists()
        {
            string token = SpotifyAuth.AccessToken;

            if (string.IsNullOrEmpty(token))
            {
                Plugin.Log.LogWarning("[UI-Playlist] Access Token ว่างเปล่า โปรด Connect Spotify ก่อน");
                return;
            }

            Plugin.Log.LogInfo("[UI-Playlist] กำลังเรียกขอข้อมูล Playlist จาก Spotify API...");

            // ดึงข้อมูล Playlist ล่าสุด
            List<PlaylistInfo> playlists = await SpotifyApiClient.GetMyPlaylistsAsync(token);
            RenderUserPlaylists(playlists);
        }

        public void RenderUserPlaylists(List<PlaylistInfo> playlists)
        {
            if (playlistContentPanel == null || playlistButtonTemplate == null) return;

            // ลบปุ่มเก่าออกก่อนสร้างใหม่ เพื่อไม่ให้ข้อมูลซ้ำซ้อน
            foreach (var btn in _spawnedPlaylists) Destroy(btn);
            _spawnedPlaylists.Clear();

            if (playlists == null || playlists.Count == 0)
            {
                Plugin.Log.LogInfo("[UI-Playlist] ไม่มี Playlist ให้แสดงผล");
                return;
            }

            // สร้างปุ่ม Playlist ทีละปุ่มตามข้อมูลที่ได้รับ
            foreach (var pl in playlists)
            {
                GameObject newBtn = Instantiate(playlistButtonTemplate, playlistContentPanel);
                newBtn.SetActive(true);
                _spawnedPlaylists.Add(newBtn);

                Text btnText = newBtn.GetComponentInChildren<Text>();
                if (btnText != null) btnText.text = pl.Name;

                Button btn = newBtn.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OnPlaylistSelected(pl));
                }
            }
            Plugin.Log.LogInfo($"[UI-Playlist] สร้างปุ่มสำเร็จจำนวน {playlists.Count} รายการ");
        }

        // ==========================================
        // 3. เมื่อผู้เล่นคลิกเลือก Playlist ใดๆ
        // ==========================================
        private void OnPlaylistSelected(PlaylistInfo playlist)
        {
            Plugin.Log.LogInfo($"[UI] เลือก Playlist: {playlist.Name}");

            Task.Run(async () =>
            {
                List<TrackInfo> tracks = await SpotifyApiClient.GetPlaylistTracksAsync(playlist.Id, SpotifyAuth.AccessToken);

                MainThreadDispatcher.Enqueue(() =>
                {
                    trackController?.RenderPlaylist(tracks);
                });
            });
        }
    }
}