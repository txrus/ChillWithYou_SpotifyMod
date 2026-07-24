// RowThumbnails.cs
// โหลดรูปเล็กหน้าแถวในลิสต์ (ปกอัลบั้ม/ปก playlist/รูปศิลปิน) แบบ async แล้วยัดใส่ Image ให้
// แยกจาก injector เพราะเป็นเรื่องเดียวจบ: "URL -> sprite ที่พร้อมแสดง" พร้อมกฎกันงานซ้ำ 3 ข้อ
//   1) cache ตาม URL ทั้ง session - ค้นคำเดิม/เปิด My Lists ซ้ำไม่โหลดรูปใหม่
//   2) รวมคำขอที่ URL เดียวกันซึ่งยังโหลดไม่เสร็จเข้าด้วยกัน - ยิงเน็ตครั้งเดียวต่อรูป
//   3) จำ URL ที่โหลดพลาด ไม่ต้องลองซ้ำทุกครั้งที่ rebuild ลิสต์
// รูปมาถึงทีหลังเสมอ แถวอาจถูก destroy ไปแล้ว - ทุกจุดที่แตะ Image จึงเช็ค null (Unity overload
// ทำให้ object ที่ถูก destroy แล้วเทียบเท่ากับ null) ก่อนใส่ sprite
// สัญญากับผู้เรียก: Load() เรียกจาก main thread เท่านั้น (ตารางทั้งสามไม่ได้ล็อก - ตัวที่แก้ตาราง
// อีกจุดคือ Complete() ซึ่งวิ่งผ่าน Plugin.RunOnMainThread อยู่แล้ว)
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChillWithYou_SpotifyMod
{
    internal static class RowThumbnails
    {
        // เพดานกัน texture สะสมไม่จำกัดในเซสชันยาวๆ (ค้นหาหลายสิบครั้ง) - ครบแล้วยังโหลดรูปได้
        // ตามปกติ แค่ไม่จำต่อ รูปละ ~16KB (64px) ถึง ~360KB (ปกอัลบั้ม 300px)
        private const int MaxCacheEntries = 96;

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<Image>> _pending = new Dictionary<string, List<Image>>();
        private static readonly HashSet<string> _failed = new HashSet<string>();

        // เรียกจาก main thread ตอนสร้างแถว - คืนทันที รูปจะโผล่เองเมื่อโหลดเสร็จ
        public static void Load(Image target, string url)
        {
            if (target == null || string.IsNullOrEmpty(url)) return;

            if (_cache.TryGetValue(url, out Sprite cached))
            {
                // sprite ที่จำไว้อาจถูก engine เก็บกวาดไปแล้ว (เปลี่ยนฉาก) - ลืมทิ้งแล้วโหลดใหม่
                if (cached != null)
                {
                    ApplyTo(target, cached);
                    return;
                }
                _cache.Remove(url);
            }

            if (_failed.Contains(url)) return;

            // URL นี้มีคนสั่งโหลดไปแล้วและยังไม่เสร็จ - ต่อคิวรอผลชุดเดียวกัน
            if (_pending.TryGetValue(url, out List<Image> waiting))
            {
                waiting.Add(target);
                return;
            }

            _pending[url] = new List<Image> { target };
            FetchAsync(url);
        }

        private static async void FetchAsync(string url)
        {
            byte[] bytes = null;
            try
            {
                bytes = await SpotifyGateway.GetImageAsync(url);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[RowThumbnails] โหลดรูปพลาด: {ex.Message}");
            }

            // Texture2D/Sprite สร้างได้เฉพาะบน main thread
            Plugin.RunOnMainThread(() => Complete(url, bytes));
        }

        private static void Complete(string url, byte[] bytes)
        {
            if (!_pending.TryGetValue(url, out List<Image> targets)) return;
            _pending.Remove(url);

            Sprite sprite = bytes != null && bytes.Length > 0 ? ToSprite(bytes) : null;
            if (sprite == null)
            {
                _failed.Add(url);
                return;
            }

            if (_cache.Count < MaxCacheEntries)
                _cache[url] = sprite;

            foreach (Image target in targets)
                ApplyTo(target, sprite);
        }

        private static Sprite ToSprite(byte[] bytes)
        {
            var tex = new Texture2D(2, 2);
            // markNonReadable: ไม่มีใครอ่าน pixel กลับ ทิ้งสำเนาฝั่ง CPU ได้เลย ประหยัดหน่วยความจำครึ่งนึง
            if (!tex.LoadImage(bytes, markNonReadable: true))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        private static void ApplyTo(Image target, Sprite sprite)
        {
            if (target == null) return; // แถวถูก destroy ไประหว่างรอรูป
            target.sprite = sprite;
            target.color = Color.white; // เลิกโปร่งใส เผยรูปทับ placeholder
        }
    }
}
