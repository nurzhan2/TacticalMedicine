# Field Medic VR - black-screen bisect on the Quest (w0b). ASCII only (PowerShell 5).
# Usage:  powershell -ExecutionPolicy Bypass -File tools\quest_bisect.ps1 [-Dir C:\Users\user\Desktop\FMVR_TEST] [-WaitSec 45] [-Dev]
# Installs 1_HELLO -> 2_SAFE -> 3_FULL (or 3_FULL_DEV with -Dev) with "adb install -r -g", launches each, waits,
# takes a headset screenshot, pulls fmvr_log.txt (+ previous launch log) and a filtered logcat into
# logs\bisect_<time>\, prints PASS/FAIL per APK and the first failing one with its key log lines.
# All three APKs share the package id kz.qorsa.fieldmedic: every install replaces the previous one.
param(
    [string]$Dir = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'FMVR_TEST'),
    [string]$Package = 'kz.qorsa.fieldmedic',
    [int]$WaitSec = 45,
    [switch]$Dev,
    [switch]$KeepGoing
)

$root = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$out = Join-Path $root "logs\bisect_$stamp"
New-Item -ItemType Directory -Force $out | Out-Null

function Find-Adb {
    $cands = @('C:\Program Files\Meta Quest Developer Hub\resources\bin\adb.exe',
               'C:\Android\platform-tools\adb.exe',
               (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'),
               'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
    foreach ($c in $cands) { if ($c -and (Test-Path $c)) { return $c } }
    $cmd = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}
function Invoke-Adb([string[]]$AdbArgs) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $o = & $script:adb -s $script:serial @AdbArgs 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $old }
    return $o
}

$script:adb = Find-Adb
if (-not $script:adb) { Write-Host 'NO DEVICE (adb not found)'; exit 2 }
$list = & $script:adb devices 2>&1 | ForEach-Object { "$_" }
$devs = @($list | Where-Object { $_ -match '^\S+\s+device$' -and $_ -notmatch '^emulator-' } | ForEach-Object { ($_ -split '\s+')[0] })
if ($devs.Count -eq 0) {
    if (@($list | Where-Object { $_ -match 'unauthorized' }).Count -gt 0) { Write-Host 'NO DEVICE: put the headset on and allow USB debugging' }
    else { Write-Host 'NO DEVICE: connect the Quest with a USB-C data cable, developer mode on' }
    exit 2
}
$script:serial = $devs[0]
$model = (Invoke-Adb @('shell', 'getprop', 'ro.product.model')) -join ''
$os = (Invoke-Adb @('shell', 'getprop', 'ro.build.version.incremental')) -join ''
Write-Host "[bisect] device $script:serial ($model, build $os), output $out"

$full = if ($Dev) { '3_FULL_DEV.apk' } else { '3_FULL.apk' }
$steps = @(
    @{ Name = '1_HELLO'; Apk = '1_HELLO.apk'; NeedReady = $false },
    @{ Name = '2_SAFE';  Apk = '2_SAFE.apk';  NeedReady = $true },
    @{ Name = '3_FULL';  Apk = $full;         NeedReady = $true }
)
$dataDir = "/sdcard/Android/data/$Package/files"
$results = @()
$firstFail = $null

