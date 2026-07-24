// SpotifyUiKit.cs
// ชุดประกอบ widget ตามภาษาดีไซน์ของเกม (แผงดำโปร่งแสง + ขอบ/เส้นขาว + ตัวหนังสือขาวสามระดับ)
// แยกออกมาจาก SpotifyButtonInjector: ไฟล์นี้รู้แค่ "หน้าตา" (สี ฟอนต์ ทรงปุ่ม) ไม่รู้จัก Spotify เลย
// ผู้เรียกผูก onClick/listener เองทั้งหมด - ทุกเมธอดในนี้แค่สร้าง GameObject แล้วคืน component
using System;
using UnityEngine;
using UnityEngine.UI;

namespace ChillWithYou_SpotifyMod
{
    internal static class SpotifyUiKit
    {
        // === โทนสี ===
        public static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.055f, 0.55f);
        public static readonly Color LineColor = new Color(1f, 1f, 1f, 0.85f);     // ขอบหลัก (ปุ่ม/กรอบปก)
        public static readonly Color LineSoft = new Color(1f, 1f, 1f, 0.32f);      // เส้นคั่น/ขอบรอง
        public static readonly Color TextSecondary = new Color(1f, 1f, 1f, 0.65f);
        public static readonly Color TextFaint = new Color(1f, 1f, 1f, 0.45f);
        // เขียว Spotify ใช้เชิงความหมายเท่านั้น: progress fill, เพลงที่กำลังเล่น, ปุ่ม Connect
        public static readonly Color ButtonActive = new Color(0.11f, 0.73f, 0.33f, 1f);
        public static readonly Color CoverPlaceholder = new Color(0.16f, 0.15f, 0.20f, 1f);

        // ฟอนต์ที่ทุก widget ใช้ - ตั้งครั้งเดียวตอน inject ผ่าน ResolveFont()
        private static Font _font;

