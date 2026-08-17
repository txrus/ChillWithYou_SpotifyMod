<#
.SYNOPSIS
    One-step build for ChillWithYou_SpotifyMod.

    The Client ID is normally set at runtime in BepInEx\config\com.pw_txr.spotifyplayer.cfg,
    so a plain build needs no ID at all. Pass -ClientId only if you want it baked into the DLL
    (the source file is patched for the build, then restored untouched).

.EXAMPLE
    .\build.ps1                                   # Client ID comes from the .cfg at runtime
    .\build.ps1 -ClientId "your32charclientid"    # bake the ID into the DLL instead
    .\build.ps1 -GameDir "C:\Steam\steamapps\common\Chill with You Lo-Fi Story"
#>
[CmdletBinding()]
param(
    [string]$ClientId,
    [string]$GameDir
)

$ErrorActionPreference = "Stop"
$configFile = Join-Path $PSScriptRoot "SpotifyConfig.cs"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK not found. Install it from https://dotnet.microsoft.com/download"
}

$ClientId = if ($ClientId) { $ClientId.Trim().Trim('"') } else { "" }
if ($ClientId -eq "ENTER_YOUR_CLIENT_ID") { $ClientId = "" }

if ($ClientId -and $ClientId -notmatch '^[0-9a-fA-F]{32}$') {
    Write-Warning "That doesn't look like a typical 32-char hex Client ID. Continuing anyway..."
}

# Keep the exact original bytes so we can restore the file untouched afterwards
$originalBytes = [System.IO.File]::ReadAllBytes($configFile)
$originalText  = [System.Text.Encoding]::UTF8.GetString($originalBytes)
$patched       = $false

if ($ClientId) {
    $pattern = 'private const string BakedInClientId = "[^"]*";'
    if ($originalText -notmatch $pattern) {
        Write-Error "Could not find the BakedInClientId line in SpotifyConfig.cs"
    }
    $patchedText = $originalText -replace $pattern, "private const string BakedInClientId = `"$ClientId`";"
    $patched = $true
}

try {
    if ($patched) {
        [System.IO.File]::WriteAllText($configFile, $patchedText, (New-Object System.Text.UTF8Encoding($true)))
    }

    $buildArgs = @("build", "-c", "Release")
    if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed - see errors above." }
}
finally {
    if ($patched) {
        [System.IO.File]::WriteAllBytes($configFile, $originalBytes)
    }
}

$dll = Join-Path $PSScriptRoot "bin\Release\netstandard2.1\ChillWithYou_SpotifyMod.dll"
Write-Host ""
if ($ClientId) {
    Write-Host "Done! DLL built with your Client ID baked in:" -ForegroundColor Green
} else {
    Write-Host "Done! DLL built:" -ForegroundColor Green
}
Write-Host "  $dll"
Write-Host "If the game folder was found, it was also copied to BepInEx\plugins automatically."
Write-Host "Otherwise, copy the DLL to <GameFolder>\BepInEx\plugins yourself."
if (-not $ClientId) {
    Write-Host ""
    Write-Host "Next: run the game once, then set ClientId in" -ForegroundColor Yellow
    Write-Host "  <GameFolder>\BepInEx\config\com.pw_txr.spotifyplayer.cfg" -ForegroundColor Yellow
}
