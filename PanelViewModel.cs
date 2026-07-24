// PanelViewModel.cs
// State machine ล้วนของ Spotify panel - ไม่พึ่ง UnityEngine, ไม่ log, ไม่เรียก API เอง
// (แพทเทิร์นเดียวกับ NowPlayingSession: caller ป้อนข้อมูล/เหตุการณ์เข้ามา แล้วอ่านผลออกไป
//  ทำให้ logic "แถวไหนโผล่/ซ่อน + ตอนไหนต้อง reflow + list โชว์อะไร" ทดสอบด้วย xUnit ได้โดยไม่เปิดเกม)
//
// สัญญากับผู้เรียก (SpotifyButtonInjector):
// - เรียกทุก event method จาก main thread เท่านั้น (VM ไม่ thread-safe และไม่จำเป็นต้องเป็น)
// - หลังทุก event เอา Current ไปเข้า Apply(state) จุดเดียว: SetActive ทุกแถวตาม flag แบบ idempotent,
//   rebuild list เมื่อ revision เปลี่ยน, และ ForceRebuildLayoutImmediate เมื่อ NeedsReflow
// - NeedsReflow อธิบาย "event ล่าสุด" เท่านั้น (ตั้งใหม่ทุกครั้ง ไม่สะสม)
// - นาฬิกา progress ทุกเฟรมเป็นเรื่องของ NowPlayingSession - VM ตัวนี้ทำงานเฉพาะตอนเกิด event ซึ่งนานๆ ที
using System;
using System.Collections.Generic;

namespace ChillWithYou_SpotifyMod
{
    // สิ่งที่เกิดขึ้นเมื่อผู้เล่นกดแถวหนึ่งใน list - VM บรรยาย action เป็นข้อมูลล้วน
    // ให้ injector เป็นคนแปลงไปเป็น API call (VM จะได้ไม่ต้องรู้จัก async/Spotify เลย)
    public enum RowActionKind
    {
        None,               // แถวกดไม่ได้ (เช่นคิวของ artist context)
        PlayTrack,          // เล่นเพลงเดี่ยว (หลุด context)
        PlayContext,        // เล่นทั้ง playlist/album/artist ตั้งแต่ต้น
        PlayTrackInContext, // เล่นเพลงนี้โดยคง context (next/prev เดินต่อ)
        LoadAlbum,          // โหลด track list ของอัลบั้มมาแสดง (ยังไม่เล่น)
    }

    public struct RowAction
    {
        public RowActionKind Kind;
        public string TrackId;
        public string ContextUri;
        public string AlbumId;
        public string AlbumName;
        public string AlbumCoverUrl;

        public static readonly RowAction None = new RowAction { Kind = RowActionKind.None };
        public static RowAction PlayTrack(string trackId) =>
            new RowAction { Kind = RowActionKind.PlayTrack, TrackId = trackId };
        public static RowAction PlayContext(string contextUri) =>
            new RowAction { Kind = RowActionKind.PlayContext, ContextUri = contextUri };
        public static RowAction PlayTrackInContext(string contextUri, string trackId) =>
            new RowAction { Kind = RowActionKind.PlayTrackInContext, ContextUri = contextUri, TrackId = trackId };
        public static RowAction LoadAlbum(string albumId, string albumName, string coverUrl) =>
            new RowAction { Kind = RowActionKind.LoadAlbum, AlbumId = albumId, AlbumName = albumName, AlbumCoverUrl = coverUrl };
    }

    // แถวหนึ่งใน list (คิวเพลง / ผลค้นหา / My Lists) - เป็น data ล้วน renderer วาดตามนี้
    public class PanelRow
    {
        public string Index;   // เลขลำดับ ("1", "2", ...) - null = ไม่มีช่องเลข (แถวผลค้นหา)
        public string Title;
        public string Sub;     // บรรทัดรอง (ศิลปิน / "Artist" / "N tracks" / เจ้าของ playlist)
        public string Right;   // ช่องขวา (เวลาเพลง) - null = ไม่มี
        public string TrackId; // ใช้จับคู่ highlight เพลงที่กำลังเล่น - null = แถวนี้ไม่เกี่ยว
        public RowAction Action;
    }

