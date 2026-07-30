// SpotifyGatewayPolicy.cs
// การตัดสินใจล้วนๆ ของ envelope: เจอ response แบบนี้ควรทำอะไรต่อ (retry/รายงาน 429/ตัดจบ)
// และเกณฑ์เลือกรูปจากชุดขนาดที่ Spotify ให้มา - แยกออกจาก SpotifyGateway เพื่อให้ logic
// ที่ subtle ที่สุดของชั้นเน็ตเวิร์กขึ้น test bench ได้ (เดิมทดสอบไม่ได้เลยเพราะพันอยู่กับ
// HttpResponseMessage/JToken ทั้งที่ตัวการตัดสินใจไม่ได้ต้องรู้จักของพวกนั้นเลย)
//
// ไฟล์นี้พึ่งแค่ System เหมือน SpotifyModels - ถูก link-compile เข้า test project
// ตัว SpotifyGateway เหลือหน้าที่ plumbing: ยิง HTTP, แปลง response เป็นคำถาม, ทำตามคำตอบ
using System;
using System.Collections.Generic;

namespace ChillWithYou_SpotifyMod
{
    // สิ่งที่ envelope ควรทำกับ response หนึ่งตัว
    public enum GatewayAction
    {
        Succeed,           // 2xx - ส่งต่อให้ผู้เรียก
        RetryTransientAuth, // 401/403 ทั้งที่ token ยังดี = อาการชั่วคราวฝั่ง Spotify ไม่ใช่ token เสีย
        RateLimited,       // 429 - รายงาน SpotifyRateLimiter แล้วหุบ
        Fail,              // ที่เหลือ - log แล้วคืน null ให้ผู้เรียกตัดสินใจเอง
    }

    public static class SpotifyGatewayPolicy
    {
        // ระยะรอก่อน retry แต่ละครั้งของอาการ 401/403 ชั่วคราว - รวมแล้วไม่เกิน ~2 วิ
        // ถ้ายังไม่ผ่านหลังจากนี้ ปล่อยให้ผู้เรียกไปเริ่มรอบใหม่เองดีกว่าค้างรอต่อ
        public static readonly int[] TransientAuthRetryDelaysMs = { 300, 600, 1000 };

        // เจอ response แบบนี้ + token อยู่ในสภาพนี้ ควรทำอะไรต่อ
        // เช็ค token เสมอ ไม่งั้นเคส token ตายจริงจะโดน retry ฟรีๆ ทุกครั้ง (401 ที่ไม่มีวันหาย)
        // 429 ไม่ใช่เรื่อง auth - ต่อให้ token ดีก็ต้องรายงาน rate limiter ไม่ใช่ retry ถี่ๆ ซ้ำเข้าไป
        public static GatewayAction Classify(int statusCode, bool hasUsableToken)
        {
            if (statusCode >= 200 && statusCode <= 299) return GatewayAction.Succeed;
            if ((statusCode == 401 || statusCode == 403) && hasUsableToken)
                return GatewayAction.RetryTransientAuth;
            if (statusCode == 429) return GatewayAction.RateLimited;
            return GatewayAction.Fail;
        }

        // เลือก URL รูปที่เล็กที่สุดแต่ยังไม่ต่ำกว่า minWidth จากชุด (url, width)
        // (Spotify เรียงใหญ่ -> เล็ก ปกติได้ 640/300/64) - รูปเล็กหน้าแถวกว้างแค่ 32px การหยิบ
        // 640px มาใช้แปลว่าเสีย texture 1.6MB ต่อแถว ซึ่งแพงมากเมื่อผลค้นหามี 20 แถว
        // width เป็น null ได้ (ปก mosaic ของ playlist บางอัน) - ถือว่าใหญ่พอไว้ก่อน
        // คืนรูปที่เล็กที่สุดที่มีเมื่อไม่มีตัวไหนถึง minWidth และคืน null เมื่อไม่มีรูปเลย
        public static string PickImageUrl(IEnumerable<(string Url, int? Width)> images, int minWidth)
        {
            if (images == null) return null;

            string best = null;
            int bestWidth = int.MaxValue;
            string smallest = null;
            int smallestWidth = int.MaxValue;

            foreach ((string url, int? nullableWidth) in images)
            {
                if (string.IsNullOrEmpty(url)) continue;

                int width = nullableWidth ?? int.MaxValue;

                if (width <= smallestWidth) { smallestWidth = width; smallest = url; }
                if (width >= minWidth && width <= bestWidth) { bestWidth = width; best = url; }
            }

            return best ?? smallest;
        }
    }
}