        // พยายามใช้ฟอนต์ UI ของเกมเองให้ section กลมกลืน - หาไม่ได้ค่อย fallback เป็น Arial
        // รับเฉพาะ dynamic font ที่มีตัวอักษรพื้นฐานครบ กันไปหยิบ icon font แล้วตัวหนังสือพัง
        public static void ResolveFont()
        {
            try
            {
                foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
                {
                    if (f == null || !f.dynamic) continue;
                    string n = f.name.ToLowerInvariant();
                    if (n.Contains("arial")) continue;
                    if (!f.HasCharacter('A') || !f.HasCharacter('g') || !f.HasCharacter('0') || !f.HasCharacter(':'))
                        continue;
                    Plugin.Log.LogInfo($"[SpotifyUiKit] ใช้ฟอนต์ของเกม: {f.name}");
                    _font = f;
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SpotifyUiKit] หาฟอนต์ของเกมไม่สำเร็จ: {ex.Message}");
            }
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        public static Text CreateText(Transform parent, string content, int fontSize, TextAnchor anchor)
        {
            GameObject go = new GameObject("Text");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize + 6;
            le.minHeight = fontSize + 6;

            Text text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.font = _font;
            text.raycastTarget = false;
            // Unity Text ซ่อนทั้งบรรทัดถ้าความสูง rect ไม่พอ (default = Truncate) - ฟอนต์ IBM Plex ของเกม
            // สูงกว่า Arial เล็กน้อย ทำให้ title 12pt ในแถวคิว 2 บรรทัดที่ถูกบีบหลุด threshold แล้วหายทั้งบรรทัด
            // ตั้ง Overflow ให้วาดตัวหนังสือเสมอแม้ rect เตี้ยไปนิด ดีกว่าหายไปเฉยๆ
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        public static Text CreateInlineText(Transform parent, string content, float width)
        {
            GameObject go = new GameObject("Time");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            Text text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = 11;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = TextSecondary;
            text.font = _font;
            text.raycastTarget = false;
            return text;
        }

        public static void CreateSectionLabel(Transform parent, string label)
        {
            Text t = CreateText(parent, label.ToUpper(), 10, TextAnchor.MiddleLeft);
            t.color = TextFaint;
        }

        // แถวในลิสต์ (คิวเพลง / ผลค้นหา / My Lists) ต้องเป็นบรรทัดเดียวเสมอ:
        // ชื่อเพลงยาวๆ ให้ตัดที่ขอบคอลัมน์ด้วย RectMask2D แทนการ wrap ลงบรรทัดใหม่ ซึ่งจะดันแถว
        // ให้สูงเกิน preferredHeight แล้วไปทับแถว/ส่วนอื่นด้านล่าง (ปัญหา UI ซ้อนทับ)
        public static void ClipRowToSingleLine(GameObject col, params Text[] lines)
        {
            col.AddComponent<RectMask2D>();
            foreach (Text t in lines)
                if (t != null) t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        // ปุ่มวงกลมภาษาเดียวกับปุ่มไอคอนของเกม:
        // solid = วงกลมขาวทึบ + glyph เข้ม (ปุ่มหลักอย่าง play/pause กลาง transport)
        // ไม่ solid = วงแหวนขอบขาว พื้นใส ชี้/กดแล้วมีวงกลมขาวจางโผล่ข้างใน
        public static Button CreateCircleButton(Transform parent, string label, float size, bool solid, Color? ringColor = null)
        {
            GameObject go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.minWidth = size;
            le.preferredHeight = size; le.minHeight = size;

            Image shape = go.AddComponent<Image>();
            shape.sprite = solid ? UiSprites.Circle : UiSprites.Ring;
            shape.preserveAspect = true;
            shape.color = solid ? Color.white : (ringColor ?? LineColor);

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            if (solid)
            {
                btn.targetGraphic = shape;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
                cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
                cb.selectedColor = Color.white; // default 0.96 ขาวเกือบทึบ - ถ้าไม่ตั้งปุ่มจะติดสว่างค้างหลังกด
            }
            else
            {
                // วงแหวนคงที่ตลอด ตอนกดมีวงกลมขาววาบขึ้นหนึ่งจังหวะแล้วดับ (selected โปร่งใส เลยไม่ติดค้าง)
                GameObject pressGo = new GameObject("PressFill");
                pressGo.transform.SetParent(go.transform, worldPositionStays: false);
                RectTransform pressRt = pressGo.AddComponent<RectTransform>();
                pressRt.anchorMin = Vector2.zero; pressRt.anchorMax = Vector2.one; pressRt.sizeDelta = Vector2.zero;
                Image press = pressGo.AddComponent<Image>();
                press.sprite = UiSprites.Circle;
                press.preserveAspect = true;
                press.raycastTarget = false;
                btn.targetGraphic = press;
                cb.normalColor = new Color(1f, 1f, 1f, 0f);
                cb.highlightedColor = new Color(1f, 1f, 1f, 0.10f);
                cb.pressedColor = new Color(1f, 1f, 1f, 0.45f); // แสงวาบยืนยันว่ากดติด
                cb.selectedColor = new Color(1f, 1f, 1f, 0f); // กลับหายทันทีหลังปล่อย ไม่ติดค้าง
            }
            btn.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;

            Text text = labelGo.AddComponent<Text>();
            text.text = label;
            text.fontSize = Mathf.RoundToInt(size * 0.36f);
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = solid ? new Color(0.05f, 0.05f, 0.09f, 1f) : Color.white;
            text.font = _font;
            text.raycastTarget = false;

            return btn;
        }

        // ปุ่ม pill ตามภาษาเกม: filled = พื้นทึบสี accent + ตัวหนังสือเข้ม (ปุ่มหลัก เช่น Connect เขียว Spotify)
        // outline = ขอบขาวพื้นใส + ตัวหนังสือขาว (ปุ่มรอง เช่น Search / My Lists)
        public static Button CreatePillButton(Transform parent, string label, bool filled, Color accent, float height = 30f)
        {
            GameObject go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            Image img = go.AddComponent<Image>();
            img.sprite = filled ? UiSprites.Pill : UiSprites.PillOutline;
            img.type = Image.Type.Sliced;
            img.color = filled ? accent : LineColor;

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            if (filled)
            {
                // สีจริงมาจาก img.color แล้วโดน ColorBlock คูณทับ - ขาวล้วน = สีเดิม, เทา = เข้มลงตอนกด
                btn.targetGraphic = img;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(0.93f, 0.93f, 0.93f, 1f);
                cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
                cb.selectedColor = Color.white; // default 0.96 - กันปุ่มติดสีค้างหลังกด
            }
            else
            {
                GameObject pressGo = new GameObject("PressFill");
                pressGo.transform.SetParent(go.transform, worldPositionStays: false);
                RectTransform pressRt = pressGo.AddComponent<RectTransform>();
                pressRt.anchorMin = Vector2.zero; pressRt.anchorMax = Vector2.one; pressRt.sizeDelta = Vector2.zero;
                Image press = pressGo.AddComponent<Image>();
                press.sprite = UiSprites.Pill;
                press.type = Image.Type.Sliced;
                press.raycastTarget = false;
                btn.targetGraphic = press;
                cb.normalColor = new Color(1f, 1f, 1f, 0f);
                cb.highlightedColor = new Color(1f, 1f, 1f, 0.10f);
                cb.pressedColor = new Color(1f, 1f, 1f, 0.45f); // แสงวาบยืนยันว่ากดติด
                cb.selectedColor = new Color(1f, 1f, 1f, 0f);
            }
            btn.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;

            Text text = labelGo.AddComponent<Text>();
            text.text = label;
            text.fontSize = 12;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = filled ? new Color(0.02f, 0.09f, 0.045f, 1f) : Color.white;
            text.font = _font;
            text.raycastTarget = false;

            return btn;
        }

        public static Slider CreateProgressSlider(Transform parent)
        {
            GameObject go = new GameObject("ProgressSlider");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 8f;

            Slider slider = go.AddComponent<Slider>();
            slider.interactable = false; // แค่แสดงผล ไม่ให้ user ลากเปลี่ยนตำแหน่งเพลง (Spotify API ตัวนี้ยังไม่รองรับ seek)
            slider.transition = Selectable.Transition.None;

            // แถบจริงสูง 6px หัวท้ายมน วางกึ่งกลางแนวตั้งของพื้นที่ slider
            GameObject bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f); bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, 6f);
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = UiSprites.Bar;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(1f, 1f, 1f, 0.22f);

            GameObject fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f); fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.sizeDelta = new Vector2(0f, 6f);

            GameObject fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillArea.transform, worldPositionStays: false);
            RectTransform fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f); fillRt.anchorMax = new Vector2(0f, 1f); fillRt.sizeDelta = new Vector2(10f, 0f);
            Image fillImg = fillGo.AddComponent<Image>();
            fillImg.sprite = UiSprites.Bar;
            fillImg.type = Image.Type.Sliced;
            fillImg.color = ButtonActive;

            slider.fillRect = fillRt;
            slider.targetGraphic = fillImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0f;

            return slider;
        }

        // ช่องค้นหาทรง pill พื้นขาวจางแบบ input ของเกม - listener (Enter/ล้างคำค้น) เป็นเรื่องของผู้เรียก
        public static InputField CreateSearchInputField(Transform parent, string placeholderText)
        {
            GameObject go = new GameObject("SearchInput");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 30f;
            le.minHeight = 30f;

            Image bg = go.AddComponent<Image>();
            bg.sprite = UiSprites.Pill;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.12f);

            // viewport กัน text ล้นขอบ
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(12f, 2f);
            vpRt.offsetMax = new Vector2(-12f, -2f);
            viewport.AddComponent<RectMask2D>();

            // placeholder
            GameObject placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(viewport.transform, worldPositionStays: false);
            RectTransform phRt = placeholderGo.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.sizeDelta = Vector2.zero;
            Text placeholder = placeholderGo.AddComponent<Text>();
            placeholder.text = placeholderText;
            placeholder.fontSize = 11;
            placeholder.color = TextFaint;
            placeholder.font = _font;
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.raycastTarget = false;

            // text
            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(viewport.transform, worldPositionStays: false);
            RectTransform textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            Text inputText = textGo.AddComponent<Text>();
            inputText.fontSize = 12;
            inputText.color = Color.white;
            inputText.font = _font;
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;
            inputText.raycastTarget = false;

            InputField field = go.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.textComponent = inputText;
            field.placeholder = placeholder;
            field.caretWidth = 2;
            field.caretColor = Color.white;
            field.selectionColor = new Color(ButtonActive.r, ButtonActive.g, ButtonActive.b, 0.4f); // SpotifyGreen โปร่งแสง

            return field;
        }
    }
}