    // หมวดหนึ่งในพื้นที่ผลลัพธ์ (เช่น "TRACKS") - Message มีค่าเมื่อหมวดนี้เป็นข้อความแจ้งเฉยๆ
    public class PanelSection
    {
        public string Label;
        public string Message;
        public List<PanelRow> Rows = new List<PanelRow>();
    }

    // พื้นที่ผลลัพธ์ใต้แถบ search ตอนนี้โชว์อะไรอยู่
    public enum ResultsMode { Empty, SearchResults, MyPlaylists }

    // Snapshot ทั้งหมดที่ UI ต้องรู้ - Apply(state) อ่านจากนี่ที่เดียว
    public class PanelState
    {
        // แถวไหน active บ้าง (บั๊ก 3 ใน 6 ตัวของ v1.1.2 คือ "ลืมจุดใดจุดหนึ่ง" ของกลุ่มนี้)
        public bool ConnectRowVisible;
        public bool ControlsRowVisible;
        public bool PlaylistHeaderVisible;
        public bool QueueListVisible;
        public bool SearchRowVisible;

        public string StatusText = "";

        // ส่วน now-playing (เฉพาะของที่เปลี่ยนตาม event - เข็มวินาที/slider เป็นของ NowPlayingSession)
        public string TrackTitle = IdleTitle;
        public string TrackArtist = "";
        public string PlayPauseGlyph = "||";
        public bool ShowIdleProgress = true;    // true = ไม่มีเพลง ให้รีเซ็ตแสดงผลเวลา/slider เป็นศูนย์
        public byte[] NowPlayingCoverBytes;     // null = คงปกเดิม/placeholder (injector cache sprite เอง)
        public string HighlightedTrackId;       // เพลงในคิวที่ควรทาสีเขียว - null = ไม่มี

        // Playlist header
        public string HeaderName = "-";
        public string HeaderSubLabel = "PLAYING FROM PLAYLIST"; // null = ซ่อนบรรทัดรอง
        public bool HeaderCoverVisible = true;
        public byte[] HeaderCoverBytes;         // null = placeholder

        // คิวเพลง: rows หรือ (ถ้าโหลดรายชื่อไม่ได้) ข้อความแจ้ง
        public List<PanelRow> QueueRows = new List<PanelRow>();
        public string QueueMessage;

        // พื้นที่ผลลัพธ์ใต้แถบ search
        public ResultsMode ResultsMode = ResultsMode.Empty;
        public List<PanelSection> ResultsSections = new List<PanelSection>();

        // rebuild เฉพาะตอนเนื้อหาเปลี่ยนจริง - Apply เทียบกับเลขที่ตัวเอง apply ล่าสุด
        public int QueueRevision;
        public int ResultsRevision;

        // event ล่าสุดเปลี่ยนโครงสร้าง (แถวโผล่/หาย, list เปลี่ยน) -> Apply ต้องสั่ง
        // ForceRebuildLayoutImmediate ที่ scroll content ชั้นนอก ไม่งั้นแถวของเกมวาดทับ section เรา
        public bool NeedsReflow;

        public const string IdleTitle = "Connect Spotify and play a song on any device to see controls";
    }

    public class PanelViewModel
    {
        public PanelState Current { get; private set; } = new PanelState();

        // === events ===

