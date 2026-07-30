# Changelog

Format follows [Keep a Changelog](https://keepachangelog.com/) and uses [Semantic Versioning](https://semver.org/)

## [Unreleased]

## [1.4.0] - 2026-07-30

Rolls up four merged PRs that had been sitting unreleased (queue-window-slide fix, device
transfer, progress-bar hover time, and this round's shuffle/repeat/volume) - bumped to a
minor version for the same reason 1.2.0 and 1.3.0 were: real new player-facing controls,
not just bug fixes.

### Added
- **A "Liked Songs" row in My Lists** that plays your saved tracks, right above your
  playlists. This isn't the same feature that got pulled below for hitting a 403 - that one
  tried to *list* the tracks via `/me/tracks`, which is blocked. This one just sends
  `spotify:user:{id}:collection` as a `context_uri` to the same `/me/player/play` endpoint
  every other row already uses, so it never touches the blocked endpoint at all. Once
  playing, the existing collection-context fallback (also in this release, used for artist
  and album radio too) takes over and shows the queue labeled "LIKED SONGS".
  - The context URI needs your Spotify user ID, which the mod fetches once via `GET /me` and
    caches for the session (reusing the same call `LogCurrentUser` already made at startup,
    if a session was resumed there).
  - If the ID can't be fetched, the row is hidden entirely rather than shown and failing
    silently when tapped.
  - Lives in its own small section, so a `/me/playlists` failure can't take it down with it.
- **Shuffle and repeat buttons** in the controls row, arranged the same way the Spotify app
  does (shuffle - prev - play - next - repeat), so the play button stays centered since one
  button was added on each side.
  - Shuffle is a plain on/off toggle. Repeat cycles one step per press: off → repeat all
    (`↻`) → repeat one (`↻1`) → off.
  - Dim = off, green = on, and both flip instantly without waiting for Spotify to respond
    (reverting if the command fails, e.g. a Free account or no active device) - the same
    pattern the play/pause button already uses.
  - Real state comes from `shuffle_state` / `repeat_state` on the `/me/player` response the
    mod already polls - no extra endpoint. Toggle shuffle from your phone and the in-game
    panel catches up within 6 seconds.
  - **After a successful shuffle toggle, the on-screen queue is refetched** to match. For
    playlists specifically this needed more than a refetch: `/playlists/{id}` always returns
    the playlist's fixed, saved order and has no idea shuffle exists, so no amount of
    refreshing that endpoint could ever show the shuffled order. When shuffle is on, the
    queue for a playlist is now sourced from `/me/player/queue` instead (the same live,
    actually-playing order already used for artist/album radio), while the header still keeps
    the real playlist name and cover.
  - Hovering either button now shows a small "Shuffle" / "Repeat" tooltip above it, since the
    icons alone are too small to read at a glance.
- **Volume slider**, in the corner of the controls row (`PUT /me/player/volume`, the last 20%
  of the row's width) rather than its own line. Drag-and-release sends the command once, like
  seek; the percentage follows your finger while dragging, and polled values never overwrite
  the slider mid-drag. Hidden entirely when the device can't take volume commands through the
  Web API (`supports_volume`) - a slider that does nothing is more confusing than no slider.
  Uses the existing scope - no reconnect needed. The bar and its "100%" label both use fixed
  pixel widths rather than one flexible one competing for space with the other, after the
  flexible version rendered with the two overlapping in game.
- **Devices button** next to My Lists: shows every device Spotify currently sees
  (`GET /me/player/devices`); tapping a row transfers playback there (`PUT /me/player`). This
  is the fix for the classic "pressed a button in game and nothing happened" case, caused by
  Spotify having no active device at all - previously you had to open the Spotify app and hit
  play once before the in-game panel could control anything.
  - The device currently playing is shown in green and can't be tapped (transferring to
    itself is meaningless).
  - A device marked `is_restricted` can't be tapped either, with the reason shown on its
    sub-line rather than letting the tap fail silently.
  - The current play/pause state is sent along with the transfer (`play`) - pausing, then
    switching devices, won't force playback to start.
  - On a successful transfer the new device id is remembered immediately, so the very next
    play/pause/next/seek lands on the new device without waiting for the next poll.
  - Never cached, unlike My Lists - devices come and go constantly, so every open of the list
    is a fresh fetch.
  - Uses the existing `user-read-playback-state` / `user-modify-playback-state` scopes - no
    reconnect needed.
- **Hover the progress bar to see the seek time** floating above it before you click - you
  used to have to press down first to find out (the time on the left only updated once you
  were already dragging). The label follows the cursor, never overflows past either end of the
  bar, and while dragging it shows the same value that will actually be sent
  (`slider.value`), not the raw cursor position, so it never claims a different time than
  where the seek will land. (uGUI has no "mouse moved" event, so pointer enter/exit gate
  whether the label shows, and `Input.mousePosition` is only read on frames the cursor is
  actually over the bar.)

### Fixed
- **Fixed tracks disappearing from the queue once playback passes the last row fetched.** The
  on-screen queue holds at most 21 tracks; once a playlist reached track 22, the playing song
  no longer appeared in the list at all - nothing was highlighted, and nothing refreshed until
  the playlist changed. Now, the poll cycle that notices the playing track has fallen off the
  displayed rows (while still in the same context) fetches the next window from
  `/me/player/queue` and replaces just the track rows - the header (playlist name/cover)
  stays put, since this endpoint always starts counting from the currently playing track, so
  it lands on row 1 and the following rows are clickable as usual
  (`RefreshCoordinator.PlanQueueSlide`). Fires at most once per track to avoid asking again on
  every poll, and never overwrites an album's track list that the player opened from search
  results.
- **Fixed shuffle not actually changing the on-screen queue.** Toggling shuffle cleared the
  refresh coordinator's own memory of what was loaded, but not `SpotifyWebApi`'s separate
  playlist cache, which remembers a fetch result keyed only by playlist id - with no idea
  shuffle exists. The very next fetch after toggling shuffle silently returned that stale,
  pre-toggle cache entry instead of hitting the network at all. Toggling shuffle now clears
  both caches, the same two calls the header's ↻ button already made.

### Changed
- Internal cleanup from an architecture review (no visible change to players):
  - The optimistic apply-then-revert shape used by the shuffle/repeat buttons (change the
    display immediately, send the command, revert only on failure) is now one shared
    `RunOptimistic` helper instead of duplicated per button, with a single documented table of
    which commands revert on failure and which let the next poll correct them instead
    (volume and seek fall in that second group).
  - The network layer's decisions (retrying a transient 401/403, reporting a 429, picking the
    right image size) moved into `SpotifyGatewayPolicy`, plain logic with no dependency on
    `HttpResponseMessage` - now covered by 15 unit tests that previously had no way to run at
    all.
  - Repeat-mode wire mapping (`"track"`/`"context"`/`"off"`) moved out of the DTO file and into
    `SpotifyApi`, per that file's own rule that DTOs shouldn't know about wire formats.

### Removed
- **The save-song heart button and the direct Liked Songs listing.** Both used the
  `/me/tracks` endpoint family, which returns `403` in development mode even with the correct
  scopes granted - there was no way to make it work, so it's been pulled out rather than left
  half-broken. The `user-library-read` / `user-library-modify` scopes requested for it are
  gone too; no one needs to reconnect over this.
  - **Liked Songs is still viewable from in-game** when you start it from the Spotify app: the
    panel recognizes the `spotify:user:*:collection` context and displays it through the
    regular queue mechanism (the same fallback already used for artist and album radio),
    labeled "LIKED SONGS" in the header - it just can't be browsed or launched from inside
    the mod anymore.
- **Search within the loaded playlist/queue**, added earlier in this same unreleased cycle
  and removed again before shipping: it only ever searched the up-to-21 tracks already
  fetched, never the whole playlist, so a track that wasn't found looked like it wasn't in
  the playlist at all when it might simply be outside the loaded window - actively misleading
  rather than merely limited.
- **Queue pagination**, also added and removed within this cycle: didn't work as intended
  once tested in game. The queue goes back to showing every loaded track (up to 21) in a
  single scrollable list, same as before pagination was tried.

## [1.3.0] - 2026-07-25

### Added
- The in-game progress bar can now be dragged (or clicked) to seek - sends a single
  `PUT /me/player/seek` on release, not on every frame of the drag, and moves the local clock
  immediately instead of waiting for Spotify to respond, so the bar doesn't snap back
  momentarily (a failed command still gets corrected by the next poll within 6 seconds).
  While dragging, the time on the left follows the drag position, and the hot path stops
  overwriting the slider value. The bar's hit area grew to 16px (the visible bar is still
  6px) so it's easier to land a tap on. Dragging only works while a track is actually playing.

