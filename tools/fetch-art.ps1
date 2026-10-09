<#
.SYNOPSIS
    Downloads the free CC0 art for Epochs Unbound into Assets/_Project/ThirdParty.

.DESCRIPTION
    - Poly Haven: five ground textures (grass, dirt, sand, rock, snow; 2K colour, normal, roughness) and a 4K sky.
    - Kenney: building, prop and animated character packs (FBX models and their textures only).
    - Any zip you put in the ArtDrop folder at the repo root (Quaternius packs, or a Kenney pack the script could
      not fetch) is unpacked the same way: Quaternius into ThirdParty/Quaternius/<name>, kenney_* into ThirdParty/Kenney/<name>.

    Safe to rerun: anything already downloaded is skipped unless you pass -Force.
    Writes ThirdParty/CREDITS.md listing every asset and where it came from (all CC0).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\fetch-art.ps1
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # the progress bar makes Invoke-WebRequest very slow in Windows PowerShell 5
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$Repo = Split-Path -Parent $PSScriptRoot
$Art = Join-Path $Repo 'Assets/_Project/ThirdParty'
$Drop = Join-Path $Repo 'ArtDrop'
$Headers = @{ 'User-Agent' = 'EpochsUnbound-fetch-art/1.0' }   # Poly Haven asks API users to send a User-Agent
$Failed = New-Object System.Collections.Generic.List[string]

# Ground layers: candidate Poly Haven asset ids, first that exists wins; then the most downloaded texture whose id
# contains the keyword. The game's terrain shader expects these five layer names.
$GroundLayers = [ordered]@{
    Grass = @{ Ids = @('leafy_grass', 'grass_path_2', 'aerial_grass_rock'); Keyword = 'grass' }
    Dirt  = @{ Ids = @('brown_mud_leaves_01', 'forrest_ground_01', 'brown_mud_02'); Keyword = 'mud' }
    Sand  = @{ Ids = @('coast_sand_01', 'coast_sand_02', 'sand_01'); Keyword = 'sand' }
    Rock  = @{ Ids = @('rock_face', 'aerial_rocks_02', 'rocky_terrain_02'); Keyword = 'rock' }
    Snow  = @{ Ids = @('snow_02', 'snow_01', 'snow_03'); Keyword = 'snow' }
}
$SkyIds = @('kloofendal_48d_partly_cloudy_puresky', 'kloofendal_43d_clear_puresky', 'qwantani_puresky')

# Kenney packs (kenney.nl/assets/<slug>): low-poly buildings, survival and farm props, animated people.
$KenneyPacks = @('fantasy-town-kit', 'survival-kit', 'nature-kit', 'animated-characters-1')

function Save-Url([string]$Url, [string]$Path) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    Write-Host "    $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Path -Headers $Headers -UseBasicParsing
}

function Get-PolyFiles([string]$Id) {
    try { Invoke-RestMethod -Uri "https://api.polyhaven.com/files/$Id" -Headers $Headers } catch { $null }
}

function Resolve-PolyAsset([string[]]$Ids, [string]$Type, [string]$Keyword) {
    foreach ($id in $Ids) {
        $files = Get-PolyFiles $id
        if ($files) { return @{ Id = $id; Files = $files } }
    }
    $all = Invoke-RestMethod -Uri "https://api.polyhaven.com/assets?t=$Type" -Headers $Headers
    $best = $all.PSObject.Properties | Where-Object { $_.Name -like "*$Keyword*" } |
        Sort-Object { [int]$_.Value.download_count } -Descending | Select-Object -First 1
    if (-not $best) { throw "no Poly Haven $Type matching '$Keyword'" }
    return @{ Id = $best.Name; Files = (Get-PolyFiles $best.Name) }
}

# Picks the requested resolution, or the nearest one below it.
function Get-MapUrl($Map, [string]$Format, [string[]]$Resolutions) {
    if (-not $Map) { return $null }
    foreach ($r in $Resolutions) {
        $entry = $Map.PSObject.Properties[$r]
        if ($entry -and $entry.Value.PSObject.Properties[$Format]) { return $entry.Value.$Format.url }
    }
    return $null
}

