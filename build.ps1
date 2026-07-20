<#
.SYNOPSIS
    One-step build for ChillWithYou_SpotifyMod.
    Injects your Spotify Client ID, builds the DLL, then restores the source file.

.EXAMPLE
    .\build.ps1                                   # prompts for Client ID
    .\build.ps1 -ClientId "your32charclientid"
    .\build.ps1 -ClientId "..." -GameDir "C:\Steam\steamapps\common\Chill with You Lo-Fi Story"
#>
[CmdletBinding()]
param(
    [string]$ClientId,
    [string]$GameDir
)

$ErrorActionPreference = "Stop"
$authFile = Join-Path $PSScriptRoot "SpotifyAuth.cs"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK not found. Install it from https://dotnet.microsoft.com/download"
}

if (-not $ClientId) {
    Write-Host "Get your Client ID from https://developer.spotify.com/dashboard"
    Write-Host "(create an app with Redirect URI: http://127.0.0.1:8901/callback/)"
    $ClientId = Read-Host "Enter your Spotify Client ID"
}
$ClientId = $ClientId.Trim()

if (-not $ClientId -or $ClientId -eq "ENTER_YOUR_CLIENT_ID") {
    Write-Error "A Spotify Client ID is required."
}
if ($ClientId -notmatch '^[0-9a-fA-F]{32}$') {
    Write-Warning "That doesn't look like a typical 32-char hex Client ID. Continuing anyway..."
}

# Keep the exact original bytes so we can restore the file untouched afterwards
$originalBytes = [System.IO.File]::ReadAllBytes($authFile)
$originalText  = [System.Text.Encoding]::UTF8.GetString($originalBytes)

$pattern = 'private const string ClientId = "[^"]*";'
if ($originalText -notmatch $pattern) {
    Write-Error "Could not find the ClientId line in SpotifyAuth.cs"
}
$patchedText = $originalText -replace $pattern, "private const string ClientId = `"$ClientId`";"

try {
    [System.IO.File]::WriteAllText($authFile, $patchedText, (New-Object System.Text.UTF8Encoding($true)))

    $buildArgs = @("build", "-c", "Release")
    if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed - see errors above." }
}
finally {
    [System.IO.File]::WriteAllBytes($authFile, $originalBytes)
}

$dll = Join-Path $PSScriptRoot "bin\Release\netstandard2.1\ChillWithYou_SpotifyMod.dll"
Write-Host ""
Write-Host "Done! DLL built with your Client ID:" -ForegroundColor Green
Write-Host "  $dll"
Write-Host "If the game folder was found, it was also copied to BepInEx\plugins automatically."
Write-Host "Otherwise, copy the DLL to <GameFolder>\BepInEx\plugins yourself."
