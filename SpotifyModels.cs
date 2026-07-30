// SpotifyModels.cs
// DTO ล้วนของชั้น Spotify ทั้งหมด - จงใจให้พึ่งแค่ System เท่านั้น (ห้าม UnityEngine / Newtonsoft)
// เพราะไฟล์นี้ถูก link-compile เข้า test project (net10.0 + xUnit) เพื่อทดสอบ logic
// ที่กินข้อมูลพวกนี้ (NowPlayingSession, PanelViewModel) โดยไม่ต้องเปิดเกม
// ตัว parse JSON -> DTO อยู่กับ API แต่ละไฟล์ตามเดิม (SpotifyApi / SpotifyWebApi / SpotifySearchApi)
using System;
using System.Collections.Generic;

namespace ChillWithYou_SpotifyMod
{
    // โหมดเล่นซ้ำ - "context" = ซ้ำทั้ง playlist/album ที่เล่นอยู่ / "track" = ซ้ำเพลงเดียว
    // การแปลงไป/กลับค่าสตริงของ Spotify เป็นเรื่องของ SpotifyApi (ไฟล์นี้ห้ามรู้จัก wire format)
    public enum RepeatMode { Off, Context, Track }

    public static class RepeatModes
    {
        // ลำดับการกดวน: ปิด -> ซ้ำทั้งชุด -> ซ้ำเพลงเดียว -> ปิด (ลำดับเดียวกับแอป Spotify เอง)
        public static RepeatMode Next(RepeatMode current) =>
            current == RepeatMode.Off ? RepeatMode.Context :
            current == RepeatMode.Context ? RepeatMode.Track : RepeatMode.Off;
    }

    public class SpotifyNowPlayingInfo
    {
        public string TrackId;
        public string Title;
        public string Artist;
        public bool IsPlaying;
        public TimeSpan Position;
        public TimeSpan Duration;
        public byte[] ThumbnailBytes; // ปกอัลบั้ม โหลดจาก URL ของ Spotify
        public string PlaylistContextId; // parse จาก context.uri ของ /me/player call เดียวกันนี้เลย
                                         // ไม่ต้องยิง endpoint แยกเพื่อเช็คว่า playlist เปลี่ยนไหม
                                         // null เมื่อเล่นจาก context ที่ไม่ใช่ playlist (artist/album) - ดู ContextUri
        public string ContextUri;        // context.uri ดิบ เช่น spotify:artist:xxx / spotify:album:xxx
                                         // ใช้เช็คว่า context เปลี่ยนไหม แทน PlaylistContextId ที่เห็นแค่ playlist

        // ระดับเสียงของอุปกรณ์ที่เล่นอยู่ - null เมื่อไม่มีอุปกรณ์/ไม่รายงานมา
        public int? VolumePercent;
        // false = อุปกรณ์นี้สั่งระดับเสียงผ่าน Web API ไม่ได้ (เครื่องเล่นบางรุ่น/ลำโพงบางยี่ห้อ)
        // -> ซ่อนแถบเสียงไปเลย ดีกว่าให้ลากแล้วไม่มีอะไรเกิดขึ้น
        public bool SupportsVolume;

        public bool ShuffleOn;
        public RepeatMode RepeatMode;
    }

    public class PlaylistTrackInfo
    {
        public string Id;
        public string Title;
        public string Artist;
        public int DurationMs;
    }

    public class PlaylistInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public byte[] CoverImageBytes;
        public List<PlaylistTrackInfo> Tracks;

