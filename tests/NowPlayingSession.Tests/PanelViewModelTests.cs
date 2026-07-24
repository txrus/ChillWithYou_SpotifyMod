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
            Assert.Equal("spotify:artist:a1", s.ResultsSections[1].Rows[0].Action.ContextUri);
            Assert.Equal(RowActionKind.LoadAlbum, s.ResultsSections[2].Rows[0].Action.Kind);
            Assert.Equal("spotify:playlist:p1", s.ResultsSections[3].Rows[0].Action.ContextUri);
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
