// เทสต์ RefreshCoordinator - กฎ orchestration ของการ refresh (โหลด context เมื่อไหร่/ทางไหน,
// commit ตอนไหน, วงจร retry หลังสั่งเล่น, cooldown ของ focus-resync)
// จุดสำคัญสุดคือกฎ commit-เฉพาะ-ตอนสำเร็จ: จำผิดจังหวะเดียว = หน้าคิวว่างค้างถาวรแบบเงียบๆ
using System;
using Xunit;

namespace ChillWithYou_SpotifyMod.Tests
{
    public class RefreshCoordinatorTests
    {
        private static SpotifyNowPlayingInfo PlaylistTrack(string uri = "spotify:playlist:p1") =>
            new SpotifyNowPlayingInfo
            {
                TrackId = "t1",
                Title = "Song",
                Artist = "Artist",
                ContextUri = uri,
                PlaylistContextId = uri != null && uri.StartsWith("spotify:playlist:")
                    ? uri.Substring("spotify:playlist:".Length)
                    : null,
                ThumbnailBytes = new byte[] { 1, 2 },
            };

        private static SpotifyNowPlayingInfo ContextTrack(string uri) =>
            new SpotifyNowPlayingInfo
            {
                TrackId = "t1",
                Title = "Song",
                Artist = "Artist",
                ContextUri = uri,
                PlaylistContextId = null,
                ThumbnailBytes = new byte[] { 1, 2 },
            };

        // === PlanContextFetch: เลือกทางโหลด ===

        [Fact]
        public void Plan_NotLoggedIn_None()
        {
            var c = new RefreshCoordinator();
            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(PlaylistTrack(), loggedIn: false).Kind);
        }

        [Fact]
        public void Plan_NewPlaylistContext_PlaylistFetch()
        {
            var c = new RefreshCoordinator();
            ContextFetchPlan plan = c.PlanContextFetch(PlaylistTrack(), loggedIn: true);

            Assert.Equal(ContextFetchKind.Playlist, plan.Kind);
            Assert.Equal("p1", plan.PlaylistId);
        }

        // album: อ่าน track list ของ context ตรงๆ ไม่ได้ (dev mode) -> ใช้คิว และยืมปกเพลงที่เล่นอยู่
        [Fact]
        public void Plan_AlbumContext_QueueFetchWithCover()
        {
            var c = new RefreshCoordinator();
            SpotifyNowPlayingInfo info = ContextTrack("spotify:album:al1");
            ContextFetchPlan plan = c.PlanContextFetch(info, loggedIn: true);

            Assert.Equal(ContextFetchKind.Queue, plan.Kind);
            Assert.Equal("spotify:album:al1", plan.ContextUri);
            Assert.Equal("Artist", plan.DisplayName);
            Assert.Same(info.ThumbnailBytes, plan.CoverBytes);
        }

        // artist: ปกเปลี่ยนตามอัลบั้มของแต่ละเพลง ใช้เป็นปก header ไม่ได้ -> null (VM จะซ่อนช่องรูป)
        [Fact]
        public void Plan_ArtistContext_QueueFetchWithoutCover()
        {
            var c = new RefreshCoordinator();
            ContextFetchPlan plan = c.PlanContextFetch(ContextTrack("spotify:artist:a1"), loggedIn: true);

            Assert.Equal(ContextFetchKind.Queue, plan.Kind);
            Assert.Null(plan.CoverBytes);
        }

        // เลิกเล่นจาก context (เช่นสลับไปเพลงเดี่ยว) ทั้งที่เคยจำ context เก่าไว้ -> ต้องสั่งโหลด
        // ผ่านทาง Playlist ด้วย id null = เคลียร์คิวบนจอ ไม่ใช่ปล่อยคิวเก่าค้าง
        [Fact]
        public void Plan_ContextGone_PlaylistFetchWithNullId()
        {
            var c = new RefreshCoordinator();
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);

            ContextFetchPlan plan = c.PlanContextFetch(ContextTrack(null), loggedIn: true);

