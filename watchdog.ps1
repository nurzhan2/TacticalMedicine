# Night watchdog: keeps PC awake, restarts run_all.ps1 if it is not running and work is not finished.
# ASCII only (PowerShell 5).
$root = 'C:\TacticalMedicine'
Add-Type -Namespace W -Name P -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
[W.P]::SetThreadExecutionState([uint32]2147483649) | Out-Null
$log = "$root\logs\runner.log"
New-Item -ItemType Directory -Force "$root\logs" | Out-Null
Add-Content $log "$(Get-Date -Format 'dd.MM HH:mm') WATCHDOG started"
while ($true) {
  if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'ALL DONE' -Quiet)) { break }
  $running = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match 'run_all\.ps1' }
  if (-not $running) {
    Add-Content $log "$(Get-Date -Format 'dd.MM HH:mm') WATCHDOG: run_all not running -> (re)start"
    Start-Process powershell -WindowStyle Minimized -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',"$root\run_all.ps1"
    Start-Sleep -Seconds 60
  }
  Start-Sleep -Seconds 120
}
Add-Content $log "$(Get-Date -Format 'dd.MM HH:mm') WATCHDOG exit"
[W.P]::SetThreadExecutionState([uint32]2147483648) | Out-Null