### Fixed
- Fixed the in-game panel not following songs played from elsewhere (phone / the desktop
  Spotify app): the mod previously only learned about track changes through three paths -
  pressing a button in-game, the local clock counting down to the end of a track, and
  alt-tabbing back into the game - which missed the case of a player who stayed in-game the
  whole time and skipped a track from their phone (no event fired at all, so the panel sat on
  the stale track). It now polls every 6 seconds, but **only while the panel is actually
  open** - closing the menu stops the polling immediately, and any other kind of refresh
  (button press, track ending, alt-tab) pushes the next poll out on its own rather than firing
  on top of it (`RefreshCoordinator.ShouldPollNowPlaying`, plus a guard against overlapping
  requests when one poll runs long).
- Reduced log spam from polling: the `RefreshNowPlaying` log line now only writes when the
  observed track actually changes from the previous check (it used to write on every fetch,
  which is now every 6 seconds).

## [1.2.0] - 2026-07-25

Rolls up everything that had been numbered 1.1.2 - bumped to a minor version because this
round adds real UI (cover art, expandable artist albums, inline play buttons), not just bug
fixes.

### Added
- Row thumbnails in search results and My Lists: track/album/playlist rows now show real
  cover art, and artist rows get a circular photo matching Spotify's own visual language
  (`RowThumbnails` loads asynchronously with a session-wide cache keyed by URL, coalesces
  duplicate requests for the same URL into a single fetch, and remembers failed URLs so it
  doesn't retry them). The image slot and its placeholder appear immediately when a row is
  built, with the real image filling in later without any visible stall, and it picks the
  smallest image at or above 64px rather than the 640px one Spotify lists first (rows only
  display images at 34px - the larger file costs roughly 1.6MB of texture memory per row).
- Tapping an artist row in search results now expands a list of that artist's albums beneath
  it, showing year and track count for each (`GET /artists/{id}/albums`, paged with `offset`
  until 50 are collected, since this endpoint's `limit` ceiling dropped to 10 per page; albums
  duplicated across markets are deduplicated by name; cached per artist for the session).
  Tapping an album from there shows its track list the same way the Albums section does, and
  tapping the artist row again collapses it - the `>` / `<` arrow at the end of the row shows
  which state it's in. Only one artist can be expanded at a time, since expanding several at
  once made the list too long to scan.
  Note: tapping an artist row no longer starts artist radio directly (tapping now expands the
  album list instead).
- Rows tapped in the search/My Lists results area now highlight green immediately on tap,
  rather than waiting for Spotify to respond (loading an album or switching tracks can take a
  couple of seconds, and previously nothing happened visibly until it did).
- A small circular play button at the end of artist / album rows in search results - starts
  playing the whole thing without expanding into track selection first (tapping the row itself
  still expands/shows tracks; tapping the button plays immediately). Playlist rows keep their
  original behavior of playing the whole playlist on tap with no expansion, since Spotify
  strips the track list from most playlist responses anyway.
- Hid the "0 tracks" line under playlist names in My Lists - `/me/playlists` reports
  `tracks.total = 0` routinely, and showing that number as-is only implied the playlist was
  empty.

### Fixed
- Fixed the API being hammered non-stop near the end of every track: the "track ended"
  trigger was re-armed every time track data synced, so if Spotify still reported the same
  track sitting at the end (hadn't switched over yet), the very next frame fired the same
  request again, looping until the track actually changed. It's now only re-armed on an actual
  track change, or when playback moves more than 2 seconds away from the end again, and the
  end-of-track case now uses the same bounded retry loop as right after pressing play
  (up to 4 attempts).
- Fixed a burst of API calls whenever several rows were tapped in quick succession: the
  retry-loop that runs after issuing a play command (4 attempts per command) used to stack one
  loop on top of another, so five quick taps meant twenty `GET /me/player` calls in about two
  seconds. A new command now immediately cancels any older command's loop
  (`RefreshCoordinator.BeginPlayCycle`).
- Fixed row thumbnails rendering as ovals: the row's `HorizontalLayoutGroup` had
  `childForceExpandHeight` enabled (Unity's default is `true`), stretching the image's height
  to fill the row even though its width was locked. Row height is now driven by each child's
  own preferred size, and the image slot locks both axes.
- Fixed rows with no image (some artists have none on Spotify) sitting flush left on their
  own, throwing off the name column's alignment with other rows: every row in a list that has
  images now always reserves the slot and shows a placeholder instead.
- Increased list row height from 36px to 42px so cover art and two lines of text have room to
  breathe.
- Fixed track titles in the queue appearing to "vanish" (leaving only the artist name) right
  when the currently-playing highlight turned green: Unity's `Text` hides an entire line when
  its rect is shorter than the font's line height (`verticalOverflow` defaults to `Truncate`).
  Once the restyle switched to the game's IBM Plex font, which sits taller than Arial, the
  12pt track title in a 30px-tall row fell under that threshold and disappeared. `CreateText`
  now sets `verticalOverflow = Overflow` so text always draws.
- Fixed long track titles wrapping onto a second line and spilling over neighboring rows and
  the search/My Lists area beneath them: queue rows and search/playlist rows now render the
  title and artist as a single line (`horizontalOverflow = Overflow`) clipped with
  `RectMask2D`, so a long name is cut off at the column edge instead of wrapping, and row
  height grew from 30/32px to 36px.
- Fixed the queue showing every track twice with repeat enabled: when a playlist is shorter
  than the queue window (~20 tracks), `/me/player/queue` wraps back around to the start of the
  context, so the same tracks reappear (a 7-track playlist showed 14+ rows).
  `GetQueueTracksAsync` now deduplicates by track id using a `HashSet` (local files with no id
  still pass through as-is, since they can't be deduplicated).
- Fixed the UI not updating on its own the first time you switched to Spotify and back to the
  game (a manual ↻ press was required): the alt-tab resync handler
  (`Application.focusChanged`) was subscribed inside `ApplyNowPlaying`, after a null check that
  the very first connect - with nothing playing yet - never reached, so the subscription never
  happened. It's now subscribed right at inject time instead.
- Fixed the game's own row list drawing over the playlist header and search bar immediately
  after a successful Connect: `OnLoginSuccess` revealed those rows with `SetActive(true)`
  without rebuilding the outer scroll content, so the section grew taller but the game's rows
  didn't shift down to make room (same root cause as the next item).
- Fixed the game's own track list (Original & Special) overlapping Spotify search results:
  `BuildSearchResults` rebuilt only the results list itself, not the scroll content outside the
  mod's section, so the section never grew to match the results. `ForceRebuildLayoutImmediate`
  now runs on `_cachedScrollRect.content` too, so the game's rows flow down below the results
  as expected.

### Changed
- Consolidated Spotify's request envelope (HttpClient, bearer header, 429 → rate limiter,
  error logging, 401/403 retry), previously duplicated across `SpotifyApi`, `SpotifyWebApi`,
  and `SpotifySearchApi`, into a single `SpotifyGateway`.
- Extracted the now-playing state machine (the progress-bar interpolation clock and
  play/pause state) out of `SpotifyButtonInjector` into `NowPlayingSession`, pure logic with
  no Unity dependency, backed by 13 unit tests that run on the plain .NET SDK without the game.
- Removed dead playlist-selection code that was no longer called anywhere.
- Folded HTTP calls that had drifted outside the envelope back into the API layer: the
  injector used to assemble the body for `me/player/play` itself in three separate places, and
  a fourth `HttpClient` hit `/albums/{id}/tracks` directly, bypassing the gateway's
  retry/429/logging entirely. Every play command now goes through `SpotifyApi.Play*`, and
  album loading goes through `SpotifyWebApi.GetAlbumTracksAsync`.
- Split the game-styled widget kit (colors, locating the game's font, circular/pill buttons,
  the progress slider, the search field) out into `SpotifyUiKit` - a module that only knows
  "what it looks like" and nothing about Spotify.
- Split all of the panel's screen logic (which rows show/hide, when a layout reflow is
  needed, what the results area displays, the My Lists toggle) out into `PanelViewModel`, a
  pure state machine: events go in, a full `PanelState` snapshot comes out, and the Unity side
  is left with a single idempotent `Apply(state)` call. This makes the "forgot to SetActive" /
  "forgot to rebuild" family of bugs (the root cause of 3 of the 6 bugs fixed this version)
  structurally impossible (#15, #16).
- Split the refresh orchestration rules (when/how to load context, the commit-only-on-success
  rule that prevents a queue from getting stuck empty, the retry timing after issuing a play
  command, the focus-resync cooldown) out into `RefreshCoordinator` - the injector is left
  just issuing API calls per the plan it's handed (#17, #18).
- The test bench now covers 73 cases total (`dotnet test`, no game/Unity/Spotify account
  needed), spanning `NowPlayingSession`, `PanelViewModel`, and `RefreshCoordinator`, including
  a regression test for every real bug in this version that used to require opening the game,
  logging in, and playing a track to even reproduce.

## [1.1.1]

### Fixed
- Fixed the UI freezing after pressing **Connect Spotify** and approving in the browser: the
  OAuth callback (`OnLoginSuccess` / `OnLoginFailed`) was invoked from a thread-pool
  continuation and touched Unity UI directly, throwing "can only be called from the main
  thread" - silently swallowed, so the browser showed "Login successful" while the in-game
  panel sat stuck on the Connect button. It now marshals back to the main thread via
  `Plugin.RunOnMainThread(...)`.

## [1.1.0]

- Initial release with an in-game Spotify player: playback control, search, and playlist
  selection.
