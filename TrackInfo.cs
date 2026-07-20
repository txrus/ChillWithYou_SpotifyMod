using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; // จำเป็นสำหรับการจัดการ Text และ Button ของ Unity UI

namespace ChillWithYou_SpotifyMod
{
    // 1. โครงสร้างข้อมูลเพลง (คัมภีร์ข้อมูล)
    public class TrackInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Artist { get; set; }
    }

    public class PlaylistUIController : MonoBehaviour
    {
        // ในการทำ Mod เรามักจะต้องหา GameObject เหล่านี้ผ่าน GameObject.Find() หรืออ้างอิงจากโค้ดเกม
        public Transform contentPanel; // ตำแหน่ง Parent ที่เราจะเอาปุ่มไปใส่ (เช่น ภายใน ScrollView)
        public GameObject trackButtonTemplate; // แม่แบบปุ่มที่เราจะโคลน

        // เก็บรายชื่อปุ่มที่สร้างไว้ เพื่อลบทำความสะอาดเวลาเปลี่ยน Playlist
        private List<GameObject> _spawnedButtons = new List<GameObject>();

        // 2. กระบวนท่าหลัก: สร้างปุ่มเพลงทั้งหมด
        public void RenderPlaylist(List<TrackInfo> tracks)
        {
            if (trackButtonTemplate == null || contentPanel == null)
            {
                Plugin.Log.LogError("[PlaylistUI] ล้มเหลว! ขาด Template หรือ Panel สำหรับสร้าง UI");
                return;
            }

            // ทำความสะอาดค่ายกลเดิมก่อน (ทำลายปุ่มเก่าทิ้งทั้งหมด)
            ClearOldTracks();

            foreach (var track in tracks)
            {
                // สร้างปุ่มใหม่โดยการโคลนแม่แบบ และนำไปเกาะไว้ที่ contentPanel
                GameObject newBtnGo = Instantiate(trackButtonTemplate, contentPanel);
                newBtnGo.SetActive(true); // เผื่อแม่แบบถูกซ่อนไว้
                _spawnedButtons.Add(newBtnGo);

                // เปลี่ยนข้อความบนปุ่ม (สมมติว่าปุ่มมี Text component เป็นลูกอยู่)
                Text btnText = newBtnGo.GetComponentInChildren<Text>();
                if (btnText != null)
                {
                    btnText.text = $"{track.Name} - {track.Artist}";
                }

                // 3. ผูกลมปราณ (Event) เมื่อปุ่มถูกคลิก
                Button btn = newBtnGo.GetComponent<Button>();
                if (btn != null)
                {
                    // ล้าง Event เก่าที่อาจติดมาจาก Template ก่อน
                    btn.onClick.RemoveAllListeners();

                    // ใช้ Lambda expression ฝังข้อมูลของเพลงนี้เข้าไปในฟังก์ชัน
                    btn.onClick.AddListener(() => OnTrackClicked(track));
                }
            }

            Plugin.Log.LogInfo($"[PlaylistUI] สร้างรายการเพลงสำเร็จจำนวน {tracks.Count} เพลง");
        }

        private void ClearOldTracks()
        {
            foreach (var btnGo in _spawnedButtons)
            {
                Destroy(btnGo); // สั่งทำลาย GameObject ทิ้ง
            }
            _spawnedButtons.Clear();
        }

        // 4. กระบวนท่าตอบสนองเมื่อถูกคลิก
        private void OnTrackClicked(TrackInfo track)
        {
            Plugin.Log.LogInfo($"[PlaylistUI] ท่านจอมยุทธ์เลือกเล่นเพลง: {track.Name}");

            // โยนงานกลับไปที่ Dispatcher อมตะของเราที่เราสร้างไว้เมื่อรอบที่แล้ว เพื่อความปลอดภัย
            MainThreadDispatcher.Enqueue(() =>
            {
                PlaySpotifyTrack(track.Id);
            });
        }

        private void PlaySpotifyTrack(string trackId)
        {
            // === นำโค้ดเรียก Spotify API (เช่น ส่งคำสั่ง Play พร้อม URI) มาใส่ตรงนี้ ===
            Plugin.Log.LogInfo($"[Spotify API] กำลังส่งสัญญาณ Play URI: spotify:track:{trackId} ...");
        }
    }
}