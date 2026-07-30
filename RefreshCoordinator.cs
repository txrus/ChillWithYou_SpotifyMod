// RefreshCoordinator.cs
// กฎ orchestration ของการ refresh - ล้วนๆ ไม่พึ่ง UnityEngine, ไม่ log, ไม่ยิง API เอง
// (แพทเทิร์นเดียวกับ NowPlayingSession/PanelViewModel: ทดสอบด้วย xUnit ได้โดยไม่เปิดเกม)
//
// injector เป็นคนยิง API จริง แต่ "จะยิงไหม/ยิงตัวไหน/จำผลยังไง" ถามที่นี่:
// - PlanContextFetch: หลัง poll now-playing ควรโหลด context ต่อไหม และผ่านทางไหน
//   (รวมถึงกฎเลื่อนหน้าต่างคิวเมื่อเพลงที่เล่นอยู่เดินเลยแถวสุดท้ายบนจอไปแล้ว)
// - OnContextFetchCompleted: กฎ commit-เฉพาะ-ตอนสำเร็จ - โหลดพลาดต้องไม่จำ เพื่อให้
//   รอบ poll หน้า retry เอง (จำผิดจังหวะเดียว = หน้าคิวว่างค้างถาวรแบบเงียบๆ)
// - NextRetryDelayMs / IsPlaySettled: วงจรวนเช็คหลังสั่งเล่น จน Spotify สลับเพลงให้จริง
// - ShouldResyncOnFocus: resync ตอน alt-tab กลับเข้าเกม + cooldown กันยิง API รัว
//
// เรื่อง thread: ถูกเรียกจาก continuation ของ async refresh (ไม่ใช่ main thread เสมอ)
// เหมือนที่ field เดิมใน injector โดนมาก่อน - state ในนี้เป็น field เดี่ยวอ่าน/เขียนตรงๆ
// ตามโมเดลเดิม ไม่ได้เพิ่มข้อกำหนดใหม่
using System;
using System.Collections.Generic;

namespace ChillWithYou_SpotifyMod
{
    // ทางที่ต้องใช้โหลด context หลัง poll now-playing
    public enum ContextFetchKind
    {
        None,     // ไม่ต้องโหลด (ยังไม่ login / context เดิมที่จำไว้อยู่แล้ว)
        Playlist, // โหลดผ่าน /playlists/{id} (PlaylistId เป็น null ได้ = ไม่ได้เล่นจาก context -> เคลียร์คิว)
        Queue,    // context ที่ไม่ใช่ playlist (artist/album) - อ่านตรงๆ ไม่ได้แล้ว ใช้ /me/player/queue แทน

        // context เดิม แต่เพลงที่เล่นอยู่หลุดออกไปจากแถวที่โชว์อยู่แล้ว -> ขอคิวชุดใหม่มาแทนเฉพาะแถว
        // (header ยังเป็นของเดิม เพราะยังเล่นอยู่ใน playlist/album/artist เดียวกัน)
        QueueWindow,
    }

    public struct ContextFetchPlan
    {
        public ContextFetchKind Kind;
        public string PlaylistId;   // Kind == Playlist
        public string ContextUri;   // Kind == Queue
        public string DisplayName;  // Kind == Queue: ชื่อที่จะโชว์บน header (ชื่อศิลปิน)
        public byte[] CoverBytes;   // Kind == Queue: ปก header (album ยืมปกเพลงที่เล่นอยู่ / artist = null)

        // Kind == Playlist: เปิด shuffle อยู่ไหม - /playlists/{id} คืนลำดับเพลงตายตัวเสมอ ไม่ขยับตาม
        // shuffle เลย ผู้เรียกต้องรู้ค่านี้เพื่อสลับไปใช้คิวจริงจาก /me/player/queue แทนตอน shuffle เปิด
        // (ดู SpotifyWebApi.GetCurrentPlaylistAsync)
        public bool ShuffleOn;
    }

    public class RefreshCoordinator
    {
        // context ล่าสุดที่โหลดสำเร็จ (หรือยืนยันแล้วว่าไม่มี) - ตัวกันไม่ให้โหลดซ้ำทุกรอบ poll
        private string _lastSeenContextUri;

        // ให้ PlayThen/OnPrev/OnNext ใช้เป็น "context ก่อนสั่ง" เวลารอดูว่า Spotify สลับให้หรือยัง
        public string LastSeenContextUri => _lastSeenContextUri;

        private DateTime _lastFocusRefreshUtc = DateTime.MinValue;
        private static readonly TimeSpan FocusRefreshCooldown = TimeSpan.FromSeconds(3);