        // UI เพิ่งถูกสร้างใหม่ทั้งชุด (inject/re-inject) - เริ่ม state ใหม่หมดจาก flag login เดียว
        // กัน state ค้างจาก panel รอบก่อน (เช่น toggle My Lists ที่เคยค้างมาแล้วหนึ่งรอบ)
        public PanelState ResetForInject(bool loggedIn)
        {
            Current = new PanelState
            {
                ConnectRowVisible = !loggedIn,
                ControlsRowVisible = loggedIn,
                PlaylistHeaderVisible = loggedIn,
                QueueListVisible = loggedIn,
                SearchRowVisible = loggedIn,
                NeedsReflow = true,
            };
            return Current;
        }

        public PanelState ConnectClicked()
        {
            Current.NeedsReflow = false;
            Current.StatusText = "";
            return Current;
        }

        public PanelState LoginSucceeded()
        {
            // สลับ connect row ออก เอาชุดหลัง login เข้า - section สูงขึ้น ต้อง reflow
            // (บั๊กจริงจาก smoke test v1.1.2: เดิม SetActive เฉยๆ แถวของเกมเลยวาดทับ header/แถบ search)
            Current.ConnectRowVisible = false;
            Current.ControlsRowVisible = true;
            Current.PlaylistHeaderVisible = true;
            Current.QueueListVisible = true;
            Current.SearchRowVisible = true;
            Current.StatusText = "";
            Current.NeedsReflow = true;
            return Current;
        }

        public PanelState LoginFailed(string error)
        {
            Current.NeedsReflow = false;
            Current.StatusText = "Connect failed: " + error;
            return Current;
        }

        // ข้อมูลเพลงที่กำลังเล่นมาถึง (null = ไม่มีอะไรเล่นอยู่/ยังไม่ login)
        // เปลี่ยนแค่ข้อความ/glyph/highlight - ไม่แตะโครงสร้าง เลยไม่ต้อง reflow
        public PanelState NowPlayingUpdated(SpotifyNowPlayingInfo info)
        {
            Current.NeedsReflow = false;

            if (info == null || string.IsNullOrEmpty(info.Title))
            {
                Current.TrackTitle = PanelState.IdleTitle;
                Current.TrackArtist = "";
                Current.ShowIdleProgress = true;
                Current.HighlightedTrackId = null;
                Current.NowPlayingCoverBytes = null;
                return Current;
            }

            Current.TrackTitle = info.Title;
            Current.TrackArtist = info.Artist ?? "-";
            Current.PlayPauseGlyph = info.IsPlaying ? "||" : ">";
            Current.ShowIdleProgress = false;
            Current.HighlightedTrackId = info.TrackId;
            Current.NowPlayingCoverBytes = info.ThumbnailBytes;
            return Current;
        }

        // สลับ play/pause ในเครื่องโดยไม่ยิง GET ตาม (ผลลัพธ์รู้อยู่แล้ว) - เปลี่ยนแค่ glyph
        public PanelState LocalPlayPauseToggled(bool isPlaying)
        {
            Current.NeedsReflow = false;
            Current.PlayPauseGlyph = isPlaying ? "||" : ">";
            return Current;
        }

