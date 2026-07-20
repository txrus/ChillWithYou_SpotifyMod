// SpotifyRateLimiter.cs
// ตัวจับสถานะ "โดน 429 อยู่ไหม" แบบ shared ระหว่าง SpotifyApi.cs (SMTC เดิม/Web API เล่นเพลง)
// และ SpotifyWebApi.cs (playlist) เพราะทั้งคู่ยิงไปที่ account เดียวกัน โดน limit พร้อมกันได้
using System;
using System.Net.Http;

namespace ChillWithYou_SpotifyMod
{
    internal static class SpotifyRateLimiter
    {
        private static DateTime _blockedUntilUtc = DateTime.MinValue;

        public static bool IsBlocked => DateTime.UtcNow < _blockedUntilUtc;

        public static TimeSpan RemainingBlock =>
            IsBlocked ? _blockedUntilUtc - DateTime.UtcNow : TimeSpan.Zero;

        // เรียกตอนเจอ 429 - อ่าน Retry-After จาก response ถ้ามี ไม่มีก็ fallback เป็น 5 วิ
        public static void ReportTooManyRequests(HttpResponseMessage resp)
        {
            int retryAfterSeconds = 5; // fallback เผื่อ Spotify ไม่ส่ง header มาด้วย (ไม่ควรเกิดขึ้นปกติ)

            if (resp.Headers.RetryAfter != null)
            {
                if (resp.Headers.RetryAfter.Delta.HasValue)
                    retryAfterSeconds = (int)resp.Headers.RetryAfter.Delta.Value.TotalSeconds;
                else if (resp.Headers.RetryAfter.Date.HasValue)
                    retryAfterSeconds = (int)(resp.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
            }

            retryAfterSeconds = Math.Max(retryAfterSeconds, 1);
            _blockedUntilUtc = DateTime.UtcNow.AddSeconds(retryAfterSeconds);
            Plugin.Log.LogWarning($"[SpotifyRateLimiter] โดน 429 - Spotify บอกให้รอ {retryAfterSeconds} วิ (บล็อกถึง {_blockedUntilUtc:HH:mm:ss} UTC)");
        }
    }
}