        // ระยะรอของวงจรวนเช็คหลังสั่งเล่น: Spotify ใช้เวลาครู่หนึ่งกว่าจะสลับเพลง/context
        // รอบแรกเร็วหน่อย รอบถัดไปห่างขึ้น รวม ~1.8 วิ แล้วเลิก (ไม่มี polling ตามเวลาต่อ)
        private static readonly int[] PlayRetryDelaysMs = { 300, 500, 500, 500 };

        // === จุด reset ===

        // UI ถูกสร้างใหม่ทั้งชุด (inject/re-inject) - คิวบนจอว่างเปล่า ต้องบังคับให้รอบ poll
        // ถัดไปโหลด context มาเติมใหม่แม้ uri จะเหมือนเดิม (ฝั่ง SpotifyWebApi ยังมี cache
        // เลยมักได้ของทันทีโดยไม่เปลือง API call เพิ่ม)
        public void Reset()
        {
            _lastSeenContextUri = null;
            ForgetShownQueue();
        }

        // ผู้ใช้กด ↻ - ลืม context ที่จำไว้เพื่อบังคับโหลดคิว snapshot ล่าสุดใหม่ทั้งชุด
        // (การล้าง cache ฝั่ง SpotifyWebApi เป็นหน้าที่ผู้เรียก - นั่นเป็น side effect)
        public void InvalidateContext()
        {
            _lastSeenContextUri = null;
            ForgetShownQueue();
        }

        // === context-refresh policy ===

        // หลัง poll now-playing เสร็จ: ควรโหลด context ต่อไหม ทางไหน
        // โหลดเฉพาะตอน context เปลี่ยนจากที่จำไว้ - ไม่มี timer แยก ไม่ยิง endpoint เพิ่มฟรีๆ
        public ContextFetchPlan PlanContextFetch(SpotifyNowPlayingInfo info, bool loggedIn)
        {
            string contextUri = info?.ContextUri;
            if (!loggedIn)
                return NoFetch;

            // context เดิม - ไม่ต้องโหลด header ใหม่ แต่คิวบนจออาจตามเพลงไม่ทันแล้ว
            if (contextUri == _lastSeenContextUri)
                return PlanQueueSlide(info);

            string playlistId = info?.PlaylistContextId;
            if (!string.IsNullOrEmpty(contextUri) && string.IsNullOrEmpty(playlistId))
            {
                // artist/album/Liked Songs: อ่านรายชื่อเพลงของ context ตรงๆ ไม่ได้ (dev mode
                // บล็อกทั้ง track list และ /me/tracks) -> ใช้คิวแทน
                // ปก: album ทุกเพลงใช้ปกเดียวกัน ยืมปกเพลงที่เล่นอยู่ได้เลย / artist กับ Liked Songs
                // ปกเปลี่ยนตามอัลบั้มของแต่ละเพลง ใช้ไม่ได้ -> null แล้วให้ VM ซ่อนช่องรูป
                bool coverVaries = SpotifyContext.HeaderCoverVaries(contextUri);
                return new ContextFetchPlan
                {
                    Kind = ContextFetchKind.Queue,
                    ContextUri = contextUri,
                    DisplayName = SpotifyContext.IsCollection(contextUri) ? "Liked Songs" : info?.Artist,
                    CoverBytes = coverVaries ? null : info?.ThumbnailBytes,
                };
            }

            // playlist จริง (id ไม่ null) หรือไม่ได้เล่นจาก context เลย (ทั้งคู่ null = เคลียร์คิว)
            // ส่ง ShuffleOn ไปด้วยเสมอแม้ playlistId จะ null - ผู้เรียกใช้แค่ตอนมี playlist จริง
            return new ContextFetchPlan
            {
                Kind = ContextFetchKind.Playlist,
                PlaylistId = playlistId,
                ShuffleOn = info?.ShuffleOn ?? false,
            };
        }

        private static ContextFetchPlan NoFetch => new ContextFetchPlan { Kind = ContextFetchKind.None };

        // === หน้าต่างคิวบนจอ ===

        // track id ของทุกแถวที่โชว์อยู่ในคิวตอนนี้ - null = ของที่โชว์อยู่ไม่ใช่คิวของ context ที่เล่นอยู่
        // (ผู้ใช้กดอัลบั้มจากผลค้นหามาดูรายชื่อเพลง) จึงห้ามไปเลื่อนทับของที่เขากำลังดู
        private HashSet<string> _shownTrackIds;

        // เพลงที่สั่งเลื่อนหน้าต่างไปแล้วหนึ่งรอบ - กันยิงซ้ำทุกรอบ poll ถ้าคิวชุดใหม่ยังไม่มีเพลงนี้
        private string _slidForTrackId;

