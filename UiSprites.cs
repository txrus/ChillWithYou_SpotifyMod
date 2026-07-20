// UiSprites.cs
// สร้าง sprite รูปทรงพื้นฐาน (วงกลม/วงแหวน/pill/มุมโค้ง) จากโค้ดล้วนด้วย signed distance function
// เพื่อให้ UI ของ mod ใช้ทรงโค้งแบบเดียวกับ UI ของเกมได้โดยไม่ต้องแนบไฟล์ asset ไปกับ DLL
// ทุกตัวเป็นสีขาว ให้ tint สีจริงผ่าน Image.color ฝั่งผู้ใช้งาน
using UnityEngine;

namespace ChillWithYou_SpotifyMod
{
    internal static class UiSprites
    {
        private static Sprite _circle;
        private static Sprite _ring;
        private static Sprite _pill;
        private static Sprite _pillOutline;
        private static Sprite _bar;
        private static Sprite _rounded;
        private static Sprite _roundedOutline;

        // วงกลมทึบ (ปุ่ม play/pause หลัก + fill ตอนกดของปุ่มวงแหวน)
        public static Sprite Circle => _circle != null ? _circle
            : (_circle = Make(64, 64, 31.5f, 0f, Vector4.zero));

        // วงแหวนขอบ ~2px เมื่อแสดงที่ 30-36px (ปุ่ม prev/next/refresh)
        public static Sprite Ring => _ring != null ? _ring
            : (_ring = Make(64, 64, 31.5f, 4f, Vector4.zero));

        // pill สูง 30px ทึบ - 9-slice ยืดแนวนอนอย่างเดียว ใช้กับปุ่มข้อความ/ช่อง search
        // (ผู้ใช้ต้องล็อกความสูงที่ 30 ด้วย LayoutElement เพื่อให้มุมโค้งไม่เพี้ยน)
        public static Sprite Pill => _pill != null ? _pill
            : (_pill = Make(64, 30, 14.5f, 0f, new Vector4(15f, 0f, 15f, 0f)));

        // pill สูง 30px แบบขอบ 2px พื้นใส (ปุ่มรอง เช่น Search / My Lists)
        public static Sprite PillOutline => _pillOutline != null ? _pillOutline
            : (_pillOutline = Make(64, 30, 14.5f, 2f, new Vector4(15f, 0f, 15f, 0f)));

        // แถบสูง 6px หัวท้ายมน (progress bar)
        public static Sprite Bar => _bar != null ? _bar
            : (_bar = Make(16, 6, 2.5f, 0f, new Vector4(3f, 0f, 3f, 0f)));

        // สี่เหลี่ยมมุมโค้ง 6px ทึบ - ใช้เป็นพื้น hover ของแถวรายการเพลง
        public static Sprite Rounded => _rounded != null ? _rounded
            : (_rounded = Make(24, 24, 6f, 0f, new Vector4(8f, 8f, 8f, 8f)));

        // กรอบมุมโค้ง 6px ขอบ 2px - ใช้ตีกรอบปกอัลบั้ม/ปก playlist
        public static Sprite RoundedOutline => _roundedOutline != null ? _roundedOutline
            : (_roundedOutline = Make(24, 24, 6f, 2f, new Vector4(8f, 8f, 8f, 8f)));

        // stroke = 0 ได้ทรงทึบ, stroke > 0 ได้เฉพาะเส้นขอบหนา stroke px (วัดเข้าด้านใน)
        private static Sprite Make(int w, int h, float radius, float stroke, Vector4 border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float cx = w * 0.5f, cy = h * 0.5f;
            float hw = cx - 0.5f, hh = cy - 0.5f; // กันขอบ 0.5px ให้ AA มีที่จาง

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // SDF ของ rounded rect: ระยะจากขอบทรง (ติดลบ = อยู่ข้างใน)
                    float px = Mathf.Abs(x + 0.5f - cx) - (hw - radius);
                    float py = Mathf.Abs(y + 0.5f - cy) - (hh - radius);
                    float d = Mathf.Sqrt(Mathf.Max(px, 0f) * Mathf.Max(px, 0f)
                                       + Mathf.Max(py, 0f) * Mathf.Max(py, 0f)) - radius;

                    float a = stroke > 0f
                        ? Mathf.Clamp01(stroke * 0.5f - Mathf.Abs(d + stroke * 0.5f) + 0.5f)
                        : Mathf.Clamp01(0.5f - d);

                    pixels[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f, extrude: 0, meshType: SpriteMeshType.FullRect, border: border);
        }
    }
}
