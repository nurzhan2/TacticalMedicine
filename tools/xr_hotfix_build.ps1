# XR hotfix: rebuild the demo APK with com.meta.xr.sdk.all restored. ASCII only.
Set-Location C:\TacticalMedicine
Add-Type -Namespace K -Name A -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
[K.A]::SetThreadExecutionState([uint32]2147483649) | Out-Null
. .\tools\unity.ps1
"$(Get-Date -Format 'HH:mm:ss') START" | Out-File logs\xr_hotfix.txt
$code = Invoke-UnityBatch -Method FieldMedic.EditorTools.BuildTools.BuildAndroid -LogName xr_hotfix_build -TimeoutMin 80
"$(Get-Date -Format 'HH:mm:ss') END code=$code" | Out-File logs\xr_hotfix.txt -Append
if (Test-Path Builds\FieldMedicVR.apk) {
  $d = 'C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_DEMO_XRFIX.apk'
  Copy-Item Builds\FieldMedicVR.apk $d -Force
  "$(Get-Date -Format 'HH:mm:ss') copied $d $((Get-Item $d).Length)" | Out-File logs\xr_hotfix.txt -Append
}
[K.A]::SetThreadExecutionState([uint32]2147483648) | Out-Null