        // คิวบนจอเพิ่งถูกแทนที่ด้วยเพลงชุดนี้ (null = ไม่ใช่คิวของ context ที่เล่นอยู่)
        // ไม่ล้าง _slidForTrackId ที่นี่: ถ้าคิวชุดใหม่ยังไม่มีเพลงที่เล่นอยู่ การล้างจะทำให้รอบ poll
        // ถัดไปสั่งเลื่อนอีกวนไม่จบ - ปล่อยให้ปลดล็อกเองตอนเปลี่ยนเพลง (หรือผู้ใช้กด ↻)
        public void OnQueueShown(List<PlaylistTrackInfo> tracks)
        {
            if (tracks == null)
            {
                _shownTrackIds = null;
                return;
            }

            var ids = new HashSet<string>();
            foreach (PlaylistTrackInfo t in tracks)
                if (!string.IsNullOrEmpty(t.Id)) ids.Add(t.Id);
            _shownTrackIds = ids;
        }

        private void ForgetShownQueue()
        {
            _shownTrackIds = null;
            _slidForTrackId = null;
        }

        // เล่นอยู่ใน context เดิม แต่เพลงที่เล่นอยู่ไม่มีในแถวที่โชว์แล้ว - เกิดเมื่อ Spotify เดินเลย
        // เพลงสุดท้ายที่เราดึงมาได้ (playlist ยาวกว่า 21 เพลง พอถึงเพลงที่ 22 คิวบนจอก็ไม่มีเพลงไหน
        // ถูกไฮไลต์อีกเลย) หรือผู้ใช้ข้ามไปเพลงอื่นใน playlist เดิมจากแอปอื่น
        // -> ขอคิวจาก /me/player/queue ซึ่งเริ่มนับจากเพลงที่เล่นอยู่เสมอ มาแทนแถวเดิม
        private ContextFetchPlan PlanQueueSlide(SpotifyNowPlayingInfo info)
        {
            string trackId = info?.TrackId;
            if (string.IsNullOrEmpty(trackId) || string.IsNullOrEmpty(info.ContextUri))
                return NoFetch;

            // ยังไม่รู้ว่าจอโชว์อะไรอยู่ / จอโชว์ของอย่างอื่นอยู่ (อัลบั้มที่กดดู) - ไม่ใช่เรื่องของกฎนี้
            if (_shownTrackIds == null || _shownTrackIds.Count == 0) return NoFetch;
            if (_shownTrackIds.Contains(trackId)) return NoFetch;

            // เลื่อนให้เพลงละครั้งเดียว: ถ้าคิวชุดใหม่ยังไม่มีเพลงนี้ (หรือโหลดพลาด) ต้องไม่วนยิงทุกรอบ poll
            if (trackId == _slidForTrackId) return NoFetch;
            _slidForTrackId = trackId;

            return new ContextFetchPlan { Kind = ContextFetchKind.QueueWindow, ContextUri = info.ContextUri };
        }

        // ผลของการโหลดตามแผน: commit เฉพาะตอนสำเร็จ (หรือไม่มี context ให้โหลด)
        // โหลดพลาดต้องไม่ commit - รอบ poll ถัดไปจะเห็นว่า uri ยังไม่ตรงกับที่จำไว้แล้ว retry เอง
        public void OnContextFetchCompleted(string contextUri, bool loaded)
        {
            if (loaded || string.IsNullOrEmpty(contextUri))
                _lastSeenContextUri = contextUri;
        }

        // === วงจรวนเช็คหลังสั่งเล่น ===

        // ระยะรอก่อน refresh ครั้งที่ attempt (เริ่มนับ 0) - null = เลิกรอแล้ว ปล่อยตามยถากรรม
        public static int? NextRetryDelayMs(int attempt) =>
            attempt >= 0 && attempt < PlayRetryDelaysMs.Length ? PlayRetryDelaysMs[attempt] : (int?)null;

        // เลขรอบของคำสั่งเล่นล่าสุด - กันวงจรวนเช็คซ้อนกัน: ผู้เล่นกดหลายแถวติดๆ กัน (กดผิด
        // แล้วกดใหม่ทันที เป็นเรื่องปกติ) เดิมได้วงจรละ 4 GET ซ้อนกันทุกครั้งที่กด ทั้งที่คำสั่งเก่า
        // ไม่มีความหมายอีกแล้ว - คำสั่งใหม่ทำให้ของเก่าเลิกวนทันที
        private int _playCycle;

        public int BeginPlayCycle() => ++_playCycle;

        public bool IsCurrentPlayCycle(int cycle) => cycle == _playCycle;

        // === trigger ตอนนาฬิกาในเครื่องเดินถึงปลายเพลง ===

        private bool _songEndFired;
        private string _lastSyncedTrackId;

        // เพลงเดิมที่ position ยังห่างปลายเพลงเกินเท่านี้ = ผู้ใช้ย้อนกลับไปฟังใหม่/seek ถอยหลัง
        // ถือว่าเป็นการเล่นรอบใหม่ ติดอาวุธ trigger ได้อีกครั้ง
        private static readonly TimeSpan SongEndRearmMargin = TimeSpan.FromSeconds(2);

