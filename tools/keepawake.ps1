# Keeps system + display "required" until 09:00 so Modern Standby is not entered when the lid is closed. ASCII only.
Add-Type -Namespace K -Name A -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
$end = (Get-Date).Date.AddHours(23); if ((Get-Date) -gt $end) { $end = $end.AddDays(1) }
while ((Get-Date) -lt $end) {
  [K.A]::SetThreadExecutionState([uint32]2147483651) | Out-Null   # ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
  Start-Sleep -Seconds 30
}
[K.A]::SetThreadExecutionState([uint32]2147483648) | Out-Null
