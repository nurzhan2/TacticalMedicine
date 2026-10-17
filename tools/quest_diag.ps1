# Waits for an authorized Quest, then collects diagnostics of the installed game. ASCII only.
$adb = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe' | Select -First 1 -Expand FullName
$d = 'C:\TacticalMedicine\logs\device'; New-Item -ItemType Directory -Force $d | Out-Null
$st = "$d\diag_status.txt"; "waiting $(Get-Date -Format HH:mm:ss)" | Out-File $st
$end = (Get-Date).AddMinutes(15)
while ((Get-Date) -lt $end) {
  $l = (& $adb devices) -join "`n"
  if ($l -match "`tdevice") { break }
  Start-Sleep 3
}
if (-not ((& $adb devices) -join "`n" -match "`tdevice")) { "timeout (not authorized)" | Out-File $st -Append; exit }
"authorized $(Get-Date -Format HH:mm:ss)" | Out-File $st -Append
& $adb shell getprop ro.build.display.id > "$d\os.txt"
& $adb shell pm list packages -3 > "$d\packages.txt"
$pkg = (Get-Content "$d\packages.txt" | Select-String 'qorsa|fieldmedic|tacticalmedicine' | Select -First 1).Line -replace 'package:',''
"pkg=$pkg" | Out-File $st -Append
& $adb logcat -d -v time > "$d\logcat_before.txt"
if ($pkg) {
  & $adb shell dumpsys package $pkg | Select-String 'versionName|lastUpdateTime|firstInstallTime' > "$d\pkg_info.txt"
  & $adb pull "/sdcard/Android/data/$pkg/files/fmvr_log.txt" "$d\fmvr_log_before.txt" 2>&1 | Out-Null
  & $adb pull "/sdcard/Android/data/$pkg/files/fmvr_log_prev.txt" "$d\fmvr_log_prev_before.txt" 2>&1 | Out-Null
  & $adb logcat -c
  & $adb shell monkey -p $pkg -c android.intent.category.LAUNCHER 1 | Out-Null
  "launched $(Get-Date -Format HH:mm:ss)" | Out-File $st -Append
  Start-Sleep 45
  & $adb logcat -d -v time > "$d\logcat_run.txt"
  & $adb exec-out screencap -p > "$d\screen_run.png"
  & $adb pull "/sdcard/Android/data/$pkg/files/fmvr_log.txt" "$d\fmvr_log_run.txt" 2>&1 | Out-Null
}
"done $(Get-Date -Format HH:mm:ss)" | Out-File $st -Append
