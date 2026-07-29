// เทสต์ PanelViewModel - state machine ของ panel ทั้ง visibility/reflow/เนื้อหา list
// รันบน .NET SDK ปกติโดยไม่ต้องมีเกม/Unity (ดูเหตุผลใน csproj)
// หลายเคสในนี้คือบั๊กจริงจาก v1.1.2 ที่เดิมต้องเปิดเกม + login + เปิดเพลงจริงถึงจะเจอ
using System;
using System.Collections.Generic;
using System.Linq;
using ChillWithYou_SpotifyMod;
using Xunit;

namespace ChillWithYou_SpotifyMod.Tests
{
    public class PanelViewModelTests
    {
        private static SpotifyNowPlayingInfo Track(string id = "t1", string title = "Song", bool playing = true) =>
            new SpotifyNowPlayingInfo
            {
                TrackId = id,
                Title = title,
                Artist = "Artist",
                IsPlaying = playing,
                Position = TimeSpan.FromSeconds(10),
                Duration = TimeSpan.FromMinutes(3),
            };

        private static PlaylistInfo Playlist(string contextUri = "spotify:playlist:p1", int tracks = 2)
        {
            var list = new List<PlaylistTrackInfo>();
            for (int i = 0; i < tracks; i++)
                list.Add(new PlaylistTrackInfo { Id = $"t{i + 1}", Title = $"Track {i + 1}", Artist = "A", DurationMs = 200000 });
            return new PlaylistInfo { Id = "p1", Name = "My Mix", ContextUri = contextUri, Tracks = list };
        }

        // === สถานะเริ่มต้น ===

        [Fact]
        public void ResetForInject_NotLoggedIn_ShowsOnlyConnectRow()
        {
            var vm = new PanelViewModel();
            PanelState s = vm.ResetForInject(loggedIn: false);

            Assert.True(s.ConnectRowVisible);
            Assert.False(s.ControlsRowVisible);
            Assert.False(s.PlaylistHeaderVisible);
            Assert.False(s.QueueListVisible);
            Assert.False(s.SearchRowVisible);
            Assert.Equal(PanelState.IdleTitle, s.TrackTitle);
            Assert.True(s.NeedsReflow); // UI ชุดใหม่ต้องจัด layout เสมอ
        }

        [Fact]
        public void ResetForInject_LoggedIn_ShowsPlayerRows()
        {
            var vm = new PanelViewModel();
            PanelState s = vm.ResetForInject(loggedIn: true);

            Assert.False(s.ConnectRowVisible);
            Assert.True(s.ControlsRowVisible);
            Assert.True(s.PlaylistHeaderVisible);
            Assert.True(s.QueueListVisible);
            Assert.True(s.SearchRowVisible);
        }

        // re-inject (เกม destroy panel เก่าแล้วสร้างใหม่) ต้องล้าง state ค้างทั้งหมด - บั๊ก toggle
        // My Lists ค้างข้าม panel เคยเกิดจริงมาแล้วหนึ่งรอบ
        [Fact]
        public void ResetForInject_ClearsStaleMyListsToggle()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.MyPlaylistsArrived(new List<UserPlaylistInfo> { new UserPlaylistInfo { Id = "x", Name = "L", TrackCount = 3 } });

            vm.ResetForInject(loggedIn: true);

            Assert.Equal(ResultsMode.Empty, vm.Current.ResultsMode);
            Assert.True(vm.MyListsClicked()); // กดครั้งแรกหลัง re-inject ต้อง "ขอ fetch" ไม่ใช่ "หุบ"
        }

        // === login ===

