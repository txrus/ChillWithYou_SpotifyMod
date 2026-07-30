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
a "LIKED SONGS" header with non-clickable rows.

## Consequences

- Don't re-attempt a direct `/me/tracks` integration (save button, saved-tracks listing,
  saved-tracks paging) without evidence Spotify's development-mode policy has changed for this
  endpoint family specifically.
- Any future "save this track" feature needs a different mechanism entirely — there is no
  known working path through the Web API for a development-mode app.
- The `RunOptimistic` helper and `SpotifyGatewayPolicy` module introduced alongside the
  original library work stayed — they're used by shuffle/repeat and the whole gateway
  respectively, independent of the library feature that motivated writing them.