        // นาฬิกาเดินถึงปลายเพลงแล้ว - ควรยิง refresh เพื่อดึงเพลงถัดไปไหม (ยิงได้ครั้งเดียวต่อเพลง)
        public bool ShouldRefreshOnSongEnd()
        {
            if (_songEndFired) return false;
            _songEndFired = true;
            return true;
        }

        // ข้อมูลเพลงรอบใหม่มาถึงและ sync เข้านาฬิกาแล้ว - ตัดสินว่าติดอาวุธ trigger ใหม่ได้หรือยัง
        // ห้ามติดอาวุธใหม่เมื่อ Spotify ยังคืนเพลงเดิมที่ปลายเพลงอยู่ (ยังสลับให้ไม่ทัน) ไม่งั้น
        // เฟรมถัดไปจะยิง refresh อีกทันทีวนไปเรื่อยๆ = ยิง API รัวจนกว่า Spotify จะเปลี่ยนเพลงให้
        public void OnNowPlayingSynced(SpotifyNowPlayingInfo info)
        {
            if (info == null)
            {
                _lastSyncedTrackId = null;
                _songEndFired = false;
                return;
            }

            bool trackChanged = info.TrackId != _lastSyncedTrackId;
            _lastSyncedTrackId = info.TrackId;

            if (trackChanged || info.Duration - info.Position > SongEndRearmMargin)
                _songEndFired = false;
        }

        // ผู้เล่นลาก progress bar ในเกมเอง - ตำแหน่งเปลี่ยนโดยไม่มีข้อมูลรอบใหม่จาก Spotify
        // ลากถอยกลับมาจากปลายเพลงหลัง trigger ยิงไปแล้ว ต้องติดอาวุธใหม่ ไม่งั้นพอเดินถึงปลาย
        // อีกรอบจะไม่มีใครไปดึงเพลงถัดไปให้
        public void OnLocalSeek(TimeSpan position, TimeSpan duration)
        {
            if (duration - position > SongEndRearmMargin)
                _songEndFired = false;
        }

        // Spotify รับคำสั่งไปแล้วจริงไหม: เห็นเพลงหรือ context เปลี่ยนไปจากตอนก่อนสั่ง = จบ
        public static bool IsPlaySettled(SpotifyNowPlayingInfo info, string trackIdBefore, string contextUriBefore) =>
            info != null && (info.TrackId != trackIdBefore || info.ContextUri != contextUriBefore);

        // === poll ระหว่างที่แผงเปิดค้างอยู่ ===

        // เวลาที่เริ่มดึงข้อมูลเพลงครั้งล่าสุด ไม่ว่าจะมาจากทางไหน (กดปุ่ม/เพลงจบ/alt-tab/poll)
        // -> การ refresh ทางอื่นเลื่อน poll รอบถัดไปออกไปเอง ไม่ยิงซ้อนกัน
        private DateTime _lastNowPlayingFetchUtc = DateTime.MinValue;

        // ถี่พอให้การสั่งจากมือถือ/แอปบนเครื่องขึ้นในเกมภายในไม่กี่วินาที แต่ยังเป็นแค่ ~10 ครั้ง/นาที
        // และเกิดเฉพาะตอนแผงเปิดอยู่จริง (ปิดเมนูแล้วไม่มีอะไรให้อัปเดต - หยุดยิงทันที)
        private static readonly TimeSpan VisiblePollInterval = TimeSpan.FromSeconds(6);

        public void OnNowPlayingFetchStarted(DateTime nowUtc) => _lastNowPlayingFetchUtc = nowUtc;

        // ถึงรอบ poll หรือยัง - เงื่อนไขคือแผงเปิดอยู่ + login แล้ว + ห่างจากการดึงครั้งล่าสุดพอ
        public bool ShouldPollNowPlaying(bool panelVisible, bool loggedIn, DateTime nowUtc)
        {
            if (!panelVisible || !loggedIn) return false;
            return nowUtc - _lastNowPlayingFetchUtc >= VisiblePollInterval;
        }

        // === focus-resync policy ===

        // สลับหน้าต่างกลับเข้าเกม = จังหวะที่ผู้ใช้อาจเพิ่งไปสั่งเพลงจากแอป Spotify มา
        // -> ควร resync ไหม (คืน true พร้อมจดเวลาไว้กันรอบถัดไปยิงถี่เกิน cooldown)
        public bool ShouldResyncOnFocus(bool hasFocus, bool loggedIn, DateTime nowUtc)
        {
            if (!hasFocus || !loggedIn) return false;
            if (nowUtc - _lastFocusRefreshUtc < FocusRefreshCooldown) return false; // alt-tab รัวๆ ไม่ยิงตาม
            _lastFocusRefreshUtc = nowUtc;
            return true;
        }
    }
}