foreach ($s in $steps) {
    $apk = Join-Path $Dir $s.Apk
    $tag = $s.Name
    Write-Host ""
    Write-Host "===== $tag ($apk)"
    if (-not (Test-Path $apk)) { Write-Host "  SKIP: no APK"; $results += "$tag SKIP (missing)"; continue }
    $inst = Invoke-Adb @('install', '-r', '-g', $apk)
    if (-not ($inst -match 'Success')) {
        Invoke-Adb @('uninstall', $Package) | Out-Null
        $inst = Invoke-Adb @('install', '-g', $apk)
    }
    if (-not ($inst -match 'Success')) {
        $msg = "$tag FAIL install: $($inst -join ' ')"
        Write-Host "  $msg"; $results += $msg
        if (-not $firstFail) { $firstFail = @{ Name = $tag; Lines = $inst } }
        if (-not $KeepGoing) { break } else { continue }
    }
    # fresh launch state: no crash flag from the previous APK, full mode
    Invoke-Adb @('shell', 'am', 'force-stop', $Package) | Out-Null
    Invoke-Adb @('shell', 'rm', '-f', "$dataDir/fmvr_state.txt", "$dataDir/fmvr_force_safe.txt") | Out-Null
    Invoke-Adb @('logcat', '-c') | Out-Null
    Invoke-Adb @('shell', 'monkey', '-p', $Package, '-c', 'com.oculus.intent.category.VR', '1') | Out-Null
    Start-Sleep -Seconds 3
    if (-not ((Invoke-Adb @('shell', 'pidof', $Package)) -match '\d')) { Invoke-Adb @('shell', 'monkey', '-p', $Package, '1') | Out-Null }
    Write-Host "  launched, waiting $WaitSec s (wear the headset to look)..."
    Start-Sleep -Seconds $WaitSec

    # headset screenshot (Meta capture service, falls back to screencap)
    Invoke-Adb @('shell', 'am', 'startservice', '-n', 'com.oculus.metacam/.capture.CaptureService', '-a', 'TAKE_SCREENSHOT') | Out-Null
    Start-Sleep -Seconds 3
    $shot = (Invoke-Adb @('shell', 'ls', '-t', '/sdcard/Oculus/Screenshots/') | Select-Object -First 1)
    if ($shot -and $shot -match '\.(jpg|png)$') { Invoke-Adb @('pull', "/sdcard/Oculus/Screenshots/$shot", (Join-Path $out "$tag.jpg")) | Out-Null }
    else {
        Invoke-Adb @('shell', 'screencap', '-p', '/sdcard/fmvr_shot.png') | Out-Null
        Invoke-Adb @('pull', '/sdcard/fmvr_shot.png', (Join-Path $out "$tag.png")) | Out-Null
    }

    $procId = (Invoke-Adb @('shell', 'pidof', $Package)) -join ''
    $alive = $procId -match '\d'
    $flog = Join-Path $out "${tag}_fmvr_log.txt"
    Invoke-Adb @('pull', "$dataDir/fmvr_log.txt", $flog) | Out-Null
    Invoke-Adb @('pull', "$dataDir/fmvr_log_prev.txt", (Join-Path $out "${tag}_fmvr_log_prev.txt")) | Out-Null
    $all = Invoke-Adb @('logcat', '-d', '-v', 'time')
    $pattern = 'Unity|AndroidRuntime|OpenXR|libc|DEBUG|CRASH|FMVR|OVRPlugin|VrApi|vulkan|ActivityManager.*' + [regex]::Escape($Package) + '|' + [regex]::Escape($Package)
    $lc = @($all | Where-Object { $_ -match $pattern })
    $lc | Set-Content -Encoding UTF8 (Join-Path $out "${tag}_logcat.txt")
    $fl = @(); if (Test-Path $flog) { $fl = Get-Content $flog -Encoding UTF8 }

    $crash = @($lc | Where-Object { $_ -match 'FATAL EXCEPTION|SIGSEGV|SIGABRT|Fatal signal' }).Count -gt 0
    $frame = @($fl + $lc | Where-Object { $_ -match 'first frame rendered' }).Count -gt 0
    $xrFail = @($fl + $lc | Where-Object { $_ -match 'XR FAILED' }).Count -gt 0
    $ready = @($fl + $lc | Where-Object { $_ -match 'gameplay ready' }).Count -gt 0
    $stall = @($fl | Where-Object { $_ -match 'STALL' })
    $ok = $alive -and -not $crash -and $frame -and -not $xrFail -and ((-not $s.NeedReady) -or $ready)
    $line = "$tag " + $(if ($ok) { 'PASS' } else { 'FAIL' }) + " (alive=$alive crash=$crash firstFrame=$frame xrFailed=$xrFail gameplayReady=$ready stalls=$($stall.Count))"
    Write-Host "  $line"
    $results += $line
    if (-not $ok -and -not $firstFail) {
        $key = @($fl + $lc | Where-Object { $_ -match 'ERROR|EXCEPTION|XR status|XR FAILED|STALL|FATAL|SIG|first frame|launch mode|boot:|additive|SAFE' } | Select-Object -First 40)
        $firstFail = @{ Name = $tag; Lines = $key }
        if (-not $KeepGoing) { break }
    }
}

Write-Host ""
Write-Host "===== RESULT ($model)"
$results | ForEach-Object { Write-Host "  $_" }
$results | Set-Content -Encoding UTF8 (Join-Path $out 'result.txt')
if ($firstFail) {
    Write-Host ""
    Write-Host "FIRST FAILING: $($firstFail.Name)" -ForegroundColor Red
    $firstFail.Lines | ForEach-Object { Write-Host "  $_" }
    Write-Host "logs: $out"
    exit 1
}
Write-Host "ALL PASS - logs: $out" -ForegroundColor Green
exit 0
