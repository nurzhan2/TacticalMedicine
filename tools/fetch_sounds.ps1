# Downloads the hand-picked best 2-4 sounds per category from SourceAssets/Audio/freesound_manifest.tsv
# into Assets/Audio/SFX/<cat>/<id>_<slug>.mp3 and writes Assets/Audio/SFX/attribution.tsv
# (merged into ATTRIBUTION.md by the same script). ASCII only (PowerShell 5). Idempotent: skips existing files.
# Usage: powershell -ExecutionPolicy Bypass -File tools\fetch_sounds.ps1
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $root 'SourceAssets\Audio\freesound_manifest.tsv'
$outRoot = Join-Path $root 'Assets\Audio\SFX'
$maxBytes = 5MB

# Picks by category (chosen by name/metadata sanity; obviously wrong ones like "spaceship", "Wilhelm",
# "fireworks", "stun gun" are excluded). Order = preference.
$picks = [ordered]@{
    'explosion_near'     = @(344686, 529239, 140727)
    'explosion_near2'    = @(609587, 108641, 523365, 563010)
    'explosion_far'      = @(108640, 209769, 320788)
    'incoming_whistle'   = @(241840, 170991, 325258)
    'artillery_outgoing' = @(161343, 161341, 186945, 565794)
    'debris_dirt'        = @(109360, 325268, 477393, 438524)
    'drone_fpv'          = @(677833, 201923, 733937)
    'drone_fpv2'         = @(383852, 250085)
    'drone_mavic'        = @(250086, 529055, 272595)
    'drone_shahed'       = @(78651, 198611, 192354)
    'gunfire_distant'    = @(170478, 158978, 158977, 160771)
    'gunfire_ak'         = @(163457, 520935, 17130)
    'bullet_whiz'        = @(505238, 730351, 134024)
    'bullet_impact_dirt' = @(150839, 150838, 150837)
    'flare'              = @(348767, 317834, 423109)
    'radio'              = @(524205, 47646, 337747, 561847)
    'tinnitus'           = @(365956, 188201, 718562)
    'heartbeat'          = @(232481, 216221, 793474)
    'breath_heavy'       = @(554307, 319103, 317478, 432997)
    'pain_groan'         = @(257706, 464486, 413186, 344681)
    'pain_scream'        = @(179325, 381942, 179317, 695386)
    'gasp_choke'         = @(514794, 252236, 403675, 582433)
    'cough'              = @(537150, 743357, 743354, 466874)
    'velcro'             = @(536231, 536229, 536228, 28549)
    'scissors_fabric'    = @(490114, 649923, 627627)
    'package_tear'       = @(188203, 477369, 391473)
    'fabric_rustle'      = @(19291, 235243, 475440)
    'body_drag'          = @(44406, 268909, 44407)
    'footsteps_mud'      = @(384871, 384875, 166511, 166510)
    'wind_night'         = @(773075, 540035)
    'crickets_night'     = @(715682, 574670, 484034)
    'ratchet_click'      = @(151778, 117087, 384406)
    'marker_write'       = @(383872, 119926)
    'blood_squirt'       = @(737235, 414744, 414747, 537919)
    'rain_trench'        = @(96729, 691544)
}

$rows = Import-Csv -Path $manifest -Delimiter "`t" -Encoding UTF8
$attrib = New-Object System.Collections.Generic.List[string]
$attrib.Add("cat`tid`tlicense`tauthor`tname`turl`tfile")
$ok = 0; $fail = 0

function Get-Slug([string]$s) {
    $t = ($s -replace '\.(wav|mp3|aif|aiff|ogg|wa)$', '').ToLowerInvariant()
    $t = $t -replace '[^a-z0-9]+', '_'
    $t = $t.Trim('_')
    if ($t.Length -gt 32) { $t = $t.Substring(0, 32).Trim('_') }
    if ($t -eq '') { $t = 'sound' }
    return $t
}

foreach ($cat in $picks.Keys) {
    $dir = Join-Path $outRoot $cat
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($id in $picks[$cat]) {
        $r = $rows | Where-Object { $_.id -eq "$id" } | Select-Object -First 1
        if ($null -eq $r) { Write-Host "missing in manifest: $id"; continue }
        $lic = if ($r.lic -eq 'CC0') { 'CC0 1.0' } else { 'CC BY 4.0' }
        $file = "{0}_{1}.mp3" -f $id, (Get-Slug $r.name)
        $dest = Join-Path $dir $file
        if (-not (Test-Path $dest)) {
            & curl.exe -s -L --max-time 240 -o $dest $r.url
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dest) -or (Get-Item $dest).Length -lt 1000) {
                Write-Host "FAIL $cat $id" -ForegroundColor Red
                if (Test-Path $dest) { Remove-Item $dest -Force }
                $fail++; continue
            }
            if ((Get-Item $dest).Length -gt $maxBytes) {
                Write-Host "SKIP >5MB $cat $id"; Remove-Item $dest -Force; continue
            }
            Write-Host "ok $cat/$file"
        }
        $ok++
        $attrib.Add(("{0}`t{1}`t{2}`t{3}`t{4}`thttps://freesound.org/s/{1}/`t{5}" -f $cat, $id, $lic, $r.user, $r.name, "$cat/$file"))
    }
}

$tsv = Join-Path $outRoot 'attribution.tsv'
[System.IO.File]::WriteAllLines($tsv, $attrib, (New-Object System.Text.UTF8Encoding($false)))

# Rewrite the generated section of ATTRIBUTION.md
$attrMd = Join-Path $root 'ATTRIBUTION.md'
$text = [System.IO.File]::ReadAllText($attrMd)
$begin = '<!-- SFX-BEGIN -->'; $end = '<!-- SFX-END -->'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine($begin)
[void]$sb.AppendLine('## Sound effects (Freesound.org, via tools/fetch_sounds.ps1)')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| File (Assets/Audio/SFX/) | Title | Author | Source | License |')
[void]$sb.AppendLine('|---|---|---|---|---|')
foreach ($line in ($attrib | Select-Object -Skip 1)) {
    $f = $line -split "`t"
    [void]$sb.AppendLine(("| {0} | {1} | {2} | {3} | {4} |" -f $f[6], ($f[4] -replace '\|', '/'), $f[3], $f[5], $f[2]))
}
[void]$sb.AppendLine($end)
$i0 = $text.IndexOf($begin); $i1 = $text.IndexOf($end)
if ($i0 -ge 0 -and $i1 -gt $i0) { $text = $text.Substring(0, $i0) + $sb.ToString().TrimEnd() + $text.Substring($i1 + $end.Length) }
else { $text = $text.TrimEnd() + "`r`n`r`n" + $sb.ToString() }
[System.IO.File]::WriteAllText($attrMd, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "[fetch_sounds] ok=$ok fail=$fail -> $tsv"
