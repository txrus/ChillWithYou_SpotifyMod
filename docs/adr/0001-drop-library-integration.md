# 0001 — Drop the `/me/tracks` (save song / Liked Songs) integration

## Status

Accepted.

## Context

A save-song heart button and a direct Liked Songs listing were built against Spotify's
library endpoints (`GET/PUT/DELETE /me/tracks`, `GET /me/tracks/contains`), requesting the
`user-library-read` and `user-library-modify` scopes to do it. Tested in game: every call in
the `/me/tracks` family returned `403`, even with both scopes granted and confirmed present
on the access token. This is a known Spotify restriction on apps running in development mode
(this mod's permanent state — see the "Development-mode 403" glossary entry in `CONTEXT.md`),
not a bug in the request or a token problem.

## Decision

Removed the feature entirely — `SpotifyLibrary.cs`, the save button, the Liked Songs row in
My Lists, and the two library scopes — rather than leave code on disk calling an endpoint
that can never succeed for this app.

Liked Songs remains viewable when played from the Spotify app itself: the panel recognizes
the `spotify:user:*:collection` context and falls back to the queue window (the same
mechanism already used for artist and album radio, which also can't be read directly), showing
a "LIKED SONGS" header. (Row clickability for this specific context changed later — see the
addendum below; this paragraph describes the state at the time of the original decision.)

## Consequences

- Don't re-attempt a direct `/me/tracks` integration (save button, saved-tracks listing,
  saved-tracks paging) without evidence Spotify's development-mode policy has changed for this
  endpoint family specifically.
- Any future "save this track" feature needs a different mechanism entirely — there is no
  known working path through the Web API for a development-mode app.
- The `RunOptimistic` helper and `SpotifyGatewayPolicy` module introduced alongside the
  original library work stayed — they're used by shuffle/repeat and the whole gateway
  respectively, independent of the library feature that motivated writing them.

## Addendum: playing Liked Songs is not the same as reading it

*Added after a follow-up request to "add Liked Songs to My Lists."*

The restriction above is specifically on the `/me/tracks` family — reading or writing the
saved-tracks list. It says nothing about **playing** the collection: `PUT /me/player/play`
with `context_uri: "spotify:user:{id}:collection"` is a playback-endpoint call, not a library
call, and isn't part of the blocked family. A "Liked Songs" row was added to My Lists that
does exactly this — plays the collection from the top, the same way a playlist row does —
without ever touching `/me/tracks`. It can't show a track count or thumbnail up front
(nothing was listed to get one from), and once playing, track *browsing* still goes through
the existing collection-context queue fallback this ADR already describes, not a new listing
call. This doesn't reopen the decision above; it's a different endpoint entirely.

Rows in that queue were initially made unclickable by code choice - not because Spotify was
ever asked and refused, but on the precautionary assumption that it would refuse an `offset`
for a `collection` context the same way it refuses one for `artist`. That assumption was
never actually tested, because there was previously no way to reach this context at all.
Once the play button existed, a player confirmed in game that rows genuinely couldn't be
selected - expected, since the row's click action was `None` and no offset request was ever
sent. Rows were then made clickable (`SpotifyContext.RowsViewOnly` no longer includes
`IsCollection`) to find out whether Spotify's Play endpoint accepts an `offset` for this
context.

**Confirmed in game: it does.** Clicking a track in the Liked Songs queue jumps playback to
that track, same as a playlist row. `IsCollection` and `IsArtist` are no longer the same case
for row clickability - only `IsArtist` remains a confirmed rejection.
