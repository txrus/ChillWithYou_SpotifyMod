# ChillWithYou Spotify Mod

A BepInEx mod for **Chill with You: Lo-Fi Story** — adds an in-game Spotify player so you can control playback, search, and pick playlists without alt-tabbing out of the game.

![The mod in-game — the Spotify player and playlist list inside Chill with You](assets/screenshot.png)

> ⚠️ **A Spotify Premium account is required** — the Spotify Web API only allows playback control (play/pause/skip) on Premium accounts. Free accounts can log in but can't control playback.

## Features

- Spotify login via OAuth 2.0 (Authorization Code + PKCE) — no client secret needed
- Remembers your session locally (encrypted with Windows DPAPI), so you don't have to log in again on the next launch
- Control playback — play / pause / skip — from the in-game UI
- Search tracks and pick your own playlists
- Built-in rate limiter to avoid hammering the API

## Installation (for players)

1. Install [BepInEx 5.x (x64)](https://github.com/BepInEx/BepInEx/releases) into the game folder, then launch the game once so BepInEx creates its folders.
2. Put **both** files from `bin\Release\netstandard2.1\` (build them yourself using the steps below) into `<game folder>\BepInEx\plugins`:
   - `ChillWithYou_SpotifyMod.dll`
   - `System.Security.Cryptography.ProtectedData.dll` — the mod uses this to encrypt your refresh token with Windows DPAPI. If this file is missing, **login will fail** (the token exchange throws `Could not load file or assembly`).
3. Launch the game and click the Spotify login button in-game — your browser opens Spotify's authorization page; approve it.

## Creating a Spotify App (required before building)

The mod needs your own **Client ID** from the Spotify Developer Dashboard:

1. Go to [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) and log in with your Spotify account.
2. Click **Create app**. The name and description can be anything.
3. In **Redirect URIs**, enter this exactly (don't forget the trailing `/`):
   ```
   http://127.0.0.1:8901/callback/
   ```
4. Under "Which API/SDKs are you planning to use?", tick **Web API**, then Save.
5. Open the app's Settings page and copy the **Client ID** to use with the build script below.

> You only need the Client ID — **no Client Secret** — because the mod uses OAuth with PKCE.

## Easy build (recommended)

You need [.NET SDK 8.0 or newer](https://dotnet.microsoft.com/download) (developed/tested with 10.0.302) and the game (with BepInEx installed). Open PowerShell in the project folder and run:

```powershell
.\build.ps1
```

The script asks for your Client ID and builds the DLL for you (you can also pass the ID as a parameter):

```powershell
.\build.ps1 -ClientId "your32charclientid"

# If the game isn't at the default path, specify it:
.\build.ps1 -ClientId "..." -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story"
```

You get files in `bin\Release\netstandard2.1\`, and if the game folder is found, **both `ChillWithYou_SpotifyMod.dll` and `System.Security.Cryptography.ProtectedData.dll`** are copied into `BepInEx\plugins` automatically — the Client ID is embedded only in the DLL, and the source file is always restored after the build.

> If the script won't run because of the execution policy, run it with `powershell -ExecutionPolicy Bypass -File .\build.ps1`

## Building manually (without the script)

Edit `SpotifyAuth.cs` and replace `ENTER_YOUR_CLIENT_ID` with your Client ID:

```csharp
private const string ClientId = "ENTER_YOUR_CLIENT_ID";
```

The project references DLLs directly from the game folder. The default points to:

```
F:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story
```

If your game is elsewhere, create a `GameDir.props` file (not committed) next to the `.csproj`:

```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story</GameDir>
  </PropertyGroup>
</Project>
```

Then:

```
dotnet build
```

After a successful build, both `ChillWithYou_SpotifyMod.dll` and its dependency `System.Security.Cryptography.ProtectedData.dll` are copied into the game's `BepInEx\plugins` automatically (if the folder exists).

> If the game folder isn't found (no `GameDir.props`), copy the files from `bin\Release\netstandard2.1\` yourself — remember to take **both files**, not just `ChillWithYou_SpotifyMod.dll`, otherwise Spotify login will fail.

## Code overview

| File | Purpose |
|---|---|
| `plugin.cs` | Plugin entry point + MainThreadDispatcher |
| `SpotifyAuth.cs` | OAuth PKCE flow + local callback server |
| `SpotifyTokenStore.cs` | Stores the token on disk, encrypted (DPAPI) |
| `SpotifyWebApi.cs` / `SpotifyApi.cs` / `SpotifyApiClient.cs` | Spotify Web API calls |
| `SpotifySearchApi.cs` | Track search |
| `SpotifyRateLimiter.cs` | Limits API call frequency |
| `SpotifyButtonInjector.cs` | Builds/injects the player UI into the game |
| `PlaylistSelectionUI.cs` | Playlist selection UI |
| `UiSprites.cs` | Builds UI sprites/textures in code (no image assets needed) |
| `TrackInfo.cs` | Now-playing data model + UI update helpers |
| `SpotifyPatches.cs` | Harmony patches |

## Spotify Web API limitations (Development Mode)

The app you created above runs in **Development Mode**, where Spotify has removed several endpoints. As a result the mod can't do some things — **these aren't bugs** and can't be fixed in code:

| Not possible | Reason |
|---|---|
| List an artist's top tracks before playing | `/artists/{id}/top-tracks` was removed (Feb 2026) — clicking an artist starts playback, then shows the real queue from `/me/player/queue` |
| Pick a specific track in the queue while playing from an artist | Spotify rejects `offset` when the context is an artist (only album/playlist are supported) — the queue is display-only there; use the next button.<br>Playing that track standalone is possible but drops the context so next/prev no longer follow the artist, so it's intentionally not done |
| Browse *all* of an artist's albums | `/artists/{id}/albums` was removed (Feb 2026) — but albums found through search can still be opened to view their track list |
| See related artists | `/artists/{id}/related-artists` was removed (Nov 2024) |
| Open Daily Mix / Discover Weekly / "This Is ..." | Spotify-owned playlists, no longer readable through the API (Nov 2024) |
| See an artist's playlists | This endpoint never existed — playlists belong to a *user*, not an artist |
| More than 10 search results per category | The `limit` cap dropped from 50 to 10 (Feb 2026) — the mod already uses 5 |

What **still works normally**: playback control (play/pause/next/prev), search, your own playlists, and now-playing info.

> These limits are tied to Development Mode — an app granted **Extended Quota Mode** isn't affected, but that requires applying and passing Spotify's review, which this mod hasn't done since it's built for learning / personal use.
>
> References: [Nov 2024 announcement](https://developer.spotify.com/blog/2024-11-27-changes-to-the-web-api) · [Feb 2026 migration guide](https://developer.spotify.com/documentation/web-api/tutorials/february-2026-migration-guide)

## Other limitations

- A **Spotify Premium** account is required to control playback
- Windows only (token storage uses DPAPI)
- There are no secrets in the code — only a Client ID (a public PKCE client) that you create yourself using the steps above
- This mod is not affiliated with the game's developers or Spotify

## Acknowledgements

This mod was made to learn, and almost everything about it was learned from other people's work — thank you:

- **fraguledust**, **Ecaphet**, and **ALMIA** — I read through these three modders' code to learn how it's done; a lot of this mod only made sense because I got to read their work first.
- The [**BepInEx**](https://github.com/BepInEx/BepInEx) and [**HarmonyLib**](https://github.com/pardeike/Harmony) teams, who make modding Unity games something an ordinary person can actually start doing. This mod barely does anything on its own beyond building on these two.
- The **Unity game modding community** who write articles, answer threads, and open-source their own code — many of the techniques here (finding GameObjects in a scene, injecting UI into a game not designed for it, patching with Harmony) all come from work others figured out first.
- The developers of **Chill with You: Lo-Fi Story**, who made a game with such a nice atmosphere that I wanted to listen to my own music inside it.

> If you own work this mod builds on and aren't credited here yet, open an issue — happy to add you.

## Changelog

See the per-version history in [CHANGELOG.md](CHANGELOG.md).

Latest version **v1.1.1** — fixes Spotify login failing (UI stuck on the Connect button after approving in the browser) by making the build deploy the `System.Security.Cryptography.ProtectedData.dll` dependency into `plugins`.

## License

[MIT](LICENSE) © pw_txr
