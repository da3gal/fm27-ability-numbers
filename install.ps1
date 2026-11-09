<#
FM27 Ability Numbers installer for Windows.

Finds Football Manager 27 in your Steam libraries and installs BepInEx and the plugin.

    irm https://raw.githubusercontent.com/da3gal/fm27-ability-numbers/main/install.ps1 | iex
    .\install.ps1 [-GameDir DIR] [-Check] [-Uninstall]
#>
param([string]$GameDir, [switch]$Check, [switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # the progress bar makes Invoke-WebRequest very slow in PowerShell 5.1
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$GameFolder = 'Football Manager 27'
# Pinned to the BepInEx build the plugin is tested with; the checksum guards the download.
$BepInExUrl = 'https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip'
$BepInExSha256 = '5B63D7D0E92A2448CCD13B738FF95FF1CC1026C6D718555D4DF8E6C824953AC4'
$PluginUrl = 'https://github.com/da3gal/fm27-ability-numbers/releases/latest/download/FM27AbilityNumbers.dll'
$BepInExItems = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'BepInEx', 'dotnet')

function Say($msg) { Write-Host "  $msg" }
# throw, not exit: run through 'irm | iex', exit would close the user's PowerShell window.
function Fail($msg) { throw "Error. $msg" }

function Find-Game {
    $roots = @()
    try { $roots += (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath } catch { }
    $roots += "${env:ProgramFiles(x86)}\Steam", "$env:ProgramFiles\Steam"
    $libraries = @()
    foreach ($root in ($roots | Where-Object { $_ })) {
        $libraries += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            $libraries += [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"') |
                ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' }
        }
    }
    foreach ($lib in ($libraries | Select-Object -Unique)) {
        $game = Join-Path $lib "steamapps\common\$GameFolder"
        if (Test-Path -LiteralPath (Join-Path $game 'fm.exe')) { return $game }
    }
    return $null
}

function Test-GameRunning($game) {
    $exe = Join-Path $game 'fm.exe'
    foreach ($p in @(Get-Process -Name fm -ErrorAction SilentlyContinue)) {
        if (-not $p.Path -or $p.Path -eq $exe) { return $true }   # no path means we may not see it, so assume yes
    }
    return $false
}

function Get-File($url, $out) {
    Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing -Headers @{ 'User-Agent' = 'fm27-ability-numbers-installer' }
}

function Install-BepInEx($game) {
    if (Test-Path -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')) {
        Say 'BepInEx (IL2CPP) is already installed, keeping it.'
        return
    }
    Say 'Downloading BepInEx 6.0.0-be.793...'
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("fm27an-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        $zip = Join-Path $tmp 'bepinex.zip'
        Get-File $BepInExUrl $zip
        if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $BepInExSha256) {
            Fail 'The BepInEx download does not match its checksum. Nothing was installed.'
        }
        $unpacked = Join-Path $tmp 'unpacked'
        Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
        foreach ($item in $BepInExItems) {   # everything except changelog.txt
            Copy-Item -LiteralPath (Join-Path $unpacked $item) -Destination $game -Recurse -Force
        }
    }
    finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    Say 'BepInEx installed.'
}

function Install-Plugin($game) {
    Say 'Downloading the latest FM27 Ability Numbers...'
    $dir = Join-Path $game 'BepInEx\plugins\FM27AbilityNumbers'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $tmp = Join-Path $dir 'FM27AbilityNumbers.dll.download'
    Get-File $PluginUrl $tmp
    $bytes = [IO.File]::ReadAllBytes($tmp)
    if ($bytes.Length -lt 10000 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        Remove-Item -LiteralPath $tmp -Force
        Fail 'The plugin download is not a valid DLL. Nothing was changed.'
    }
    Move-Item -LiteralPath $tmp -Destination (Join-Path $dir 'FM27AbilityNumbers.dll') -Force
    Say "Plugin installed ($([int]($bytes.Length / 1024)) KB)."
}

function Uninstall-Plugin($game) {
    Remove-Item -LiteralPath (Join-Path $game 'BepInEx\plugins\FM27AbilityNumbers') -Recurse -Force -ErrorAction SilentlyContinue
    Say 'Plugin removed.'
    $plugins = Join-Path $game 'BepInEx\plugins'
    $others = @(if (Test-Path -LiteralPath $plugins) { Get-ChildItem -LiteralPath $plugins -Force | ForEach-Object Name })
    if ($others.Count -gt 0) {
        Say "BepInEx kept, it still has other plugins ($($others -join ', '))."
        return
    }
    foreach ($item in $BepInExItems + 'changelog.txt') {
        Remove-Item -LiteralPath (Join-Path $game $item) -Recurse -Force -ErrorAction SilentlyContinue
    }
    Say 'BepInEx removed, the game is back to vanilla.'
}

Write-Host 'FM27 Ability Numbers installer'
$game = if ($GameDir) { $GameDir } else { Find-Game }
if (-not $game -or -not (Test-Path -LiteralPath (Join-Path $game 'fm.exe'))) {
    Fail "Football Manager 27 was not found. Run again with -GameDir 'C:\path\to\Football Manager 27'."
}
$game = (Resolve-Path -LiteralPath $game).Path
Say "Game folder is $game"

if ($Check) {
    Say "BepInEx installed is $(Test-Path -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'))"
    Say "Plugin installed is $(Test-Path -LiteralPath (Join-Path $game 'BepInEx\plugins\FM27AbilityNumbers\FM27AbilityNumbers.dll'))"
    return
}
if (Test-GameRunning $game) { Fail 'Football Manager 27 is running. Quit the game and run the installer again.' }

if ($Uninstall) { Uninstall-Plugin $game; return }
Install-BepInEx $game
Install-Plugin $game
Write-Host "`nDone. Start FM27. The first start after installing BepInEx takes a few extra minutes."
