# XR fix 2 (18.10): register XR settings before the build, verify libUnityOpenXR.so, install + run on the Quest. ASCII only.
Set-Location C:\TacticalMedicine
Add-Type -Namespace K -Name A -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
[K.A]::SetThreadExecutionState([uint32]2147483649) | Out-Null
$st = 'logs\xr_fix2.txt'
"$(Get-Date -Format 'HH:mm:ss') START" | Out-File $st
. .\tools\unity.ps1
Remove-Item Builds\FieldMedicVR.apk -ErrorAction SilentlyContinue
$code = Invoke-UnityBatch -Method FieldMedic.EditorTools.BuildTools.BuildAndroid -LogName xr_fix2_build -TimeoutMin 90
"$(Get-Date -Format 'HH:mm:ss') BUILD END code=$code" | Out-File $st -Append
Select-String logs\xr_fix2_build.log -Pattern '\[FMVR\] XR settings|No XR Manager settings|error CS' | Select -First 5 | % { $_.Line } | Out-File $st -Append
if (Test-Path Builds\FieldMedicVR.apk) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $z = [IO.Compression.ZipFile]::OpenRead((Resolve-Path Builds\FieldMedicVR.apk).Path)
  $has = ($z.Entries | ? { $_.FullName -match 'libUnityOpenXR\.so' }).Count; $z.Dispose()
  "libUnityOpenXR in APK: $has" | Out-File $st -Append
  $d = 'C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_DEMO_FIXED.apk'
  Copy-Item Builds\FieldMedicVR.apk $d -Force
  "copied $d $([int]((Get-Item $d).Length/1MB)) MB" | Out-File $st -Append
  if ($has -gt 0) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File tools\quest_run.ps1 -Apk $d -Tag fixed -Wait 40
    "$(Get-Date -Format 'HH:mm:ss') device run done" | Out-File $st -Append
    Select-String logs\device\fixed_fmvr_log.txt -Pattern 'XR status|XR FAILED|gameplay ready' | % { $_.Line } | Out-File $st -Append
  }
}
"$(Get-Date -Format 'HH:mm:ss') ALL DONE" | Out-File $st -Append
[K.A]::SetThreadExecutionState([uint32]2147483648) | Out-Null
