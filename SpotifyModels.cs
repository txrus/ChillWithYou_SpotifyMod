// SpotifyModels.cs
// DTO ล้วนของชั้น Spotify ทั้งหมด - จงใจให้พึ่งแค่ System เท่านั้น (ห้าม UnityEngine / Newtonsoft)
// เพราะไฟล์นี้ถูก link-compile เข้า test project (net10.0 + xUnit) เพื่อทดสอบ logic
// ที่กินข้อมูลพวกนี้ (NowPlayingSession, PanelViewModel) โดยไม่ต้องเปิดเกม
// ตัว parse JSON -> DTO อยู่กับ API แต่ละไฟล์ตามเดิม (SpotifyApi / SpotifyWebApi / SpotifySearchApi)
using System;
using System.Collections.Generic;

namespace ChillWithYou_SpotifyMod
{
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

        // "spotify:album:xxx" -> "ALBUM" / คืน null เมื่อไม่มี context uri หรือเป็นชนิดที่ไม่รู้จัก
        // (ให้ผู้เรียกซ่อน label ไปเลย ดีกว่าเดาผิดแล้วบอกผู้เล่นว่ากำลังเล่นจากอะไรที่ไม่จริง)
        public static string KindLabel(string contextUri)
        {
            if (string.IsNullOrEmpty(contextUri)) return null;
            if (contextUri.StartsWith("spotify:playlist:")) return "PLAYLIST";
            if (contextUri.StartsWith("spotify:album:")) return "ALBUM";
            if (contextUri.StartsWith("spotify:artist:")) return "ARTIST";
            return null;
        }
    }
}
