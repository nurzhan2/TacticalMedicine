# Waits for the w9 runner (a/b/c) to exit, then runs stage w9d-art. ASCII only.
$ErrorActionPreference = 'Continue'
$root = 'C:\TacticalMedicine'
Set-Location $root
Add-Type -Namespace W -Name P -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
[W.P]::SetThreadExecutionState([uint32]2147483649) | Out-Null
$log = "$root\logs\runner_w9.log"
while (Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*run_w9.ps1*' }) { Start-Sleep -Seconds 60 }
$s = 'w9d-art'
for ($try = 1; $try -le 10; $try++) {
  if (Test-Path "logs\$s.done") { break }
  Add-Content $log "$(Get-Date -Format 'dd.MM HH:mm') START $s try $try"
  if ($try -eq 1 -and -not (Test-Path "logs\$s.log")) {
    $p = "Read CLAUDE.md, docs/GDD_V2.md, docs/PROGRESS.md, docs/SUPERVISOR_NOTES.md and docs/prompts/$s.md. First make sure the git branch is w9 (git checkout w9) and no stale Unity process holds the project. Execute stage $s fully and autonomously; never ask questions; work only on git branch w9, commit often (git push fails - do not push); append progress to docs/PROGRESS.md. When the done-criteria are met, create logs/$s.done with a one-paragraph summary."
  } else {
    $p = "Stage $s was interrupted before completion (usage limit or crash). Read CLAUDE.md, docs/SUPERVISOR_NOTES.md, docs/prompts/$s.md, docs/PROGRESS.md, git log -15 and git status; make sure the branch is w9 and no stale Unity process holds the project; finish stage $s fully and autonomously. When the done-criteria are met, create logs/$s.done with a one-paragraph summary."
  }
  $t0 = Get-Date
  claude -p $p --dangerously-skip-permissions --model opus --output-format stream-json --verbose *>> "logs\$s.log"
  $mins = [math]::Round(((Get-Date) - $t0).TotalMinutes, 1)
  Add-Content $log "$(Get-Date -Format 'dd.MM HH:mm') END $s try $try exit $LASTEXITCODE after $mins min"
  if (Test-Path "logs\$s.done") { break }
  if ($mins -lt 3) { Start-Sleep -Seconds 900 } else { Start-Sleep -Seconds 60 }
}
Add-Content $log "W9D DONE $(Get-Date -Format 'dd.MM HH:mm')"
[W.P]::SetThreadExecutionState([uint32]2147483648) | Out-Null