function Read-Sources([string]$Path) {
    $map = [ordered]@{}
    if (Test-Path $Path) {
        foreach ($line in Get-Content $Path) { if ($line -match '^(\w+)=(.+)$') { $map[$Matches[1]] = $Matches[2] } }
    }
    return $map
}

# Unpacks a zip keeping only what Unity needs: FBX models, their textures, and licence/readme text. Skips previews
# and the duplicate OBJ/GLB/glTF/Blend exports so the repo does not carry every model four times.
function Import-ArtZip([string]$Zip, [string]$Dest) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('eu-art-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $Zip -DestinationPath $tmp -Force
    try {
        $top = @(Get-ChildItem -LiteralPath $tmp)
        $base = if ($top.Count -eq 1 -and $top[0].PSIsContainer) { $top[0].FullName } else { $tmp }
        $kept = 0
        foreach ($f in Get-ChildItem -LiteralPath $base -Recurse -File) {
            $rel = $f.FullName.Substring($base.Length).TrimStart('\', '/')
            $dirs = @(Split-Path -Parent $rel) -split '[\\/]' | Where-Object { $_ }
            if ($dirs | Where-Object { $_ -match '(?i)^(previews?|isometric|sprites?|2d)$' -or $_ -match '(?i)\b(obj|glb|gltf|blend|blender|unity|godot|unreal|dae|stl)\b' }) { continue }
            $ext = $f.Extension.ToLowerInvariant()
            $keep = $ext -in @('.fbx', '.png', '.jpg', '.jpeg', '.tga') -or ($ext -in @('.txt', '.md') -and $f.Name -match '(?i)licen|readme')
            if (-not $keep) { continue }
            $out = Join-Path $Dest $rel
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
            Copy-Item -LiteralPath $f.FullName -Destination $out -Force
            $kept++
        }
        Write-Host "    kept $kept files in $Dest"
    }
    finally { Remove-Item -LiteralPath $tmp -Recurse -Force }
}

# --- Poly Haven ground textures -------------------------------------------------------------------------------
Write-Host 'Poly Haven ground textures'
$groundDir = Join-Path $Art 'PolyHaven/Ground'
$sourcesPath = Join-Path $Art 'PolyHaven/sources.txt'
$sources = Read-Sources $sourcesPath
foreach ($layer in $GroundLayers.Keys) {
    $albedo = Join-Path $groundDir "${layer}_albedo.jpg"
    if ((Test-Path $albedo) -and -not $Force) { Write-Host "  $layer`: have it"; continue }
    try {
        $c = $GroundLayers[$layer]
        $asset = Resolve-PolyAsset $c.Ids 'textures' $c.Keyword
        Write-Host "  $layer`: $($asset.Id)"
        $res = @('2k', '1k')
        Save-Url (Get-MapUrl $asset.Files.Diffuse 'jpg' $res) $albedo
        $normal = Get-MapUrl $asset.Files.nor_gl 'jpg' $res
        if ($normal) { Save-Url $normal (Join-Path $groundDir "${layer}_normal.jpg") }
        $rough = Get-MapUrl $asset.Files.Rough 'jpg' $res
        if ($rough) { Save-Url $rough (Join-Path $groundDir "${layer}_rough.jpg") }
        $sources[$layer] = $asset.Id
    }
    catch { $Failed.Add("Poly Haven $layer texture: $_"); Write-Warning "$layer failed: $_" }
}

# --- Poly Haven sky -------------------------------------------------------------------------------------------
Write-Host 'Poly Haven sky'
$skyPath = Join-Path $Art 'PolyHaven/Sky/sky.hdr'
if ((Test-Path $skyPath) -and -not $Force) { Write-Host '  have it' }
else {
    try {
        $asset = Resolve-PolyAsset $SkyIds 'hdris' 'puresky'
        Write-Host "  $($asset.Id)"
        Save-Url (Get-MapUrl $asset.Files.hdri 'hdr' @('4k', '2k')) $skyPath
        $sources['Sky'] = $asset.Id
    }
    catch { $Failed.Add("Poly Haven sky: $_"); Write-Warning "sky failed: $_" }
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $sourcesPath) | Out-Null
($sources.Keys | ForEach-Object { "$_=$($sources[$_])" }) | Set-Content -Path $sourcesPath -Encoding UTF8

# --- Kenney packs ---------------------------------------------------------------------------------------------
Write-Host 'Kenney packs'
foreach ($slug in $KenneyPacks) {
    $dest = Join-Path $Art "Kenney/$slug"
    if ((Test-Path $dest) -and -not $Force) { Write-Host "  $slug`: have it"; continue }
    try {
        Write-Host "  $slug"
        $page = Invoke-WebRequest -Uri "https://kenney.nl/assets/$slug" -Headers $Headers -UseBasicParsing
        $m = [regex]::Match($page.Content, '["'']([^"'']+?\.zip)["'']')
        if (-not $m.Success) { throw 'no download link on the page' }
        $url = $m.Groups[1].Value
        if ($url.StartsWith('/')) { $url = "https://kenney.nl$url" }
        $zip = Join-Path ([IO.Path]::GetTempPath()) "kenney_$slug.zip"
        Save-Url $url $zip
        if (Test-Path $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
        Import-ArtZip $zip $dest
        Remove-Item -LiteralPath $zip -Force
    }
    catch {
        $Failed.Add("Kenney $slug`: $_ (download it from https://kenney.nl/assets/$slug and put the zip in ArtDrop)")
        Write-Warning "$slug failed: $_"
    }
}

# --- Zips dropped in ArtDrop ----------------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $Drop | Out-Null
$zips = @(Get-ChildItem -LiteralPath $Drop -Filter *.zip)
if ($zips.Count -gt 0) { Write-Host 'ArtDrop zips' }
foreach ($z in $zips) {
    $name = ($z.BaseName -replace '[^\w\-]+', '_').Trim('_')
    $dest = if ($name -match '^(?i)kenney_(.+)$') { Join-Path $Art "Kenney/$($Matches[1])" } else { Join-Path $Art "Quaternius/$name" }
    if ((Test-Path $dest) -and -not $Force) { Write-Host "  $($z.Name): have it"; continue }
    Write-Host "  $($z.Name)"
    try {
        if (Test-Path $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
        Import-ArtZip $z.FullName $dest
    }
    catch { $Failed.Add("ArtDrop $($z.Name): $_"); Write-Warning "$($z.Name) failed: $_" }
}

# --- Credits --------------------------------------------------------------------------------------------------
$credits = @('# Third-party art', '', 'Everything in this folder is CC0 (public domain); credit is given as thanks.', '')
$credits += '## Poly Haven (https://polyhaven.com)'
foreach ($k in $sources.Keys) { $credits += "- $k`: https://polyhaven.com/a/$($sources[$k])" }
foreach ($vendor in @(@{ Dir = 'Kenney'; Title = 'Kenney (https://kenney.nl)'; Url = 'https://kenney.nl/assets/' },
                      @{ Dir = 'Quaternius'; Title = 'Quaternius (https://quaternius.com)'; Url = '' })) {
    $dir = Join-Path $Art $vendor.Dir
    if (-not (Test-Path $dir)) { continue }
    $credits += '', "## $($vendor.Title)"
    foreach ($p in Get-ChildItem -LiteralPath $dir -Directory) {
        $credits += $(if ($vendor.Url) { "- $($p.Name): $($vendor.Url)$($p.Name)" } else { "- $($p.Name)" })
    }
}
$credits | Set-Content -Path (Join-Path $Art 'CREDITS.md') -Encoding UTF8

# --- Summary --------------------------------------------------------------------------------------------------
$bytes = (Get-ChildItem -LiteralPath $Art -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host ("ThirdParty is now {0:N0} MB." -f ($bytes / 1MB))
$big = Get-ChildItem -LiteralPath $Art -Recurse -File | Where-Object { $_.Length -gt 90MB }
foreach ($f in $big) { Write-Warning "$($f.FullName) is over 90 MB; GitHub rejects files over 100 MB." }
if ($Failed.Count -gt 0) {
    Write-Host ''
    Write-Warning 'Some downloads failed:'
    foreach ($f in $Failed) { Write-Host "  - $f" }
    exit 1
}
Write-Host 'Done. Open Unity, let it import, then run Epochs Unbound > Rebuild Main Scene.'
