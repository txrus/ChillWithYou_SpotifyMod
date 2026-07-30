// เทสต์ SpotifyGatewayPolicy - การตัดสินใจของ envelope (retry/429/ตัดจบ) + เกณฑ์เลือกรูป
// เดิม logic พวกนี้ทดสอบไม่ได้เลยเพราะพันอยู่กับ HttpResponseMessage/JToken ใน SpotifyGateway
// ทั้งที่เป็นจุดที่ subtle ที่สุดของชั้นเน็ตเวิร์ก (retry ผิดเคสเดียว = ยิงฟรีทุกครั้ง หรือค้างรอเปล่า)
using System.Collections.Generic;
using Xunit;

namespace ChillWithYou_SpotifyMod.Tests
{
    public class SpotifyGatewayPolicyTests
    {
        // === Classify: response แบบนี้ + token สภาพนี้ ควรทำอะไรต่อ ===

        [Theory]
        [InlineData(200)]
        [InlineData(204)] // 204 = ไม่มี active device - สำเร็จ (ฝั่ง parse เป็นคนคืน null เอง)
        public void Classify_2xx_Succeeds(int status)
        {
            Assert.Equal(GatewayAction.Succeed, SpotifyGatewayPolicy.Classify(status, hasUsableToken: true));
            Assert.Equal(GatewayAction.Succeed, SpotifyGatewayPolicy.Classify(status, hasUsableToken: false));
        }

        // Spotify ปฏิเสธเป็นครั้งคราวเหมือนไม่ได้ login ทั้งที่ token ใช้ได้จริง - ควร retry
        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        public void Classify_AuthFailureWithGoodToken_Retries(int status)
        {
            Assert.Equal(GatewayAction.RetryTransientAuth,
                SpotifyGatewayPolicy.Classify(status, hasUsableToken: true));
        }

        // token ตายจริง = 401 ที่ไม่มีวันหาย - retry ไปก็ยิงฟรี ต้องตัดจบให้ผู้เรียกไป login ใหม่
        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        public void Classify_AuthFailureWithDeadToken_FailsImmediately(int status)
        {
            Assert.Equal(GatewayAction.Fail, SpotifyGatewayPolicy.Classify(status, hasUsableToken: false));
        }

        // 429 ไม่ใช่เรื่อง auth - ต่อให้ token ดีก็ต้องรายงาน rate limiter ไม่ใช่ retry ถี่ๆ ซ้ำเข้าไป
        [Fact]
        public void Classify_429_ReportsRateLimitRegardlessOfToken()
        {
            Assert.Equal(GatewayAction.RateLimited, SpotifyGatewayPolicy.Classify(429, hasUsableToken: true));
            Assert.Equal(GatewayAction.RateLimited, SpotifyGatewayPolicy.Classify(429, hasUsableToken: false));
        }

        [Theory]
        [InlineData(400)]
        [InlineData(404)]
        [InlineData(500)]
        [InlineData(502)]
        public void Classify_OtherFailures_Fail(int status)
        {
            Assert.Equal(GatewayAction.Fail, SpotifyGatewayPolicy.Classify(status, hasUsableToken: true));
        }

        // ~2 วิรวม แล้วเลิก - ตารางนี้คือสัญญาเรื่อง "ค้างรอนานสุดเท่าไหร่" ของทุก request
        [Fact]
        public void RetryLadder_ThreeStepsUnderTwoSeconds()
        {
            Assert.Equal(new[] { 300, 600, 1000 }, SpotifyGatewayPolicy.TransientAuthRetryDelaysMs);
        }

        // === PickImageUrl: เลือกรูปเล็กสุดที่ยังไม่ต่ำกว่า minWidth ===

        private static List<(string Url, int? Width)> Images(params (string, int?)[] items) =>
            new List<(string Url, int? Width)>(items);

        [Fact]
        public void Pick_SmallestThatStillMeetsMinWidth()
        {
            var images = Images(("big", 640), ("mid", 300), ("small", 64));

            Assert.Equal("mid", SpotifyGatewayPolicy.PickImageUrl(images, minWidth: 160));
            Assert.Equal("small", SpotifyGatewayPolicy.PickImageUrl(images, minWidth: 64));
        }

        // ไม่มีตัวไหนถึง minWidth -> เอาตัวเล็กสุดที่มี (ภาพเบลอดีกว่าไม่มีภาพ)
        [Fact]
        public void Pick_FallsBackToSmallestWhenNoneReachMinWidth()
        {
            Assert.Equal("small", SpotifyGatewayPolicy.PickImageUrl(
                Images(("big", 100), ("small", 50)), minWidth: 300));
        }

        // width null (ปก mosaic ของ playlist บางอัน) = ถือว่าใหญ่พอไว้ก่อน
        [Fact]
        public void Pick_NullWidthCountsAsLargeEnough()
        {
            Assert.Equal("mosaic", SpotifyGatewayPolicy.PickImageUrl(
                Images(("mosaic", null)), minWidth: 160));
        }

        [Fact]
        public void Pick_SkipsEmptyUrlsAndHandlesEmptyList()
        {
            Assert.Null(SpotifyGatewayPolicy.PickImageUrl(Images(), minWidth: 64));
            Assert.Null(SpotifyGatewayPolicy.PickImageUrl(null, minWidth: 64));
            Assert.Equal("ok", SpotifyGatewayPolicy.PickImageUrl(
                Images((null, 640), ("", 300), ("ok", 64)), minWidth: 64));
        }
    }
}
