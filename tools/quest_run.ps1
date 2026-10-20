# Install an APK on the Quest, launch it, collect logs + screenshot. ASCII only.
param([string]$Apk = 'C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_DEMO_XRFIX.apk', [string]$Tag = 'run', [int]$Wait = 45)
$adb = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe' | Select -First 1 -Expand FullName
$d = 'C:\TacticalMedicine\logs\device'; New-Item -ItemType Directory -Force $d | Out-Null
$st = "$d\${Tag}_status.txt"; "start $(Get-Date -Format HH:mm:ss) $Apk" | Out-File $st
$pkg = 'kz.qorsa.fieldmedic'
$out = & $adb install -r -g $Apk 2>&1; "install: $out" | Out-File $st -Append
& $adb logcat -c
& $adb shell monkey -p $pkg -c android.intent.category.LAUNCHER 1 | Out-Null
"launched $(Get-Date -Format HH:mm:ss)" | Out-File $st -Append
Start-Sleep $Wait
& $adb logcat -d -v time > "$d\${Tag}_logcat.txt"
& $adb shell screencap -p /sdcard/fmvr.png; & $adb pull /sdcard/fmvr.png "$d\${Tag}_screen.png" 2>&1 | Out-Null  # PS redirection corrupts binary PNGs
& $adb pull "/sdcard/Android/data/$pkg/files/fmvr_log.txt" "$d\${Tag}_fmvr_log.txt" 2>&1 | Out-Null
& $adb shell dumpsys activity activities | Select-String 'mResumedActivity|topResumedActivity' > "$d\${Tag}_activity.txt"
"done $(Get-Date -Format HH:mm:ss)" | Out-File $st -Append
