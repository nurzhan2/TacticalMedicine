# Final W10 build with the w10e wound fix: build, verify XR, copy to Desktop, install on the Quest. ASCII only.
$ErrorActionPreference = 'Continue'
Set-Location C:\TacticalMedicine
$st = 'logs\final_build_status.txt'
"start $(Get-Date -Format HH:mm)" | Out-File $st
. .\tools\unity.ps1
$code = Invoke-UnityBatch -Method FieldMedic.EditorTools.BuildTools.BuildAndroid -LogName final_build -TimeoutMin 90
"build exit $code $(Get-Date -Format HH:mm)" | Out-File $st -Append
$xr = Select-String -Path logs\final_build.log -Pattern 'XR settings registered: android loaders=1 initOnStart=True' -ErrorAction SilentlyContinue
"xr line: $([bool]$xr)" | Out-File $st -Append
Add-Type -A System.IO.Compression.FileSystem
$apk = 'Builds\FieldMedicVR.apk'
if ((Test-Path $apk) -and (Get-Item $apk).LastWriteTime -gt (Get-Date).AddMinutes(-120)) {
  $z = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $apk)); $has = ($z.Entries | ? { $_.FullName -match 'libUnityOpenXR\.so' }).Count; $z.Dispose()
  "libUnityOpenXR in APK: $has" | Out-File $st -Append
  if ($has -ge 1 -and $xr) {
    Copy-Item C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W10.apk C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W10_prev.apk -Force
    Copy-Item $apk C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W10.apk -Force
    "copied to desktop" | Out-File $st -Append
    $adb = (Get-ChildItem "C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" | Select -First 1).FullName
    $out = & $adb install -r -g C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W10.apk 2>&1
    "install: $($out -join ' ')" | Out-File $st -Append
  } else { "NOT copied (xr=$([bool]$xr) lib=$has)" | Out-File $st -Append }
} else { "no fresh APK" | Out-File $st -Append }
"done $(Get-Date -Format HH:mm)" | Out-File $st -Append