        // context (playlist/album/artist) โหลดเสร็จ - เติม header + คิวเพลง
        // null = ไม่ได้เล่นจาก context ใดๆ (เช่นเพลงเดี่ยว) -> เคลียร์คิว
        public PanelState ContextLoaded(PlaylistInfo playlist)
        {
            Current.QueueRows = new List<PanelRow>();
            Current.QueueMessage = null;
            Current.QueueRevision++;
            Current.NeedsReflow = true;

            if (playlist == null)
            {
                Current.HeaderName = "Not playing from a playlist";
                Current.HeaderSubLabel = null;
                Current.HeaderCoverVisible = false;
                Current.HeaderCoverBytes = null;
                return Current;
            }

            Current.HeaderName = playlist.Name ?? "-";
            // บอกให้ตรงกับสิ่งที่กดเล่นจริง ไม่งั้นเล่นจากศิลปิน/อัลบั้มแล้วยังขึ้นว่า PLAYLIST
            string kind = SpotifyContext.KindLabel(playlist.ContextUri);
            Current.HeaderSubLabel = kind != null ? $"PLAYING FROM {kind}" : null;

            // artist ไม่มีปกให้ใช้ (คิวเพลงไม่ได้ให้ภาพของตัว context มา) -> ซ่อนช่องรูปไปเลย
            bool isArtist = SpotifyContext.IsArtist(playlist.ContextUri);
            Current.HeaderCoverVisible = !isArtist;
            Current.HeaderCoverBytes = isArtist ? null : playlist.CoverImageBytes;

            if (playlist.Tracks == null || playlist.Tracks.Count == 0)
            {
                // โหลดรายชื่อเพลงไม่ได้ (เช่น Daily Mix / Discover Weekly ที่ Spotify ปิด API access ไปแล้ว)
                Current.QueueMessage = "Track list not available for this playlist";
                return Current;
            }

            for (int i = 0; i < playlist.Tracks.Count; i++)
            {
                PlaylistTrackInfo t = playlist.Tracks[i];
                // artist context: Spotify ไม่รับ offset -> แถวกดไม่ได้ แสดงคิวอย่างเดียว
                bool clickable = !isArtist && !string.IsNullOrEmpty(t.Id);
                Current.QueueRows.Add(new PanelRow
                {
                    Index = (i + 1).ToString(),
                    Title = t.Title ?? "-",
                    Sub = t.Artist ?? "-",
                    Right = FormatTime(TimeSpan.FromMilliseconds(t.DurationMs)),
                    TrackId = t.Id,
                    Action = clickable
                        ? RowAction.PlayTrackInContext(playlist.ContextUri, t.Id)
                        : RowAction.None,
                });
            }
            return Current;
        }

        // ผลค้นหามาถึง - แทนที่พื้นที่ผลลัพธ์ทั้งหมด (รวมถึงปิดโหมด My Lists ที่อาจเปิดอยู่)
        public PanelState SearchResultsArrived(SpotifySearchResults results)
        {
            Current.ResultsSections = new List<PanelSection>();
            Current.ResultsRevision++;
            Current.NeedsReflow = true;

            bool any = results != null &&
                       (results.Tracks.Count + results.Artists.Count +
                        results.Albums.Count + results.Playlists.Count) > 0;
            if (!any)
            {
                Current.ResultsMode = ResultsMode.Empty;
                return Current;
            }

            Current.ResultsMode = ResultsMode.SearchResults;

            if (results.Tracks.Count > 0)
            {
                var s = NewSection("Tracks");
                foreach (SearchTrackResult t in results.Tracks)
                    s.Rows.Add(new PanelRow
                    {
                        Title = t.Title,
                        Sub = t.Artist,
                        Right = FormatTime(TimeSpan.FromMilliseconds(t.DurationMs)),
                        Action = RowAction.PlayTrack(t.Id),
                    });
            }

            if (results.Artists.Count > 0)
            {
                var s = NewSection("Artists");
                // สั่งเล่น artist context ไปเลย - ดึงรายชื่อเพลงของศิลปินมาแสดงก่อนไม่ได้แล้ว
                // (/artists/{id}/top-tracks ถูกตัดจาก Development Mode รอบ ก.พ. 2026)
                foreach (SearchArtistResult a in results.Artists)
                    s.Rows.Add(new PanelRow
                    {
                        Title = a.Name,
                        Sub = "Artist",
                        Action = RowAction.PlayContext($"spotify:artist:{a.Id}"),
                    });
            }

            if (results.Albums.Count > 0)
            {
                var s = NewSection("Albums");
                foreach (SearchAlbumResult al in results.Albums)
                    s.Rows.Add(new PanelRow
                    {
                        Title = al.Name,
                        Sub = al.ArtistName,
                        Action = RowAction.LoadAlbum(al.Id, al.Name, al.CoverUrl),
                    });
            }

            if (results.Playlists.Count > 0)
            {
                var s = NewSection("Playlists");
                // สั่งเล่น playlist เลยแทนการโหลดรายชื่อเพลงมาแสดง (อ่าน track list ตรงๆ
                // โดนบล็อกสำหรับ dev-mode app) - พอเริ่มเล่น context ใหม่จะเติมหน้าคิวให้เอง
                foreach (SearchPlaylistResult p in results.Playlists)
                    s.Rows.Add(new PanelRow
                    {
                        Title = p.Name,
                        Sub = p.OwnerName ?? "-",
                        Action = RowAction.PlayContext($"spotify:playlist:{p.Id}"),
                    });
            }

            return Current;
        }

