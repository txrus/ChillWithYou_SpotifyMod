using System;
using ChillWithYou_SpotifyMod;
using Xunit;

namespace ChillWithYou_SpotifyMod.Tests
{
    // ทดสอบนาฬิกา interpolate + สถานะ play/pause ที่แยกออกมาจาก SpotifyButtonInjector
    // ครอบเคสที่เคยมีบั๊กจริงในเซสชันนี้: progress bar ค้าง, play/pause ไม่ re-anchor, เพลงจบไม่ trigger
    public class NowPlayingSessionTests
    {
        // เวลาอ้างอิงคงที่ - เลื่อนเองด้วย .Add ในแต่ละเทสต์ ไม่พึ่ง DateTime.UtcNow จริง
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Dur = TimeSpan.FromSeconds(200);

        [Fact]
        public void New_session_is_inactive_and_paused()
        {
            var s = new NowPlayingSession();
            Assert.False(s.IsActive);
            Assert.False(s.IsPlaying);
        }

        [Fact]
        public void Sync_activates_and_ticks_from_synced_position_immediately()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(50), Dur, isPlaying: true, T0);

            Assert.True(s.IsActive);
            Assert.True(s.IsPlaying);

            PlaybackFrame f = s.Tick(T0);
            Assert.Equal(TimeSpan.FromSeconds(50), f.Position);
            Assert.Equal(Dur, f.Duration);
            Assert.Equal(0.25, f.Fraction, 4); // 50/200
            Assert.False(f.ReachedEnd);
        }

        [Fact]
        public void Playing_advances_position_by_real_elapsed_time()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(50), Dur, isPlaying: true, T0);

            PlaybackFrame f = s.Tick(T0.AddSeconds(30));
            Assert.Equal(TimeSpan.FromSeconds(80), f.Position); // 50 + 30
            Assert.Equal(0.40, f.Fraction, 4);                  // 80/200
            Assert.False(f.ReachedEnd);
        }

        [Fact]
        public void Playing_past_duration_clamps_and_reports_reached_end()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(190), Dur, isPlaying: true, T0);

            PlaybackFrame f = s.Tick(T0.AddSeconds(30)); // 190 + 30 = 220 > 200
            Assert.Equal(Dur, f.Position);               // clamp ที่ความยาวเพลง
            Assert.Equal(1.0, f.Fraction, 4);
            Assert.True(f.ReachedEnd);
        }

        [Fact]
        public void Reaching_end_exactly_reports_reached_end()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(180), Dur, isPlaying: true, T0);

            PlaybackFrame f = s.Tick(T0.AddSeconds(20)); // 180 + 20 = 200 == duration
            Assert.Equal(Dur, f.Position);
            Assert.True(f.ReachedEnd); // ใช้ >= จึงนับว่าจบพอดี
        }

        [Fact]
        public void Paused_position_is_frozen_regardless_of_elapsed_time()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(50), Dur, isPlaying: false, T0);

            PlaybackFrame f = s.Tick(T0.AddMinutes(10));
            Assert.Equal(TimeSpan.FromSeconds(50), f.Position); // หยุดอยู่ ไม่เดินตามเวลาจริง
            Assert.False(f.ReachedEnd);
        }

        [Fact]
        public void TogglePlayPause_pausing_captures_elapsed_then_holds_position()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(50), Dur, isPlaying: true, T0);

            // เล่นไป 30 วิ แล้วกด pause ณ วินาทีที่ 30
            s.TogglePlayPause(T0.AddSeconds(30));
            Assert.False(s.IsPlaying);

            // ต่อให้เวลาผ่านไปอีก 100 วิ ตำแหน่งต้องค้างที่ 80 (50 + 30) ไม่เด้งกลับไป 50
            PlaybackFrame f = s.Tick(T0.AddSeconds(130));
            Assert.Equal(TimeSpan.FromSeconds(80), f.Position);
        }

        [Fact]
        public void TogglePlayPause_resuming_continues_from_held_position()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(50), Dur, isPlaying: true, T0);
            s.TogglePlayPause(T0.AddSeconds(30));   // pause ที่ตำแหน่ง 80

            // resume ณ เวลา T0+130 แล้วปล่อยให้เดินต่ออีก 10 วิ
            s.TogglePlayPause(T0.AddSeconds(130));
            Assert.True(s.IsPlaying);

            PlaybackFrame f = s.Tick(T0.AddSeconds(140)); // 80 + 10
            Assert.Equal(TimeSpan.FromSeconds(90), f.Position);
        }

        [Fact]
        public void TogglePlayPause_pause_then_resume_does_not_lose_time_to_wall_clock()
        {
            // จับ regression: pause แล้วรอ 1 ชม. ค่อย resume - ตำแหน่งต้องไม่กระโดดไปตามเวลาจริงที่รอ
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(50), Dur, isPlaying: true, T0);
            s.TogglePlayPause(T0.AddSeconds(30));           // pause ที่ 80
            s.TogglePlayPause(T0.AddHours(1));              // resume หนึ่งชั่วโมงต่อมา
            PlaybackFrame f = s.Tick(T0.AddHours(1).AddSeconds(5)); // เดินต่อแค่ 5 วิ
            Assert.Equal(TimeSpan.FromSeconds(85), f.Position);     // 80 + 5, ไม่ใช่ 80 + 3600
        }

        [Fact]
        public void TogglePlayPause_when_inactive_is_a_noop()
        {
            var s = new NowPlayingSession();
            s.TogglePlayPause(T0); // ยังไม่เคย Sync
            Assert.False(s.IsActive);
            Assert.False(s.IsPlaying);
        }

        [Fact]
        public void Clear_deactivates_and_pauses()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(50), Dur, isPlaying: true, T0);
            s.Clear();
            Assert.False(s.IsActive);
            Assert.False(s.IsPlaying);
        }

        [Fact]
        public void Zero_duration_yields_zero_fraction_and_no_end()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.Zero, TimeSpan.Zero, isPlaying: true, T0);

            PlaybackFrame f = s.Tick(T0.AddSeconds(5));
            Assert.Equal(0f, f.Fraction);
            Assert.False(f.ReachedEnd); // duration ไม่ > 0 จึงไม่นับว่าจบ (กันหารศูนย์)
        }

        [Fact]
        public void Re_sync_after_end_clears_reached_end()
        {
            var s = new NowPlayingSession();
            s.Sync(TimeSpan.FromSeconds(190), Dur, isPlaying: true, T0);
            Assert.True(s.Tick(T0.AddSeconds(30)).ReachedEnd); // จบแล้ว

            // เพลงใหม่มา sync ทับ -> เริ่มนับใหม่ ไม่ค้างสถานะจบ
            s.Sync(TimeSpan.Zero, TimeSpan.FromSeconds(120), isPlaying: true, T0.AddSeconds(30));
            PlaybackFrame f = s.Tick(T0.AddSeconds(35));
            Assert.False(f.ReachedEnd);
            Assert.Equal(TimeSpan.FromSeconds(5), f.Position);
        }
    }
}