            Assert.Equal(ContextFetchKind.Playlist, plan.Kind);
            Assert.Null(plan.PlaylistId);
        }

        [Fact]
        public void Plan_NullInfo_TreatedAsNoContext()
        {
            var c = new RefreshCoordinator();
            // ยังไม่เคยจำอะไร (null) + info null (ไม่มีเพลง) -> uri ตรงกับที่จำไว้ ไม่ต้องโหลด
            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(null, loggedIn: true).Kind);

            // แต่ถ้าเคยจำ context ไว้ แล้วเพลงหาย -> ต้องเคลียร์
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);
            Assert.Equal(ContextFetchKind.Playlist, c.PlanContextFetch(null, loggedIn: true).Kind);
        }

        // === กฎ commit-เฉพาะ-ตอนสำเร็จ ===

        [Fact]
        public void Commit_OnSuccess_SameContextNotRefetched()
        {
            var c = new RefreshCoordinator();
            Assert.Equal(ContextFetchKind.Playlist, c.PlanContextFetch(PlaylistTrack(), loggedIn: true).Kind);

            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);

            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(PlaylistTrack(), loggedIn: true).Kind);
        }

        // โหลดพลาด (เช่น 429) ต้องไม่ commit - รอบ poll ถัดไปเห็น uri ไม่ตรงแล้ว retry เอง
        // ตรงข้ามคือบั๊กเงียบ: จำไปแล้วทั้งที่จอยังว่าง คิวจะไม่โผล่อีกเลยจนกว่าจะเปลี่ยนเพลย์ลิสต์
        [Fact]
        public void Commit_OnFailure_RetriesNextPoll()
        {
            var c = new RefreshCoordinator();
            c.PlanContextFetch(PlaylistTrack(), loggedIn: true);

            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: false);

            Assert.Equal(ContextFetchKind.Playlist, c.PlanContextFetch(PlaylistTrack(), loggedIn: true).Kind);
        }

        // "ไม่มี context ให้โหลด" commit ได้แม้ loaded=false - ไม่มีอะไรให้ retry แล้ว
        [Fact]
        public void Commit_EmptyUri_AlwaysCommits()
        {
            var c = new RefreshCoordinator();
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);

            c.PlanContextFetch(ContextTrack(null), loggedIn: true);
            c.OnContextFetchCompleted(null, loaded: false);

            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(ContextTrack(null), loggedIn: true).Kind);
            Assert.Null(c.LastSeenContextUri);
        }

        [Fact]
        public void InvalidateContext_ForcesRefetchOfSameUri()
        {
            var c = new RefreshCoordinator();
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);

            c.InvalidateContext(); // ผู้ใช้กด ↻

            Assert.Equal(ContextFetchKind.Playlist, c.PlanContextFetch(PlaylistTrack(), loggedIn: true).Kind);
        }

        [Fact]
        public void Reset_ForcesRefetchOfSameUri()
        {
            var c = new RefreshCoordinator();
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);

            c.Reset(); // re-inject UI ชุดใหม่ คิวบนจอว่าง

            Assert.Equal(ContextFetchKind.Playlist, c.PlanContextFetch(PlaylistTrack(), loggedIn: true).Kind);
        }

        // === เลื่อนหน้าต่างคิวเมื่อเพลงเดินเลยแถวสุดท้ายบนจอ ===

        private static System.Collections.Generic.List<PlaylistTrackInfo> Shown(params string[] ids)
        {
            var list = new System.Collections.Generic.List<PlaylistTrackInfo>();
            foreach (string id in ids) list.Add(new PlaylistTrackInfo { Id = id, Title = "Track " + id });
            return list;
        }

        private static SpotifyNowPlayingInfo TrackInPlaylist(string trackId, string uri = "spotify:playlist:p1")
        {
            SpotifyNowPlayingInfo info = PlaylistTrack(uri);
            info.TrackId = trackId;
            return info;
        }

        // playlist ยาวกว่าที่ดึงมาได้: พอเล่นถึงเพลงที่ 22 เพลงที่เล่นอยู่หายไปจากลิสต์เฉยๆ
        // -> ต้องขอคิวช่วงถัดไปมาแทน ทั้งที่ context ยังเป็น playlist เดิม
        [Fact]
        public void QueueSlide_TrackFellOffTheList_RefetchesWindow()
        {
            var c = new RefreshCoordinator();
            c.PlanContextFetch(TrackInPlaylist("t1"), loggedIn: true);
            c.OnQueueShown(Shown("t1", "t2"));
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);

            // ยังอยู่ในลิสต์ - ไม่ต้องยิงอะไรเพิ่ม
            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(TrackInPlaylist("t2"), loggedIn: true).Kind);

            ContextFetchPlan plan = c.PlanContextFetch(TrackInPlaylist("t22"), loggedIn: true);
            Assert.Equal(ContextFetchKind.QueueWindow, plan.Kind);
            Assert.Equal("spotify:playlist:p1", plan.ContextUri);
        }

        // คิวชุดใหม่มีเพลงที่เล่นอยู่แล้ว -> รอบ poll ถัดไปต้องเงียบ
        [Fact]
        public void QueueSlide_AfterNewWindowArrives_StopsAsking()
        {
            var c = new RefreshCoordinator();
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);
            c.OnQueueShown(Shown("t1", "t2"));

            Assert.Equal(ContextFetchKind.QueueWindow, c.PlanContextFetch(TrackInPlaylist("t22"), loggedIn: true).Kind);
            c.OnQueueShown(Shown("t22", "t23"));

            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(TrackInPlaylist("t22"), loggedIn: true).Kind);
            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(TrackInPlaylist("t23"), loggedIn: true).Kind);
        }

        // ดึงคิวไม่สำเร็จ (คิวชุดเดิมค้างอยู่) ต้องไม่วนขอใหม่ทุกรอบ poll ทุก 6 วิ - เพลงละครั้งพอ
        [Fact]
        public void QueueSlide_AsksOncePerTrack()
        {
            var c = new RefreshCoordinator();
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);
            c.OnQueueShown(Shown("t1", "t2"));

            Assert.Equal(ContextFetchKind.QueueWindow, c.PlanContextFetch(TrackInPlaylist("t22"), loggedIn: true).Kind);
            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(TrackInPlaylist("t22"), loggedIn: true).Kind);

            // เพลงถัดไปก็ยังไม่อยู่ในลิสต์ - ปลดล็อกแล้วลองใหม่ได้
            Assert.Equal(ContextFetchKind.QueueWindow, c.PlanContextFetch(TrackInPlaylist("t23"), loggedIn: true).Kind);
        }

        // ผู้ใช้กดอัลบั้มจากผลค้นหามาดูรายชื่อเพลงในพื้นที่คิว - ห้ามเลื่อนคิวมาทับของที่เขากำลังดู
        [Fact]
        public void QueueSlide_QueueAreaShowingSomethingElse_NeverSlides()
        {
            var c = new RefreshCoordinator();
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);
            c.OnQueueShown(Shown("t1", "t2"));
            c.OnQueueShown(null); // กดอัลบั้มมาดู

            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(TrackInPlaylist("t22"), loggedIn: true).Kind);
        }

        // ยังไม่รู้ว่าจอโชว์อะไร / ไม่มีเพลงเล่นอยู่ - ไม่ใช่เรื่องของกฎนี้
        [Fact]
        public void QueueSlide_NothingShownOrPlaying_NoFetch()
        {
            var c = new RefreshCoordinator();
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);

            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(TrackInPlaylist("t22"), loggedIn: true).Kind);

            c.OnQueueShown(Shown("t1"));
            Assert.Equal(ContextFetchKind.None, c.PlanContextFetch(TrackInPlaylist(null), loggedIn: true).Kind);
        }

        // กด ↻ = โหลด context ใหม่ทั้งชุดอยู่แล้ว ความจำเรื่องหน้าต่างคิวเก่าต้องถูกล้างไปด้วย
        [Fact]
        public void QueueSlide_InvalidateContext_ForgetsShownQueue()
        {
            var c = new RefreshCoordinator();
            c.OnContextFetchCompleted("spotify:playlist:p1", loaded: true);
            c.OnQueueShown(Shown("t1"));

            c.InvalidateContext();

            // uri ไม่ตรงกับที่จำไว้แล้ว -> โหลด context เต็มชุด ไม่ใช่แค่เลื่อนคิว
            Assert.Equal(ContextFetchKind.Playlist, c.PlanContextFetch(TrackInPlaylist("t22"), loggedIn: true).Kind);
        }

        // === วงจรวนเช็คหลังสั่งเล่น ===

        [Fact]
        public void RetryDelays_FastFirstThenStop()
        {
            Assert.Equal(300, RefreshCoordinator.NextRetryDelayMs(0));
            Assert.Equal(500, RefreshCoordinator.NextRetryDelayMs(1));
            Assert.Equal(500, RefreshCoordinator.NextRetryDelayMs(2));
            Assert.Equal(500, RefreshCoordinator.NextRetryDelayMs(3));
            Assert.Null(RefreshCoordinator.NextRetryDelayMs(4)); // ~1.8 วิแล้วเลิกรอ
        }

        [Fact]
        public void IsPlaySettled_TrackOrContextChange()
        {
            SpotifyNowPlayingInfo same = PlaylistTrack();

            Assert.False(RefreshCoordinator.IsPlaySettled(null, "t1", "spotify:playlist:p1")); // ยังไม่รู้ = รอต่อ
            Assert.False(RefreshCoordinator.IsPlaySettled(same, "t1", "spotify:playlist:p1")); // ยังเพลงเดิม
            Assert.True(RefreshCoordinator.IsPlaySettled(same, "t0", "spotify:playlist:p1"));  // เพลงเปลี่ยนแล้ว
            Assert.True(RefreshCoordinator.IsPlaySettled(same, "t1", "spotify:album:x"));      // context เปลี่ยนแล้ว
        }

        // === focus-resync ===

        [Fact]
        public void FocusResync_RequiresFocusAndLogin()
        {
            var c = new RefreshCoordinator();
            DateTime now = DateTime.UtcNow;

            Assert.False(c.ShouldResyncOnFocus(hasFocus: false, loggedIn: true, now)); // สลับออก ไม่ใช่เข้า
            Assert.False(c.ShouldResyncOnFocus(hasFocus: true, loggedIn: false, now)); // ยังไม่ login
            Assert.True(c.ShouldResyncOnFocus(hasFocus: true, loggedIn: true, now));
        }

        [Fact]
        public void FocusResync_CooldownBlocksRapidAltTab()
        {
            var c = new RefreshCoordinator();
            DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            Assert.True(c.ShouldResyncOnFocus(true, true, t0));
            Assert.False(c.ShouldResyncOnFocus(true, true, t0.AddSeconds(1)));  // ยังไม่พ้น 3 วิ
            Assert.False(c.ShouldResyncOnFocus(true, true, t0.AddSeconds(2.9)));
            Assert.True(c.ShouldResyncOnFocus(true, true, t0.AddSeconds(3.1))); // พ้น cooldown แล้ว
        }

        // กดหลายแถวติดกัน: วงจรวนเช็คของคำสั่งเก่าต้องรู้ตัวว่าตกรุ่นแล้วและเลิกยิง API ต่อ
        [Fact]
        public void PlayCycle_OnlyNewestCycleKeepsPolling()
        {
            var c = new RefreshCoordinator();

            int first = c.BeginPlayCycle();
            Assert.True(c.IsCurrentPlayCycle(first));

            int second = c.BeginPlayCycle();
            Assert.False(c.IsCurrentPlayCycle(first)); // คำสั่งเก่าเลิกวน
            Assert.True(c.IsCurrentPlayCycle(second));
        }

        // === poll ระหว่างแผงเปิดค้าง (ตามให้ทันเวลาสั่งจากมือถือ/แอปบนเครื่อง) ===

        [Fact]
        public void VisiblePoll_OnlyWhenPanelOpenAndLoggedIn()
        {
            var c = new RefreshCoordinator();
            DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            Assert.False(c.ShouldPollNowPlaying(panelVisible: false, loggedIn: true, t0));
            Assert.False(c.ShouldPollNowPlaying(panelVisible: true, loggedIn: false, t0));
            Assert.True(c.ShouldPollNowPlaying(panelVisible: true, loggedIn: true, t0));
        }

        // การ refresh ทางอื่น (กดปุ่ม/เพลงจบ/alt-tab) ต้องเลื่อนรอบ poll ถัดไป ไม่ยิงซ้อนของที่เพิ่งทำ
        [Fact]
        public void VisiblePoll_WaitsFullIntervalAfterAnyFetch()
        {
            var c = new RefreshCoordinator();
            DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            c.OnNowPlayingFetchStarted(t0);
            Assert.False(c.ShouldPollNowPlaying(true, true, t0.AddSeconds(1)));
            Assert.False(c.ShouldPollNowPlaying(true, true, t0.AddSeconds(5.9)));
            Assert.True(c.ShouldPollNowPlaying(true, true, t0.AddSeconds(6.1)));

            c.OnNowPlayingFetchStarted(t0.AddSeconds(6.1)); // poll รอบนั้นยิงไปแล้ว
            Assert.False(c.ShouldPollNowPlaying(true, true, t0.AddSeconds(7)));
        }

        // === trigger ตอนเพลงจบ ===

        private static SpotifyNowPlayingInfo At(string trackId, double positionSec, double durationSec = 180) =>
            new SpotifyNowPlayingInfo
            {
                TrackId = trackId,
                Title = "Song",
                Position = TimeSpan.FromSeconds(positionSec),
                Duration = TimeSpan.FromSeconds(durationSec),
            };

        // บั๊กจริง: เดิม trigger ถูกติดอาวุธใหม่ทุกครั้งที่ sync ข้อมูลเพลง พอ Spotify ยังคืนเพลงเดิม
        // ที่ปลายเพลง (ยังสลับให้ไม่ทัน) เฟรมถัดไปก็ยิงซ้ำทันที = ยิง API รัวจนกว่าเพลงจะเปลี่ยน
        [Fact]
        public void SongEnd_DoesNotRearmWhileStuckAtEndOfSameTrack()
        {
            var c = new RefreshCoordinator();
            c.OnNowPlayingSynced(At("t1", positionSec: 10));

            Assert.True(c.ShouldRefreshOnSongEnd());  // นาฬิกาถึงปลายเพลง - ยิงได้ครั้งแรก
            Assert.False(c.ShouldRefreshOnSongEnd()); // เฟรมถัดไปยังปลายเพลงเหมือนเดิม - ห้ามยิงซ้ำ

            // ผลกลับมาเป็นเพลงเดิมที่ปลายเพลง (Spotify ยังไม่ auto-advance) - ยังห้ามยิงซ้ำ
            c.OnNowPlayingSynced(At("t1", positionSec: 179.5));
            Assert.False(c.ShouldRefreshOnSongEnd());
        }

        [Fact]
        public void SongEnd_RearmsOnNewTrackOrRestart()
        {
            var c = new RefreshCoordinator();
            c.OnNowPlayingSynced(At("t1", 10));
            Assert.True(c.ShouldRefreshOnSongEnd());

            c.OnNowPlayingSynced(At("t2", 0)); // Spotify สลับเพลงให้แล้ว
            Assert.True(c.ShouldRefreshOnSongEnd());

            c.OnNowPlayingSynced(At("t2", 179.9)); // เพลงเดิมยังค้างปลายเพลง
            Assert.False(c.ShouldRefreshOnSongEnd());

            c.OnNowPlayingSynced(At("t2", 5)); // ผู้ใช้ย้อนไปฟังใหม่ = รอบใหม่
            Assert.True(c.ShouldRefreshOnSongEnd());
        }

        // ลาก progress bar ถอยกลับมาจากปลายเพลงหลัง trigger ยิงไปแล้ว ต้องติดอาวุธใหม่
        // ไม่งั้นพอเดินถึงปลายอีกรอบจะไม่มีใครไปดึงเพลงถัดไป
        [Fact]
        public void LocalSeek_RearmsSongEndOnlyWhenAwayFromEnd()
        {
            var c = new RefreshCoordinator();
            c.OnNowPlayingSynced(At("t1", 10));
            Assert.True(c.ShouldRefreshOnSongEnd());

            c.OnLocalSeek(TimeSpan.FromSeconds(179), TimeSpan.FromSeconds(180)); // ยังอยู่ปลายเพลง
            Assert.False(c.ShouldRefreshOnSongEnd());

            c.OnLocalSeek(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(180)); // ลากถอยกลับมา
            Assert.True(c.ShouldRefreshOnSongEnd());
        }

        // เคสที่ resync ไม่เกิด (focus ออก/ยังไม่ login) ต้องไม่ไปแตะนาฬิกา cooldown
        [Fact]
        public void FocusResync_DeniedChecksDoNotTouchCooldown()
        {
            var c = new RefreshCoordinator();
            DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            Assert.False(c.ShouldResyncOnFocus(false, true, t0));
            Assert.False(c.ShouldResyncOnFocus(true, false, t0.AddSeconds(1)));
            Assert.True(c.ShouldResyncOnFocus(true, true, t0.AddSeconds(2))); // ครั้งแรกจริงๆ ต้องผ่านทันที
        }
    }
}
