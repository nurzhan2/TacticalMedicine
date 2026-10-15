# Quest on-device launch check for Field Medic VR. ASCII only (PowerShell 5).
# Usage:  powershell -ExecutionPolicy Bypass -File tools\quest_check.ps1 [-Apk Builds\FieldMedicVR.apk] [-WaitSec 40]
# - finds adb, ignores emulators; no headset -> prints NO DEVICE, exit 0
# - installs the APK (-r -g), launches it, waits, saves filtered logcat + screenshot to logs\quest_<time>.*
# - prints PASS if the Unity player started and there is no FATAL EXCEPTION / SIGSEGV, else FAIL (exit 1)
param(
    [string]$Apk = '',
    [string]$Package = 'kz.qorsa.fieldmedic',
    [int]$WaitSec = 40,
    [switch]$NoInstall
)

$root = Split-Path -Parent $PSScriptRoot
if ($Apk -eq '') { $Apk = Join-Path $root 'Builds\FieldMedicVR.apk' }
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force $logDir | Out-Null

function Find-Adb {
    $cands = @('C:\Android\platform-tools\adb.exe',
               'C:\Program Files\Meta Quest Developer Hub\resources\bin\adb.exe',
               (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'),
               'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
    foreach ($c in $cands) { if ($c -and (Test-Path $c)) { return $c } }
    $cmd = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

# run adb without PowerShell turning stderr into errors
function Invoke-Adb([string[]]$AdbArgs) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $out = & $script:adb @AdbArgs 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $old }
    return $out
}

$script:adb = Find-Adb
if (-not $script:adb) { Write-Host 'NO DEVICE (adb not found)'; exit 0 }
Write-Host "[quest_check] adb: $script:adb"

$devices = @(Invoke-Adb @('devices') | Where-Object { $_ -match '^\S+\s+device$' -and $_ -notmatch '^emulator-' } |
             ForEach-Object { ($_ -split '\s+')[0] })
$unauth = @(Invoke-Adb @('devices') | Where-Object { $_ -match '\sunauthorized$' })
if ($devices.Count -eq 0) {
    if ($unauth.Count -gt 0) { Write-Host 'NO DEVICE (headset connected but USB debugging is not allowed - put the headset on and accept the prompt)' }
    else { Write-Host 'NO DEVICE' }
    exit 0
}
$serial = $devices[0]
$model = (Invoke-Adb @('-s', $serial, 'shell', 'getprop', 'ro.product.model')) -join ''
Write-Host "[quest_check] device: $serial ($model)"

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$txt = Join-Path $logDir "quest_$stamp.txt"
$png = Join-Path $logDir "quest_$stamp.png"

if (-not $NoInstall) {
    if (-not (Test-Path $Apk)) { Write-Host "FAIL (no APK at $Apk)"; exit 1 }
    Write-Host "[quest_check] installing $Apk ..."
    $inst = Invoke-Adb @('-s', $serial, 'install', '-r', '-g', $Apk)
    $inst | ForEach-Object { Write-Host "  $_" }
    if (-not ($inst -match 'Success')) {
        if ($inst -match 'INSTALL_FAILED_UPDATE_INCOMPATIBLE') {
            Write-Host '[quest_check] signature differs from the installed copy -> uninstall + install'
            Invoke-Adb @('-s', $serial, 'uninstall', $Package) | Out-Null
            $inst = Invoke-Adb @('-s', $serial, 'install', '-g', $Apk)
        }
        if (-not ($inst -match 'Success')) {
            ("INSTALL FAILED`r`n" + ($inst -join "`r`n")) | Set-Content -Encoding ASCII $txt
            Write-Host "FAIL (install failed, see $txt)"; exit 1
        }
    }
}

Invoke-Adb @('-s', $serial, 'shell', 'am', 'force-stop', $Package) | Out-Null
Invoke-Adb @('-s', $serial, 'logcat', '-c') | Out-Null
Write-Host "[quest_check] launching $Package ..."
Invoke-Adb @('-s', $serial, 'shell', 'monkey', '-p', $Package, '-c', 'com.oculus.intent.category.VR', '1') | Out-Null
$launchCheck = Invoke-Adb @('-s', $serial, 'shell', 'pidof', $Package)
if (-not ($launchCheck -match '\d')) {
    # some OS versions reject the VR category filter in monkey -> plain launcher intent
    Invoke-Adb @('-s', $serial, 'shell', 'monkey', '-p', $Package, '1') | Out-Null
}
Write-Host "[quest_check] waiting $WaitSec s (put the headset on to see it) ..."
Start-Sleep -Seconds $WaitSec

$procId = (Invoke-Adb @('-s', $serial, 'shell', 'pidof', $Package)) -join ''
$all = Invoke-Adb @('-s', $serial, 'logcat', '-d', '-v', 'time')
$pattern = 'Unity|AndroidRuntime|OpenXR|libc|DEBUG|CRASH|FMVR|OVRPlugin|VrApi|ActivityManager.*' + [regex]::Escape($Package) + '|' + [regex]::Escape($Package)
$filtered = @($all | Where-Object { $_ -match $pattern })
$header = @("quest_check $stamp device=$serial model=$model package=$Package pid=$procId",
            "apk=$Apk", '----')
($header + $filtered) | Set-Content -Encoding UTF8 $txt

# w0b: the game's own log (persistentDataPath/fmvr_log.txt: launch mode, XR status, first frame, scene timings)
$flog = Join-Path $logDir "quest_${stamp}_fmvr_log.txt"
Invoke-Adb @('-s', $serial, 'pull', "/sdcard/Android/data/$Package/files/fmvr_log.txt", $flog) | Out-Null
if (Test-Path $flog) { Write-Host "[quest_check] game log: $flog"; Get-Content $flog -Encoding UTF8 | Where-Object { $_ -match 'launch mode|XR status|XR FAILED|first frame|gameplay ready|STALL|ERROR|EXCEPTION' } | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" } }

Invoke-Adb @('-s', $serial, 'shell', 'screencap', '-p', '/sdcard/fmvr_check.png') | Out-Null
Invoke-Adb @('-s', $serial, 'pull', '/sdcard/fmvr_check.png', $png) | Out-Null
Invoke-Adb @('-s', $serial, 'shell', 'rm', '/sdcard/fmvr_check.png') | Out-Null

$started = @($filtered | Where-Object { $_ -match '\bUnity\b' -and ($_ -match 'Built from|Version|FMVR|XR|OpenXR|Loading player data|Initialize engine') }).Count -gt 0
$crash = @($filtered | Where-Object { $_ -match 'FATAL EXCEPTION|SIGSEGV|SIGABRT|Fatal signal|CRASH' }).Count -gt 0
$alive = $procId -match '\d'
Write-Host "[quest_check] log: $txt"
if (Test-Path $png) { Write-Host "[quest_check] screenshot: $png" }
Write-Host "[quest_check] unityStarted=$started processAlive=$alive crash=$crash"
if ($started -and $alive -and -not $crash) { Write-Host 'PASS'; exit 0 }
Write-Host 'FAIL'
exit 1