        // กดปุ่ม My Lists: ถ้ากำลังโชว์อยู่ -> หุบ (ผู้เรียกไม่ต้อง fetch)
        // ถ้ายังไม่โชว์ -> คืน true ให้ผู้เรียกไป fetch แล้วค่อยเรียก MyPlaylistsArrived
        public bool MyListsClicked()
        {
            if (Current.ResultsMode == ResultsMode.MyPlaylists)
            {
                Current.ResultsMode = ResultsMode.Empty;
                Current.ResultsSections = new List<PanelSection>();
                Current.ResultsRevision++;
                Current.NeedsReflow = true;
                return false;
            }

            Current.NeedsReflow = false;
            return true;
        }

        // รายชื่อ playlist ของ user มาถึง (null = โหลดพลาด) - โชว์ในพื้นที่ผลลัพธ์
        public PanelState MyPlaylistsArrived(List<UserPlaylistInfo> playlists)
        {
            Current.ResultsSections = new List<PanelSection>();
            Current.ResultsRevision++;
            Current.NeedsReflow = true;
            // นับเป็นโหมด My Lists แม้โหลดพลาด - กดปุ่มซ้ำจะได้หุบข้อความ error ได้เหมือนรายการปกติ
            Current.ResultsMode = ResultsMode.MyPlaylists;

            var section = NewSection("My Playlists");
            if (playlists == null || playlists.Count == 0)
            {
                section.Message = playlists == null
                    ? "Failed to load playlists, try again"
                    : "No playlists in this account";
                return Current;
            }

            foreach (UserPlaylistInfo p in playlists)
                section.Rows.Add(new PanelRow
                {
                    Title = p.Name,
                    Sub = $"{p.TrackCount} tracks",
                    Action = RowAction.PlayContext($"spotify:playlist:{p.Id}"),
                });
            return Current;
        }

        // คำค้นถูกลบจนว่าง -> หุบพื้นที่ผลลัพธ์กลับ (รวมถึงกรณีที่โชว์ My Lists อยู่ - toggle ต้องกลับ
        // สถานะ "หุบ" ด้วย ไม่งั้นกดปุ่ม My Lists ครั้งถัดไปจะกลายเป็นสั่งหุบทั้งที่จอว่างอยู่แล้ว)
        public PanelState SearchCleared()
        {
            if (Current.ResultsMode == ResultsMode.Empty && Current.ResultsSections.Count == 0)
            {
                Current.NeedsReflow = false; // ไม่มีอะไรให้เคลียร์ ไม่ต้อง rebuild ฟรี
                return Current;
            }

            Current.ResultsMode = ResultsMode.Empty;
            Current.ResultsSections = new List<PanelSection>();
            Current.ResultsRevision++;
            Current.NeedsReflow = true;
            return Current;
        }

        // === helpers ===

        private PanelSection NewSection(string label)
        {
            var s = new PanelSection { Label = label };
            Current.ResultsSections.Add(s);
            return s;
        }

        // อยู่ตรงนี้เพราะเป็น logic ล้วนที่ทั้ง VM (สร้างช่องเวลาใน row) และ hot path
        // ของ injector (เข็มวินาทีทุกเฟรม) ใช้ร่วมกัน
        public static string FormatTime(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
            return $"{t.Minutes}:{t.Seconds:00}";
        }
    }
}