        // ใช้เป็น context_uri ตอนสั่งเล่นเพลงจากแถวใน list (spotify:playlist:xxx หรือ spotify:album:xxx)
        // ถ้าเป็น null จะ fallback ไปเล่นทีละเพลงแบบไม่มี context แทน
        public string ContextUri { get; set; }
    }

    // รายการ playlist ของ user เองจาก /me/playlists (ใช้แสดงเป็นเมนูให้กดเลือกเล่น)
    public class UserPlaylistInfo
    {
        public string Id;
        public string Name;
        public int TrackCount;
        public string ImageUrl; // ปกของ playlist สำหรับรูปเล็กหน้าแถว - null เมื่อ playlist ไม่มีปก
    }

    // อัลบั้มหนึ่งของศิลปิน (GET /artists/{id}/albums) - ใช้กับรายการที่กางออกมาใต้แถวศิลปินในผลค้นหา
    public class ArtistAlbumInfo
    {
        public string Id;
        public string Name;
        public int TrackCount;
        public string CoverUrl;
        public string ReleaseYear; // "2003" - null เมื่อ Spotify ไม่ส่ง release_date มา
    }

    // อุปกรณ์หนึ่งตัวที่ Spotify มองเห็นอยู่ (GET /me/player/devices)
    // ใช้ทั้งโชว์รายการให้เลือก และสั่งย้ายการเล่นไปเครื่องนั้น (PUT /me/player)
    public class SpotifyDeviceInfo
    {
        public string Id;
        public string Name;
        public string Type;         // "Computer" / "Smartphone" / "Speaker" ... ตามที่ Spotify ส่งมา
        public bool IsActive;       // เครื่องที่กำลังเล่นอยู่ตอนนี้
        public bool IsRestricted;   // Spotify ห้ามสั่งควบคุมเครื่องนี้ผ่าน Web API -> ย้ายไปไม่ได้
        public int? VolumePercent;  // null เมื่อเครื่องไม่รายงานระดับเสียงมา
    }

    public class SearchTrackResult
    {
        public string Id;
        public string Title;
        public string Artist;
        public int DurationMs;
        public string AlbumCoverUrl;
    }

    public class SearchArtistResult
    {
        public string Id;
        public string Name;
        public string ImageUrl;
    }

    public class SearchAlbumResult
    {
        public string Id;
        public string Name;
        public string ArtistName;
        public string CoverUrl;
    }

    public class SearchPlaylistResult
    {
        public string Id;
        public string Name;
        public string OwnerName;
        public string CoverUrl;
    }

    public class SpotifySearchResults
    {
        public List<SearchTrackResult> Tracks = new List<SearchTrackResult>();
        public List<SearchArtistResult> Artists = new List<SearchArtistResult>();
        public List<SearchAlbumResult> Albums = new List<SearchAlbumResult>();
        public List<SearchPlaylistResult> Playlists = new List<SearchPlaylistResult>();
    }

    // ตัวช่วยอ่าน context uri ("spotify:album:xxx") - ใช้ร่วมกันทั้ง PanelViewModel และ injector
    public static class SpotifyContext
    {
        public static bool IsArtist(string contextUri) =>
            !string.IsNullOrEmpty(contextUri) && contextUri.StartsWith("spotify:artist:");

        // Liked Songs ("spotify:user:xxx:collection") - อ่านรายชื่อเพลงตรงๆ ไม่ได้ (/me/tracks
        // โดน 403 ใน development mode) เลยแสดงผ่านคิวแบบเดียวกับ artist/album
        public static bool IsCollection(string contextUri) =>
            !string.IsNullOrEmpty(contextUri) &&
            contextUri.StartsWith("spotify:user:") && contextUri.EndsWith(":collection");

        // แถวคิวของ context นี้กดเล่นไม่ได้: artist ไม่รับ offset ส่วน collection ไม่แน่ว่ารับ
        // (ทดลองไม่ได้เพราะอ่าน track list ไม่ได้อยู่แล้ว) - แถวที่กดแล้วเงียบแย่กว่าแถวดูเฉยๆ
        public static bool RowsViewOnly(string contextUri) =>
            IsArtist(contextUri) || IsCollection(contextUri);

        // "spotify:album:xxx" -> "ALBUM" / คืน null เมื่อไม่มี context uri หรือเป็นชนิดที่ไม่รู้จัก
        // (ให้ผู้เรียกซ่อน label ไปเลย ดีกว่าเดาผิดแล้วบอกผู้เล่นว่ากำลังเล่นจากอะไรที่ไม่จริง)
        public static string KindLabel(string contextUri)
        {
            if (string.IsNullOrEmpty(contextUri)) return null;
            if (contextUri.StartsWith("spotify:playlist:")) return "PLAYLIST";
            if (contextUri.StartsWith("spotify:album:")) return "ALBUM";
            if (contextUri.StartsWith("spotify:artist:")) return "ARTIST";
            if (IsCollection(contextUri)) return "LIKED SONGS";
            return null;
        }
    }
}