        // Regression: บั๊กจริงจาก smoke test v1.1.2 - หลัง Connect สำเร็จ แถวใหม่โผล่แต่ไม่ reflow
        // แถวเพลงของเกมเลยวาดทับ playlist header กับแถบ search
        [Fact]
        public void LoginSucceeded_SwapsRowsAndDemandsReflow()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: false);
            PanelState s = vm.LoginSucceeded();

            Assert.False(s.ConnectRowVisible);
            Assert.True(s.ControlsRowVisible);
            Assert.True(s.PlaylistHeaderVisible);
            Assert.True(s.QueueListVisible);
            Assert.True(s.SearchRowVisible);
            Assert.True(s.NeedsReflow);
        }

        [Fact]
        public void LoginFailed_ShowsError_ConnectRowStays()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: false);
            PanelState s = vm.LoginFailed("boom");

            Assert.Equal("Connect failed: boom", s.StatusText);
            Assert.True(s.ConnectRowVisible);
            Assert.False(s.NeedsReflow);
        }

        [Fact]
        public void ConnectClicked_ClearsPreviousError()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: false);
            vm.LoginFailed("boom");
            Assert.Equal("", vm.ConnectClicked().StatusText);
        }

        // === now-playing ===

        [Fact]
        public void NowPlayingUpdated_Null_ShowsIdlePromptAndClearsHighlight()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.NowPlayingUpdated(Track());
            PanelState s = vm.NowPlayingUpdated(null);

            Assert.Equal(PanelState.IdleTitle, s.TrackTitle);
            Assert.Equal("", s.TrackArtist);
            Assert.True(s.ShowIdleProgress);
            Assert.Null(s.HighlightedTrackId);
            Assert.False(s.NeedsReflow); // ข้อความเปลี่ยนเฉยๆ โครงสร้างเท่าเดิม
        }

        [Fact]
        public void NowPlayingUpdated_SetsTextsGlyphAndHighlight()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            PanelState s = vm.NowPlayingUpdated(Track(id: "abc", playing: false));

            Assert.Equal("Song", s.TrackTitle);
            Assert.Equal("Artist", s.TrackArtist);
            Assert.Equal(">", s.PlayPauseGlyph); // หยุดอยู่ -> ปุ่มโชว์สามเหลี่ยม play
            Assert.False(s.ShowIdleProgress);
            Assert.Equal("abc", s.HighlightedTrackId);
        }

        [Fact]
        public void NowPlayingUpdated_DoesNotBumpQueueRevision()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.ContextLoaded(Playlist());
            int rev = vm.Current.QueueRevision;

            vm.NowPlayingUpdated(Track(id: "t2"));

            Assert.Equal(rev, vm.Current.QueueRevision); // เพลงเปลี่ยน = ทาสีใหม่พอ ไม่ rebuild ทั้งคิว
            Assert.Equal("t2", vm.Current.HighlightedTrackId);
        }

        [Fact]
        public void LocalPlayPauseToggled_FlipsGlyphOnly()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.NowPlayingUpdated(Track(playing: true));

            Assert.Equal(">", vm.LocalPlayPauseToggled(false).PlayPauseGlyph);
            Assert.Equal("||", vm.LocalPlayPauseToggled(true).PlayPauseGlyph);
        }

        // === context / คิวเพลง ===

        [Fact]
        public void ContextLoaded_Playlist_BuildsClickableRowsAndHeader()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            PanelState s = vm.ContextLoaded(Playlist(tracks: 3));

            Assert.Equal("My Mix", s.HeaderName);
            Assert.Equal("PLAYING FROM PLAYLIST", s.HeaderSubLabel);
            Assert.True(s.HeaderCoverVisible);
            Assert.Equal(3, s.QueueRows.Count);
            Assert.True(s.NeedsReflow);

            PanelRow first = s.QueueRows[0];
            Assert.Equal("1", first.Index);
            Assert.Equal("t1", first.TrackId);
            Assert.Equal(RowActionKind.PlayTrackInContext, first.Action.Kind);
            Assert.Equal("spotify:playlist:p1", first.Action.ContextUri);
            Assert.Equal("3:20", first.Right); // 200000ms
        }

        // artist context: Spotify ไม่รับ offset -> แถวต้องกดไม่ได้ และไม่มีปก (แสดงคิวเฉยๆ)
        [Fact]
        public void ContextLoaded_Artist_RowsUnclickableAndNoCover()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            PanelState s = vm.ContextLoaded(Playlist(contextUri: "spotify:artist:a1"));

            Assert.Equal("PLAYING FROM ARTIST", s.HeaderSubLabel);
            Assert.False(s.HeaderCoverVisible);
            Assert.All(s.QueueRows, r => Assert.Equal(RowActionKind.None, r.Action.Kind));
        }

        [Fact]
        public void ContextLoaded_EmptyTracks_ShowsUnavailableMessage()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            PanelState s = vm.ContextLoaded(Playlist(tracks: 0));

            Assert.Empty(s.QueueRows);
            Assert.Equal("Track list not available for this playlist", s.QueueMessage);
        }

        [Fact]
        public void ContextLoaded_Null_ClearsQueueAndHeader()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.ContextLoaded(Playlist());
            PanelState s = vm.ContextLoaded(null);

            Assert.Equal("Not playing from a playlist", s.HeaderName);
            Assert.Null(s.HeaderSubLabel);
            Assert.Empty(s.QueueRows);
            Assert.True(s.NeedsReflow);
        }

        [Fact]
        public void ContextLoaded_BumpsQueueRevisionEachTime()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            int r0 = vm.Current.QueueRevision;
            int r1 = vm.ContextLoaded(Playlist()).QueueRevision;
            int r2 = vm.ContextLoaded(Playlist()).QueueRevision;

            Assert.NotEqual(r0, r1);
            Assert.NotEqual(r1, r2); // ผู้เรียกยิงเฉพาะตอน context เปลี่ยน - เนื้อหาถือว่าใหม่เสมอ
        }

        // เพลงไม่มี id (local file) กดไม่ได้ แต่ยังโชว์ในคิว
        [Fact]
        public void ContextLoaded_TrackWithoutId_IsUnclickable()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            var pl = Playlist(tracks: 1);
            pl.Tracks[0].Id = null;
            PanelState s = vm.ContextLoaded(pl);

            Assert.Single(s.QueueRows);
            Assert.Equal(RowActionKind.None, s.QueueRows[0].Action.Kind);
            Assert.Null(s.QueueRows[0].TrackId);
        }

        // === คิวเลื่อนหน้าต่าง (เพลงเดินเลยแถวสุดท้ายบนจอ) ===

        private static List<PlaylistTrackInfo> Window(params string[] ids)
        {
            var list = new List<PlaylistTrackInfo>();
            foreach (string id in ids)
                list.Add(new PlaylistTrackInfo { Id = id, Title = "Track " + id, Artist = "A", DurationMs = 200000 });
            return list;
        }

        // หัวใจของฟีเจอร์: แถวเปลี่ยนเป็นคิวช่วงใหม่ แต่ header ต้องไม่ขยับ - ยังเล่น playlist เดิมอยู่
        [Fact]
        public void QueueWindowLoaded_ReplacesRowsButKeepsHeader()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.ContextLoaded(Playlist(tracks: 2));
            int rev = vm.Current.QueueRevision;

            PanelState s = vm.QueueWindowLoaded(Window("t22", "t23"), "spotify:playlist:p1");

            Assert.Equal("My Mix", s.HeaderName);
            Assert.Equal("PLAYING FROM PLAYLIST", s.HeaderSubLabel);
            Assert.True(s.HeaderCoverVisible);
            Assert.Equal(2, s.QueueRows.Count);
            Assert.Equal("t22", s.QueueRows[0].TrackId);
            Assert.Equal("1", s.QueueRows[0].Index); // เพลงที่เล่นอยู่ขึ้นแถวแรกเสมอ
            Assert.NotEqual(rev, s.QueueRevision);
            Assert.True(s.NeedsReflow);
        }

        // แถวชุดใหม่ต้องกดเล่นได้เหมือนเดิม โดยยังผูกกับ context เดิม (next/prev เดินต่อได้)
        [Fact]
        public void QueueWindowLoaded_RowsStayClickableInSameContext()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            PanelState s = vm.QueueWindowLoaded(Window("t22"), "spotify:playlist:p1");

            Assert.Equal(RowActionKind.PlayTrackInContext, s.QueueRows[0].Action.Kind);
            Assert.Equal("spotify:playlist:p1", s.QueueRows[0].Action.ContextUri);
        }

        [Fact]
        public void QueueWindowLoaded_ArtistContext_RowsUnclickable()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            PanelState s = vm.QueueWindowLoaded(Window("t22"), "spotify:artist:a1");

            Assert.Equal(RowActionKind.None, s.QueueRows[0].Action.Kind);
        }

        // ดึงคิวชุดใหม่ไม่ได้ -> คงแถวเดิมไว้ ดีกว่าล้างจนว่าง (และไม่ต้อง rebuild ฟรี)
        [Fact]
        public void QueueWindowLoaded_NoTracks_KeepsExistingRows()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.ContextLoaded(Playlist(tracks: 2));
            int rev = vm.Current.QueueRevision;

            PanelState s = vm.QueueWindowLoaded(null, "spotify:playlist:p1");

            Assert.Equal(2, s.QueueRows.Count);
            Assert.Equal(rev, s.QueueRevision);
            Assert.False(s.NeedsReflow);
        }

        // === ผลค้นหา ===

        private static SpotifySearchResults SomeResults() => new SpotifySearchResults
        {
            Tracks = { new SearchTrackResult { Id = "t1", Title = "Song", Artist = "A", DurationMs = 61000 } },
            Artists = { new SearchArtistResult { Id = "a1", Name = "Band" } },
            Albums = { new SearchAlbumResult { Id = "al1", Name = "Album", ArtistName = "A", CoverUrl = "u" } },
            Playlists = { new SearchPlaylistResult { Id = "p1", Name = "List", OwnerName = "O" } },
        };

        [Fact]
        public void SearchResultsArrived_BuildsFourSectionsWithActions()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            PanelState s = vm.SearchResultsArrived(SomeResults());

            Assert.Equal(ResultsMode.SearchResults, s.ResultsMode);
            Assert.Equal(new[] { "Tracks", "Artists", "Albums", "Playlists" },
                s.ResultsSections.Select(x => x.Label).ToArray());
            Assert.True(s.NeedsReflow);

            Assert.Equal(RowActionKind.PlayTrack, s.ResultsSections[0].Rows[0].Action.Kind);
            Assert.Equal("1:01", s.ResultsSections[0].Rows[0].Right);
            // แถวศิลปินกดแล้วกางอัลบั้ม ส่วนการสั่งเล่นศิลปินย้ายไปอยู่ที่ปุ่มเล่นท้ายแถว
            Assert.Equal(RowActionKind.ToggleArtistAlbums, s.ResultsSections[1].Rows[0].Action.Kind);
            Assert.Equal("a1", s.ResultsSections[1].Rows[0].Action.ArtistId);
            Assert.Equal("spotify:artist:a1", s.ResultsSections[1].Rows[0].PlayAction.ContextUri);
            Assert.Equal(RowActionKind.LoadAlbum, s.ResultsSections[2].Rows[0].Action.Kind);
            Assert.Equal("spotify:album:al1", s.ResultsSections[2].Rows[0].PlayAction.ContextUri);
            // playlist ไม่มีการกาง - กดแถวแล้วสั่งเล่นทั้งชุดไปเลย
            Assert.Equal(RowActionKind.PlayContext, s.ResultsSections[3].Rows[0].Action.Kind);
            Assert.Equal("spotify:playlist:p1", s.ResultsSections[3].Rows[0].Action.ContextUri);
            Assert.Null(s.ResultsSections[3].Rows[0].Right); // ไม่มีลูกศรกาง/หุบ
        }

        // แถวที่กดแล้วแค่กาง/หุบไม่ต้องทาสีค้าง (ลูกศรบอกสถานะแล้ว) ส่วนปุ่มเล่นต้องมีคีย์ให้ทา
        [Fact]
        public void ToggleRows_AreNotHighlightedButCarryKeyForPlayButton()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            PanelState s = vm.SearchResultsArrived(SomeResults());

            PanelRow artist = s.ResultsSections[1].Rows[0];
            PanelRow playlist = s.ResultsSections[3].Rows[0];

            Assert.True(artist.Action.IsToggle);
            Assert.False(artist.PlayAction.IsToggle);
            Assert.Equal("artist:a1", artist.Key);

            // playlist กดแล้วสั่งเล่นจริง -> ต้องทาสีค้างได้
            Assert.False(playlist.Action.IsToggle);
            Assert.Equal("playlist:p1", playlist.Key);
        }

        [Fact]
        public void SearchResultsArrived_Empty_CollapsesArea()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.SearchResultsArrived(SomeResults());
            PanelState s = vm.SearchResultsArrived(new SpotifySearchResults());

            Assert.Equal(ResultsMode.Empty, s.ResultsMode);
            Assert.Empty(s.ResultsSections);
        }

        // รูปปกของแต่ละหมวดต้องไหลจากผลค้นหาไปถึงแถวครบ - แถวศิลปินขอทรงวงกลม ที่เหลือสี่เหลี่ยมมน
        [Fact]
        public void SearchResultsArrived_CarriesCoverImageUrls()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            var results = new SpotifySearchResults
            {
                Tracks = { new SearchTrackResult { Id = "t1", Title = "Song", Artist = "A", AlbumCoverUrl = "track.jpg" } },
                Artists = { new SearchArtistResult { Id = "a1", Name = "Band", ImageUrl = "artist.jpg" } },
                Albums = { new SearchAlbumResult { Id = "al1", Name = "Album", ArtistName = "A", CoverUrl = "album.jpg" } },
                Playlists = { new SearchPlaylistResult { Id = "p1", Name = "List", OwnerName = "O", CoverUrl = "list.jpg" } },
            };
            PanelState s = vm.SearchResultsArrived(results);

            Assert.Equal("track.jpg", s.ResultsSections[0].Rows[0].ImageUrl);
            Assert.Equal("artist.jpg", s.ResultsSections[1].Rows[0].ImageUrl);
            Assert.Equal("album.jpg", s.ResultsSections[2].Rows[0].ImageUrl);
            Assert.Equal("list.jpg", s.ResultsSections[3].Rows[0].ImageUrl);

            Assert.Equal(RowImageShape.Circle, s.ResultsSections[1].Rows[0].ImageShape);
            Assert.Equal(RowImageShape.Square, s.ResultsSections[0].Rows[0].ImageShape);
            Assert.Equal(RowImageShape.Square, s.ResultsSections[2].Rows[0].ImageShape);
            Assert.Equal(RowImageShape.Square, s.ResultsSections[3].Rows[0].ImageShape);
        }

        // playlist ที่ไม่มีปก (Spotify คืน images ว่าง) ต้องได้ ImageUrl เป็น null
        // เพื่อให้ renderer ข้ามช่องรูปไปเลย ไม่ใช่ทิ้งกล่องเปล่าไว้หน้าแถว
        [Fact]
        public void SearchResultsArrived_NoCover_LeavesImageUrlNull()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            var results = new SpotifySearchResults
            {
                Playlists = { new SearchPlaylistResult { Id = "p1", Name = "List", OwnerName = "O" } },
            };
            PanelState s = vm.SearchResultsArrived(results);

            Assert.Null(s.ResultsSections[0].Rows[0].ImageUrl);
            // ยังต้องกันช่องรูปไว้ ไม่งั้นแถวนี้ชิดซ้ายคนเดียวจนคอลัมน์ไม่ตรงกับแถวอื่น
            Assert.Equal(RowImageShape.Square, s.ResultsSections[0].Rows[0].ImageShape);
        }

        // === กางอัลบั้มใต้แถวศิลปิน ===

        private static List<ArtistAlbumInfo> TwoAlbums() => new List<ArtistAlbumInfo>
        {
            new ArtistAlbumInfo { Id = "al1", Name = "Modal Soul", TrackCount = 13, ReleaseYear = "2005", CoverUrl = "c1" },
            new ArtistAlbumInfo { Id = "al2", Name = "Metaphorical Music", TrackCount = 15 },
        };

        [Fact]
        public void ArtistToggled_ExpandsWithLoadingRowThenAlbums()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.SearchResultsArrived(SomeResults());

            Assert.True(vm.ArtistToggled("a1")); // ยังไม่กาง -> ต้องไป fetch

            // ระหว่างรอ: แถวสถานะใต้ศิลปิน + ลูกศรพลิกเป็น "<"
            List<PanelRow> rows = vm.Current.ResultsSections[1].Rows;
            Assert.Equal("<", rows[0].Right);
            Assert.Equal("Loading albums…", rows[1].Title);
            Assert.True(rows[1].Muted);
            Assert.True(rows[1].Indented);

            PanelState s = vm.ArtistAlbumsArrived("a1", TwoAlbums());
            rows = s.ResultsSections[1].Rows;

            Assert.Equal(3, rows.Count); // ศิลปิน + 2 อัลบั้ม
            Assert.Equal("Modal Soul", rows[1].Title);
            Assert.Equal("2005 · 13 tracks", rows[1].Sub);
            Assert.Equal("15 tracks", rows[2].Sub); // ไม่มีปีก็เหลือแค่จำนวนเพลง
            Assert.Equal(RowActionKind.LoadAlbum, rows[1].Action.Kind);
            Assert.Equal("al1", rows[1].Action.AlbumId);
            Assert.True(rows[1].Indented);
            Assert.True(s.NeedsReflow);
        }

        [Fact]
        public void ArtistToggled_SameArtistAgain_CollapsesWithoutFetch()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.SearchResultsArrived(SomeResults());
            vm.ArtistToggled("a1");
            vm.ArtistAlbumsArrived("a1", TwoAlbums());

            Assert.False(vm.ArtistToggled("a1")); // กดซ้ำ = หุบ ไม่ต้อง fetch

            List<PanelRow> rows = vm.Current.ResultsSections[1].Rows;
            Assert.Single(rows);
            Assert.Equal(">", rows[0].Right);
            Assert.True(vm.Current.NeedsReflow);
        }

        // ผลของศิลปินที่ผู้ใช้หุบ/เปลี่ยนใจไปแล้วระหว่างรอเน็ต ต้องไม่กระโดดมาแทรกทีหลัง
        [Fact]
        public void ArtistAlbumsArrived_ForCollapsedArtist_IsIgnored()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.SearchResultsArrived(SomeResults());
            vm.ArtistToggled("a1");
            vm.ArtistToggled("a1"); // หุบระหว่างรอ

            int revBefore = vm.Current.ResultsRevision;
            PanelState s = vm.ArtistAlbumsArrived("a1", TwoAlbums());

            Assert.Single(s.ResultsSections[1].Rows);
            Assert.Equal(revBefore, s.ResultsRevision);
            Assert.False(s.NeedsReflow);
        }

        [Fact]
        public void ArtistAlbumsArrived_NullAndEmpty_ShowMessageRow()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.SearchResultsArrived(SomeResults());

            vm.ArtistToggled("a1");
            vm.ArtistAlbumsArrived("a1", null);
            Assert.Equal("Album list not available for this account",
                vm.Current.ResultsSections[1].Rows[1].Title);

            vm.ArtistToggled("a1"); // หุบ
            vm.ArtistToggled("a1"); // กางใหม่
            vm.ArtistAlbumsArrived("a1", new List<ArtistAlbumInfo>());
            Assert.Equal("No albums found", vm.Current.ResultsSections[1].Rows[1].Title);
        }

        // ค้นหาใหม่/ล้างคำค้น/เปิด My Lists = พื้นที่ผลลัพธ์เป็นของอย่างอื่นแล้ว ต้องไม่มีอัลบั้มค้าง
        [Fact]
        public void ExpandedAlbums_CollapseWhenResultsAreaChanges()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.SearchResultsArrived(SomeResults());
            vm.ArtistToggled("a1");
            vm.ArtistAlbumsArrived("a1", TwoAlbums());

            PanelState s = vm.SearchResultsArrived(SomeResults());
            Assert.Single(s.ResultsSections[1].Rows);

            vm.ArtistToggled("a1");
            vm.ArtistAlbumsArrived("a1", TwoAlbums());
            vm.SearchCleared();
            Assert.False(vm.ArtistToggled("a1")); // ไม่มีผลค้นหาให้กางแล้ว
            Assert.Empty(vm.Current.ResultsSections);
        }

        [Fact]
        public void SearchResultsArrived_SkipsEmptyCategories()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            var results = new SpotifySearchResults
            {
                Tracks = { new SearchTrackResult { Id = "t1", Title = "Song", Artist = "A", DurationMs = 1000 } },
            };
            PanelState s = vm.SearchResultsArrived(results);

            Assert.Single(s.ResultsSections);
            Assert.Equal("Tracks", s.ResultsSections[0].Label);
        }

        // === My Lists toggle (บั๊ก toggle ค้างเคยเกิดจริง - เขียนทั้งวงจรกันไว้) ===

        [Fact]
        public void MyListsFlow_OpenCollapseRefetch()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);

            // กดครั้งแรก: ยังไม่โชว์ -> ขอ fetch
            Assert.True(vm.MyListsClicked());
            vm.MyPlaylistsArrived(new List<UserPlaylistInfo> { new UserPlaylistInfo { Id = "p", Name = "L", TrackCount = 5 } });
            Assert.Equal(ResultsMode.MyPlaylists, vm.Current.ResultsMode);
            Assert.Equal("My Playlists", vm.Current.ResultsSections[0].Label);
            Assert.Equal("5 tracks", vm.Current.ResultsSections[0].Rows[0].Sub);

            // กดซ้ำ: หุบ ไม่ fetch
            Assert.False(vm.MyListsClicked());
            Assert.Equal(ResultsMode.Empty, vm.Current.ResultsMode);
            Assert.Empty(vm.Current.ResultsSections);
            Assert.True(vm.Current.NeedsReflow);

            // กดอีกครั้ง: ต้องกลับมาขอ fetch ใหม่ ไม่ใช่ค้างสถานะ "โชว์อยู่"
            Assert.True(vm.MyListsClicked());
        }

        // ผลค้นหาเข้ามาแทนที่ My Lists -> toggle ต้องถือว่า "หุบแล้ว" (กดปุ่มถัดไปคือเปิดใหม่ ไม่ใช่หุบผลค้นหา)
        [Fact]
        public void SearchResults_ReplaceMyLists_ToggleResets()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.MyListsClicked();
            vm.MyPlaylistsArrived(new List<UserPlaylistInfo> { new UserPlaylistInfo { Id = "p", Name = "L", TrackCount = 1 } });

            vm.SearchResultsArrived(SomeResults());

            Assert.True(vm.MyListsClicked()); // ต้องขอ fetch ไม่ใช่สั่งหุบ
        }

        [Fact]
        public void MyPlaylistsArrived_CarriesCoverImageUrl()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.MyListsClicked();

            PanelState s = vm.MyPlaylistsArrived(new List<UserPlaylistInfo>
            {
                new UserPlaylistInfo { Id = "p1", Name = "L", TrackCount = 3, ImageUrl = "list.jpg" },
                new UserPlaylistInfo { Id = "p2", Name = "No cover", TrackCount = 1 },
            });

            Assert.Equal("list.jpg", s.ResultsSections[0].Rows[0].ImageUrl);
            Assert.Null(s.ResultsSections[0].Rows[1].ImageUrl);
        }

        // /me/playlists คืน total = 0 มาเป็นประจำ - "0 tracks" ทำให้เข้าใจผิดว่า playlist ว่าง
        [Fact]
        public void MyPlaylistsArrived_ZeroTrackCount_HidesSubLine()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.MyListsClicked();

            PanelState s = vm.MyPlaylistsArrived(new List<UserPlaylistInfo>
            {
                new UserPlaylistInfo { Id = "p1", Name = "Unknown size", TrackCount = 0 },
                new UserPlaylistInfo { Id = "p2", Name = "Known size", TrackCount = 7 },
            });

            Assert.Null(s.ResultsSections[0].Rows[0].Sub);
            Assert.Equal("7 tracks", s.ResultsSections[0].Rows[1].Sub);
        }

        // playlist ตั้งใจไม่มีการกาง - กดแถวแล้วสั่งเล่นทั้งชุดตรงๆ (ทั้ง My Lists และผลค้นหา)
        [Fact]
        public void MyPlaylistRows_PlayWholePlaylistWithNoExpandArrow()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.MyListsClicked();

            PanelState s = vm.MyPlaylistsArrived(
                new List<UserPlaylistInfo> { new UserPlaylistInfo { Id = "p1", Name = "Mix" } });

            PanelRow row = s.ResultsSections[0].Rows[0];
            Assert.Single(s.ResultsSections[0].Rows);
            Assert.Null(row.Right);
            Assert.False(row.Action.IsToggle);
            Assert.Equal(RowActionKind.PlayContext, row.Action.Kind);
            Assert.Equal("spotify:playlist:p1", row.Action.ContextUri);
            Assert.Equal("playlist:p1", row.Key); // กดแล้วทาสีค้างได้
        }

        // === ไฮไลต์แถวที่เพิ่งกด ===

        [Fact]
        public void RowSelected_MarksRowAndSurvivesExpandCollapse()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.SearchResultsArrived(SomeResults());

            string albumKey = vm.Current.ResultsSections[2].Rows[0].Key;
            Assert.Equal("album:al1", albumKey);

            PanelState s = vm.RowSelected(albumKey);
            Assert.Equal(albumKey, s.SelectedRowKey);
            Assert.False(s.NeedsReflow); // แค่เปลี่ยนสี ไม่ได้ขยับโครงสร้าง

            // กางอัลบั้มของศิลปินแล้วสีที่กดไว้ต้องไม่หาย (ยังเป็นผลค้นหาชุดเดิม)
            vm.ArtistToggled("a1");
            vm.ArtistAlbumsArrived("a1", TwoAlbums());
            Assert.Equal(albumKey, vm.Current.SelectedRowKey);
        }

        [Fact]
        public void SelectedRow_ClearsWhenResultsAreaChanges()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.SearchResultsArrived(SomeResults());
            vm.RowSelected("album:al1");

            vm.SearchResultsArrived(SomeResults());
            Assert.Null(vm.Current.SelectedRowKey);

            vm.RowSelected("album:al1");
            vm.SearchCleared();
            Assert.Null(vm.Current.SelectedRowKey);
        }

        [Fact]
        public void MyPlaylistsArrived_NullAndEmpty_ShowMessages()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);

            vm.MyPlaylistsArrived(null);
            Assert.Equal("Failed to load playlists, try again", vm.Current.ResultsSections[0].Message);
            Assert.Equal(ResultsMode.MyPlaylists, vm.Current.ResultsMode); // กดซ้ำแล้วหุบ error ได้

            vm.MyListsClicked(); // หุบ
            vm.MyListsClicked(); // ขอใหม่
            vm.MyPlaylistsArrived(new List<UserPlaylistInfo>());
            Assert.Equal("No playlists in this account", vm.Current.ResultsSections[0].Message);
        }

        // === ลบคำค้น ===

        [Fact]
        public void SearchCleared_CollapsesResultsAndResetsMyListsToggle()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            vm.MyListsClicked();
            vm.MyPlaylistsArrived(new List<UserPlaylistInfo> { new UserPlaylistInfo { Id = "p", Name = "L", TrackCount = 1 } });

            PanelState s = vm.SearchCleared();

            Assert.Equal(ResultsMode.Empty, s.ResultsMode);
            Assert.Empty(s.ResultsSections);
            Assert.True(s.NeedsReflow);
            Assert.True(vm.MyListsClicked()); // toggle กลับสถานะหุบแล้ว
        }

        // ช่องค้นหาว่างอยู่แล้ว (เช่น event onValueChanged ยิงซ้ำ) - อย่าสั่ง rebuild/reflow ฟรี
        [Fact]
        public void SearchCleared_WhenAlreadyEmpty_NoReflowNoRevisionBump()
        {
            var vm = new PanelViewModel();
            vm.ResetForInject(loggedIn: true);
            int rev = vm.Current.ResultsRevision;

            PanelState s = vm.SearchCleared();

            Assert.False(s.NeedsReflow);
            Assert.Equal(rev, s.ResultsRevision);
        }

        // === FormatTime (ใช้ทั้งใน VM และ hot path ของ progress bar) ===

        [Theory]
        [InlineData(0, "0:00")]
        [InlineData(61, "1:01")]
        [InlineData(600, "10:00")]
        [InlineData(3661, "1:01:01")]
        public void FormatTime_Formats(int seconds, string expected)
        {
            Assert.Equal(expected, PanelViewModel.FormatTime(TimeSpan.FromSeconds(seconds)));
        }
    }
}
