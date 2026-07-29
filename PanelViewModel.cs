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
        ToggleArtistAlbums, // กาง/หุบรายการอัลบั้มของศิลปินใต้แถวนั้น
        TransferPlayback,   // ย้ายการเล่นไปอุปกรณ์ตัวนั้น (ไม่ได้เปลี่ยนเพลง/คิว)
    }

    // ทรงของรูปเล็กหน้าแถว - None = แถวนี้ไม่มีช่องรูปเลย (แถวคิวเพลงที่ทุกแถวใช้ปกเดียวกัน)
    // แถวที่มีช่องรูปจะกันที่ไว้เสมอแม้ยังไม่รู้ URL หรือไม่มีรูป (ศิลปินบางคนไม่มีรูปบน Spotify)
    // ไม่งั้นแถวนั้นจะเลื่อนไปชิดซ้ายคนเดียวจนคอลัมน์ในลิสต์ไม่ตรงกัน
    public enum RowImageShape { None, Square, Circle }

    public struct RowAction
    {
        public RowActionKind Kind;
        public string TrackId;
        public string ContextUri;
        public string AlbumId;
        public string AlbumName;
        public string AlbumCoverUrl;
        public string ArtistId;
        public string DeviceId;

        public static readonly RowAction None = new RowAction { Kind = RowActionKind.None };

        // แถวที่กดแล้วแค่กาง/หุบ ไม่ได้สั่งอะไรกับ Spotify - ไม่ต้องทาสีค้างว่า "เพิ่งกด"
        // (ลูกศร > / < ที่ท้ายแถวบอกสถานะให้อยู่แล้ว)
        public bool IsToggle => Kind == RowActionKind.ToggleArtistAlbums;
        public static RowAction PlayTrack(string trackId) =>
            new RowAction { Kind = RowActionKind.PlayTrack, TrackId = trackId };
        public static RowAction PlayContext(string contextUri) =>
            new RowAction { Kind = RowActionKind.PlayContext, ContextUri = contextUri };
        public static RowAction PlayTrackInContext(string contextUri, string trackId) =>
            new RowAction { Kind = RowActionKind.PlayTrackInContext, ContextUri = contextUri, TrackId = trackId };
        public static RowAction LoadAlbum(string albumId, string albumName, string coverUrl) =>
            new RowAction { Kind = RowActionKind.LoadAlbum, AlbumId = albumId, AlbumName = albumName, AlbumCoverUrl = coverUrl };
        public static RowAction ToggleArtistAlbums(string artistId) =>
            new RowAction { Kind = RowActionKind.ToggleArtistAlbums, ArtistId = artistId };
        public static RowAction TransferPlayback(string deviceId) =>
            new RowAction { Kind = RowActionKind.TransferPlayback, DeviceId = deviceId };
    }

    // แถวหนึ่งใน list (คิวเพลง / ผลค้นหา / My Lists) - เป็น data ล้วน renderer วาดตามนี้
    public class PanelRow
    {
        public string Index;   // เลขลำดับ ("1", "2", ...) - null = ไม่มีช่องเลข (แถวผลค้นหา)
        public string Title;
        public string Sub;     // บรรทัดรอง (ศิลปิน / "Artist" / "N tracks" / เจ้าของ playlist)
        public string Right;   // ช่องขวา (เวลาเพลง / ลูกศรกาง-หุบ) - null = ไม่มี
        public string TrackId; // ใช้จับคู่ highlight เพลงที่กำลังเล่น - null = แถวนี้ไม่เกี่ยว
        public RowAction Action;

        // ปุ่มเล่นวงกลมท้ายแถว - ทางลัดสั่งเล่นทั้ง artist/album/playlist โดยไม่ต้องกางเข้าไปเลือกเพลง
        // Kind = None (ค่าเริ่มต้น) = แถวนี้ไม่มีปุ่ม
        public RowAction PlayAction;

        // รูปเล็กหน้าแถว (ปกอัลบั้ม/ปก playlist/รูปศิลปิน)
        // VM ถือแค่ URL เพราะดาวน์โหลด/แคช/สร้าง texture เป็นเรื่องของฝั่ง Unity
        // ImageUrl = null ทั้งที่ ImageShape != None แปลว่า "มีช่องรูปแต่ไม่มีรูป" -> โชว์ placeholder
        public string ImageUrl;
        public RowImageShape ImageShape;

        public bool Indented; // แถวลูก (อัลบั้ม/เพลงที่กางออกมา) - เยื้องขวาให้เห็นลำดับชั้น
        public bool Muted;    // แถวข้อความสถานะ (กำลังโหลด/โหลดไม่ได้) ไม่ใช่ของกดได้

        // รหัสประจำแถวสำหรับไฮไลต์ "แถวที่เพิ่งกด" ในพื้นที่ผลลัพธ์ (เช่น "album:xxx")
        // null = แถวนี้กดแล้วไม่ต้องค้างสี (แถวคิวเพลง / แถวที่แค่กาง-หุบ / แถวข้อความ)
        public string Key;
    }

    // หมวดหนึ่งในพื้นที่ผลลัพธ์ (เช่น "TRACKS") - Message มีค่าเมื่อหมวดนี้เป็นข้อความแจ้งเฉยๆ
    public class PanelSection
    {
        public string Label;
        public string Message;
        public List<PanelRow> Rows = new List<PanelRow>();
    }

    // พื้นที่ผลลัพธ์ใต้แถบ search ตอนนี้โชว์อะไรอยู่
    public enum ResultsMode { Empty, SearchResults, MyPlaylists, Devices }

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

        // แถวในพื้นที่ผลลัพธ์ที่ผู้เล่นเพิ่งกด (ตาม PanelRow.Key) - ทาสีค้างไว้เป็นการยืนยันว่า
        // "กดติดแล้ว กำลังทำงานให้อยู่" เพราะผลของการกด (คิวเปลี่ยน/เพลงเปลี่ยน) มาช้ากว่านิ้ว
        public string SelectedRowKey;

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

        // ผลค้นหาชุดล่าสุด - เก็บไว้เพราะการกาง/หุบอัลบั้มของศิลปินต้องประกอบ section ใหม่ทั้งชุด
        private SpotifySearchResults _lastSearchResults;

        // รายชื่อ playlist ของ user ชุดล่าสุด - เหตุผลเดียวกับ _lastSearchResults (กาง/หุบต้องประกอบใหม่)
        private List<UserPlaylistInfo> _lastMyPlaylists;

        // รายการอุปกรณ์ชุดล่าสุดที่ดึงมา - เหตุผลเดียวกับสองตัวบน
        private List<SpotifyDeviceInfo> _lastDevices;

        // ศิลปินที่กางรายการอัลบั้มอยู่ (null = ไม่มีใครกาง) พร้อมของที่จะแสดงใต้แถวนั้น
        private string _expandedArtistId;
        private List<ArtistAlbumInfo> _expandedAlbums;
        private string _expandedMessage; // ข้อความแทนรายการ (กำลังโหลด / โหลดไม่ได้) - null = มีรายการจริง

        // หมายเหตุ: แถว playlist ตั้งใจไม่มีการกางรายชื่อเพลง - กดแล้วสั่งเล่นทั้งชุดไปเลย
        // (Spotify ตัด track list ออกจาก response ของ playlist ส่วนใหญ่อยู่แล้ว กางไปก็มักได้ที่ว่าง)

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
            _lastSearchResults = null;
            _lastMyPlaylists = null;
            _lastDevices = null;
            CollapseArtist();
            return Current;
        }

        // ผู้เล่นกดแถวหนึ่งในพื้นที่ผลลัพธ์ - ทาสีค้างไว้ให้รู้ว่ากดติดแล้ว
        // (ผลจริงของการกดมาทีหลังเสมอ: โหลดรายชื่อเพลง / รอ Spotify สลับเพลง)
        public PanelState RowSelected(string key)
        {
            Current.NeedsReflow = false;
            Current.SelectedRowKey = key;
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

            AddQueueRows(playlist.Tracks, playlist.ContextUri);
            return Current;
        }

        // คิวเลื่อนหน้าต่าง: ยังเล่นอยู่ใน context เดิม แต่เพลงที่เล่นอยู่เดินเลยแถวสุดท้ายบนจอไปแล้ว
        // (playlist ยาวกว่าที่ดึงมาได้ - พอถึงเพลงที่ 22 ก็ไม่มีแถวไหนถูกไฮไลต์อีก) -> เปลี่ยนเฉพาะ
        // แถวเพลง ส่วน header (ชื่อ/ปก/PLAYING FROM) คงเดิมไว้ เพราะ context ไม่ได้เปลี่ยนไปไหน
        public PanelState QueueWindowLoaded(List<PlaylistTrackInfo> tracks, string contextUri)
        {
            if (tracks == null || tracks.Count == 0)
            {
                // ไม่ได้คิวชุดใหม่มา - ปล่อยแถวเดิมค้างไว้ ดีกว่าล้างจนว่างเปล่า
                Current.NeedsReflow = false;
                return Current;
            }

            Current.QueueRows = new List<PanelRow>();
            Current.QueueMessage = null;
            Current.QueueRevision++;
            Current.NeedsReflow = true;
            AddQueueRows(tracks, contextUri);
            return Current;
        }

        // แถวคิวหนึ่งชุด - เลขลำดับเริ่มที่ 1 เสมอ (คิวที่มาจาก /me/player/queue ไม่รู้ตำแหน่งจริง
        // ใน context อยู่แล้ว แถวแรกคือเพลงที่กำลังเล่น)
        private void AddQueueRows(List<PlaylistTrackInfo> tracks, string contextUri)
        {
            bool isArtist = SpotifyContext.IsArtist(contextUri);
            for (int i = 0; i < tracks.Count; i++)
            {
                PlaylistTrackInfo t = tracks[i];
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
                        ? RowAction.PlayTrackInContext(contextUri, t.Id)
                        : RowAction.None,
                });
            }
        }

        // ผลค้นหามาถึง - แทนที่พื้นที่ผลลัพธ์ทั้งหมด (รวมถึงปิดโหมด My Lists ที่อาจเปิดอยู่)
        public PanelState SearchResultsArrived(SpotifySearchResults results)
        {
            _lastSearchResults = results;
            _lastMyPlaylists = null;
            _lastDevices = null;
            CollapseArtist(); // ผลชุดใหม่แล้ว - อัลบั้มที่กางค้างจากคำค้นก่อนไม่เกี่ยวกันแล้ว
            Current.SelectedRowKey = null;
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
            BuildSearchSections();
            return Current;
        }

        // กดแถวศิลปิน: กางรายการอัลบั้มใต้แถวนั้น / กดซ้ำที่คนเดิมคือหุบ
        // คืน true = ผู้เรียกต้องไปดึงรายการอัลบั้มมาแล้วเรียก ArtistAlbumsArrived ต่อ
        // (ระหว่างรอ แถวจะโชว์ "Loading albums…" ไว้ก่อน ผู้เรียกจึงควร Apply ทันทีทั้งสองทาง)
        public bool ArtistToggled(string artistId)
        {
            if (string.IsNullOrEmpty(artistId) || _lastSearchResults == null)
            {
                Current.NeedsReflow = false;
                return false;
            }

            if (_expandedArtistId == artistId)
            {
                CollapseArtist();
                RebuildResults();
                return false;
            }

            // กางคนใหม่ = หุบคนเก่าไปในตัว (กางได้ทีละคน ไม่งั้นลิสต์ยาวจนหาของไม่เจอ)
            _expandedArtistId = artistId;
            _expandedAlbums = null;
            _expandedMessage = "Loading albums…";
            RebuildResults();
            return true;
        }

        // รายการอัลบั้มของศิลปินมาถึง (null = โหลดพลาด/endpoint ไม่ให้ใช้)
        // ผลของศิลปินที่ไม่ใช่คนที่กางอยู่แล้ว (ผู้ใช้หุบหรือกดคนอื่นระหว่างรอ) ให้ทิ้งไปเฉยๆ
        public PanelState ArtistAlbumsArrived(string artistId, List<ArtistAlbumInfo> albums)
        {
            if (_expandedArtistId == null || _expandedArtistId != artistId)
            {
                Current.NeedsReflow = false;
                return Current;
            }

            _expandedAlbums = albums;
            _expandedMessage =
                albums == null ? "Album list not available for this account" :
                albums.Count == 0 ? "No albums found" : null;
            RebuildResults();
            return Current;
        }

        // ประกอบ section ของผลค้นหาใหม่ทั้งชุดจาก _lastSearchResults + สถานะกาง/หุบปัจจุบัน
        // (เรียกทั้งตอนผลค้นหามาถึงและตอนกาง/หุบอัลบั้ม - แถวจึงมาจากที่เดียวเสมอ)
        private void BuildSearchSections()
        {
            SpotifySearchResults results = _lastSearchResults;
            if (results == null) return;

            if (results.Tracks.Count > 0)
            {
                var s = NewSection("Tracks");
                foreach (SearchTrackResult t in results.Tracks)
                    s.Rows.Add(new PanelRow
                    {
                        Title = t.Title,
                        Sub = t.Artist,
                        Right = FormatTime(TimeSpan.FromMilliseconds(t.DurationMs)),
                        ImageUrl = t.AlbumCoverUrl,
                        ImageShape = RowImageShape.Square,
                        Key = TrackKey(t.Id),
                        Action = RowAction.PlayTrack(t.Id),
                    });
            }

            if (results.Artists.Count > 0)
            {
                var s = NewSection("Artists");
                foreach (SearchArtistResult a in results.Artists)
                {
                    bool expanded = a.Id != null && a.Id == _expandedArtistId;
                    s.Rows.Add(new PanelRow
                    {
                        Title = a.Name,
                        Sub = "Artist",
                        Right = expanded ? "<" : ">", // ตัวบอกสถานะกาง/หุบ (กดที่แถวไหนก็ได้ทั้งแถว)
                        ImageUrl = a.ImageUrl,
                        ImageShape = RowImageShape.Circle,
                        Key = ArtistKey(a.Id),
                        Action = RowAction.ToggleArtistAlbums(a.Id),
                        // ทางลัด: ไม่อยากไล่ดูอัลบั้ม กดปุ่มนี้ให้เล่นเพลงของศิลปินคนนี้ไปเลย
                        PlayAction = RowAction.PlayContext($"spotify:artist:{a.Id}"),
                    });

                    if (expanded)
                        AddExpandedAlbumRows(s);
                }
            }

            if (results.Albums.Count > 0)
            {
                var s = NewSection("Albums");
                foreach (SearchAlbumResult al in results.Albums)
                    s.Rows.Add(new PanelRow
                    {
                        Title = al.Name,
                        Sub = al.ArtistName,
                        ImageUrl = al.CoverUrl,
                        ImageShape = RowImageShape.Square,
                        Key = AlbumKey(al.Id),
                        // กดแถว = เอารายชื่อเพลงมาลงพื้นที่คิวให้เลือกเอง / กดปุ่ม = เล่นทั้งอัลบั้มเลย
                        Action = RowAction.LoadAlbum(al.Id, al.Name, al.CoverUrl),
                        PlayAction = RowAction.PlayContext($"spotify:album:{al.Id}"),
                    });
            }

            if (results.Playlists.Count > 0)
            {
                var s = NewSection("Playlists");
                foreach (SearchPlaylistResult p in results.Playlists)
                    s.Rows.Add(new PanelRow
                    {
                        Title = p.Name,
                        Sub = p.OwnerName ?? "-",
                        ImageUrl = p.CoverUrl,
                        ImageShape = RowImageShape.Square,
                        Key = PlaylistKey(p.Id),
                        Action = RowAction.PlayContext($"spotify:playlist:{p.Id}"),
                    });
            }
        }

        // แถวลูกใต้ศิลปินที่กางอยู่ - รายการอัลบั้ม หรือข้อความสถานะเมื่อยังไม่มีรายการ
        private void AddExpandedAlbumRows(PanelSection s)
        {
            if (_expandedMessage != null)
            {
                s.Rows.Add(new PanelRow { Title = _expandedMessage, Indented = true, Muted = true });
                return;
            }

            foreach (ArtistAlbumInfo al in _expandedAlbums)
            {
                string tracks = $"{al.TrackCount} tracks";
                s.Rows.Add(new PanelRow
                {
                    Title = al.Name,
                    Sub = al.ReleaseYear != null ? $"{al.ReleaseYear} · {tracks}" : tracks,
                    ImageUrl = al.CoverUrl,
                    ImageShape = RowImageShape.Square,
                    Indented = true,
                    Key = AlbumKey(al.Id),
                    // กดแล้วเอารายชื่อเพลงมาลงพื้นที่คิว เหมือนกดอัลบั้มจากหมวด Albums
                    Action = RowAction.LoadAlbum(al.Id, al.Name, al.CoverUrl),
                    PlayAction = RowAction.PlayContext($"spotify:album:{al.Id}"),
                });
            }
        }

        private void BuildMyPlaylistSection()
        {
            var section = NewSection("My Playlists");
            if (_lastMyPlaylists == null || _lastMyPlaylists.Count == 0)
            {
                section.Message = _lastMyPlaylists == null
                    ? "Failed to load playlists, try again"
                    : "No playlists in this account";
                return;
            }

            foreach (UserPlaylistInfo p in _lastMyPlaylists)
                section.Rows.Add(new PanelRow
                {
                    Title = p.Name,
                    // /me/playlists คืน tracks.total = 0 มาให้เป็นประจำ - โชว์ "0 tracks" มีแต่ทำให้
                    // เข้าใจผิดว่า playlist ว่าง เลยซ่อนบรรทัดรองไปเลยเมื่อไม่รู้จำนวนจริง
                    Sub = p.TrackCount > 0 ? $"{p.TrackCount} tracks" : null,
                    ImageUrl = p.ImageUrl,
                    ImageShape = RowImageShape.Square,
                    Key = PlaylistKey(p.Id),
                    Action = RowAction.PlayContext($"spotify:playlist:{p.Id}"),
                });
        }

        // รายการอุปกรณ์: เครื่องที่กำลังเล่นอยู่บอกสถานะไว้เฉยๆ กดไม่ได้ (ย้ายไปตัวเองไม่มีความหมาย)
        // เครื่องที่ Spotify ห้ามควบคุมผ่าน Web API ก็กดไม่ได้เหมือนกัน - บอกเหตุผลไว้ที่บรรทัดรอง
        // ดีกว่าปล่อยให้กดแล้วเงียบ
        private void BuildDeviceSection()
        {
            var section = NewSection("Devices");
            if (_lastDevices == null || _lastDevices.Count == 0)
            {
                section.Message = _lastDevices == null
                    ? "Failed to load devices, try again"
                    : "No devices found - open Spotify on this PC or your phone first";
                return;
            }

            foreach (SpotifyDeviceInfo d in _lastDevices)
            {
                bool clickable = !d.IsActive && !d.IsRestricted && !string.IsNullOrEmpty(d.Id);
                string type = string.IsNullOrEmpty(d.Type) ? "Device" : d.Type;

                section.Rows.Add(new PanelRow
                {
                    Title = d.Name ?? "-",
                    Sub = d.IsActive ? $"{type} · Playing here"
                        : d.IsRestricted ? $"{type} · Can't be controlled from here"
                        : type,
                    // ระดับเสียงเป็นข้อมูลช่วยแยกเครื่องที่ชื่อคล้ายกัน (เครื่องบางตัวไม่รายงานมา)
                    Right = d.VolumePercent != null ? $"{d.VolumePercent}%" : null,
                    // จางเฉพาะเครื่องที่สั่งไม่ได้ - เครื่องที่เล่นอยู่กดไม่ได้เหมือนกันแต่เป็นแถวสำคัญที่สุด
                    // ในลิสต์ ไม่ควรจางกว่าคนอื่น
                    Muted = d.IsRestricted,
                    Key = DeviceKey(d.Id),
                    Action = clickable ? RowAction.TransferPlayback(d.Id) : RowAction.None,
                });

                // ทาเขียวเครื่องที่เล่นอยู่ ผ่านช่องทางเดียวกับ "แถวที่เพิ่งกด" - เห็นปุ๊บรู้ทันทีว่า
                // เสียงออกที่ไหน และหลังย้ายเครื่องสำเร็จ สีก็ย้ายตามเองตอนดึงรายการใหม่
                if (d.IsActive) Current.SelectedRowKey = DeviceKey(d.Id);
            }
        }

        private void CollapseArtist()
        {
            _expandedArtistId = null;
            _expandedAlbums = null;
            _expandedMessage = null;
        }

        // ประกอบพื้นที่ผลลัพธ์ใหม่ทั้งชุดตามโหมดที่โชว์อยู่ (ผลค้นหา / My Lists)
        private void RebuildResults()
        {
            Current.ResultsSections = new List<PanelSection>();
            Current.ResultsRevision++;
            Current.NeedsReflow = true;

            if (Current.ResultsMode == ResultsMode.SearchResults) BuildSearchSections();
            else if (Current.ResultsMode == ResultsMode.MyPlaylists) BuildMyPlaylistSection();
            else if (Current.ResultsMode == ResultsMode.Devices) BuildDeviceSection();
        }

        // กดปุ่ม My Lists: ถ้ากำลังโชว์อยู่ -> หุบ (ผู้เรียกไม่ต้อง fetch)
        // ถ้ายังไม่โชว์ -> คืน true ให้ผู้เรียกไป fetch แล้วค่อยเรียก MyPlaylistsArrived
        public bool MyListsClicked()
        {
            // ไม่ว่าจะกางหรือหุบ พื้นที่ผลลัพธ์ก็ไม่ใช่ของผลค้นหาแล้ว - ทิ้งสถานะกางอัลบั้มไปด้วย
            _lastSearchResults = null;
            _lastDevices = null;
            CollapseArtist();
            Current.SelectedRowKey = null;

            if (Current.ResultsMode == ResultsMode.MyPlaylists)
            {
                _lastMyPlaylists = null;
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
            _lastMyPlaylists = playlists;
            // นับเป็นโหมด My Lists แม้โหลดพลาด - กดปุ่มซ้ำจะได้หุบข้อความ error ได้เหมือนรายการปกติ
            Current.ResultsMode = ResultsMode.MyPlaylists;
            RebuildResults();
            return Current;
        }

        // กดปุ่ม Devices: toggle เหมือน My Lists (กางอยู่ -> หุบ / ยังไม่กาง -> คืน true ให้ผู้เรียกไป fetch)
        // ไม่มี cache เลยแม้แต่ชั้นเดียว - รายการอุปกรณ์เปลี่ยนตลอด (เปิด/ปิดแอปบนมือถือ ลำโพงหลุด wifi)
        // กดดูทีไรต้องเป็นของจริง ณ ตอนนั้น
        public bool DevicesClicked()
        {
            _lastSearchResults = null;
            _lastMyPlaylists = null;
            CollapseArtist();
            Current.SelectedRowKey = null;

            if (Current.ResultsMode == ResultsMode.Devices)
            {
                _lastDevices = null;
                Current.ResultsMode = ResultsMode.Empty;
                Current.ResultsSections = new List<PanelSection>();
                Current.ResultsRevision++;
                Current.NeedsReflow = true;
                return false;
            }

            Current.NeedsReflow = false;
            return true;
        }

        // รายการอุปกรณ์มาถึง (null = โหลดพลาด) - นับเป็นโหมด Devices ทั้งสองทาง เพื่อให้กดปุ่มซ้ำ
        // หุบข้อความ error ได้เหมือนรายการปกติ (เหตุผลเดียวกับ MyPlaylistsArrived)
        public PanelState DevicesArrived(List<SpotifyDeviceInfo> devices)
        {
            _lastDevices = devices;
            Current.ResultsMode = ResultsMode.Devices;
            RebuildResults();
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

            _lastSearchResults = null;
            _lastMyPlaylists = null;
            _lastDevices = null;
            CollapseArtist();
            Current.SelectedRowKey = null;
            Current.ResultsMode = ResultsMode.Empty;
            Current.ResultsSections = new List<PanelSection>();
            Current.ResultsRevision++;
            Current.NeedsReflow = true;
            return Current;
        }

        // === helpers ===

        // รหัสแถวสำหรับไฮไลต์ "แถวที่เพิ่งกด" - ต้องไม่ชนกันข้ามชนิดของแถว
        private static string TrackKey(string id) => id != null ? "track:" + id : null;
        private static string AlbumKey(string id) => id != null ? "album:" + id : null;
        private static string ArtistKey(string id) => id != null ? "artist:" + id : null;
        private static string PlaylistKey(string id) => id != null ? "playlist:" + id : null;
        private static string DeviceKey(string id) => id != null ? "device:" + id : null;

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
