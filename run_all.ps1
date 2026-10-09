# Night runner: executes stages s1..s7 with Claude Code CLI. ASCII only (PowerShell 5).
# Each stage is done when logs\<stage>.done exists. Retries with waits (usage limits).
$ErrorActionPreference = 'Continue'
$root = 'C:\TacticalMedicine'
Set-Location $root
New-Item -ItemType Directory -Force logs | Out-Null
Add-Type -Namespace W -Name P -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
[W.P]::SetThreadExecutionState([uint32]2147483649) | Out-Null
$stages = @('s1-foundation','s2-casualty','s3-treatment','s4-environment','s5-scenarios','s6-build','s7-polish')
$log = "$root\logs\runner.log"

foreach ($s in $stages) {
  if (Test-Path "logs\$s.done") { continue }
  for ($try = 1; $try -le 10; $try++) {
    if (Test-Path "logs\$s.done") { break }
    Add-Content $log "$(Get-Date -Format 'dd.MM HH:mm') START $s try $try"
    if ($try -eq 1 -and -not (Test-Path "logs\$s.log")) {
      $p = "Read CLAUDE.md, docs/GDD.md, docs/RESEARCH.md, docs/PROGRESS.md and docs/prompts/$s.md. Execute stage $s fully and autonomously; never ask questions; commit and push often; append progress to docs/PROGRESS.md. When the done-criteria are met, create logs/$s.done with a one-paragraph summary."
    } else {
      $p = "Stage $s was interrupted before completion (usage limit or crash). Read CLAUDE.md, docs/prompts/$s.md, docs/PROGRESS.md, git log -15 and git status; check what is already done (also make sure no stale Unity process holds the project); finish stage $s fully and autonomously. When the done-criteria are met, create logs/$s.done with a one-paragraph summary."
    }
    $t0 = Get-Date
    claude -p $p --dangerously-skip-permissions --model opus --output-format stream-json --verbose *>> "logs\$s.log"
    $mins = [math]::Round(((Get-Date) - $t0).TotalMinutes, 1)
    Add-Content $log "$(Get-Date -Format 'dd.MM HH:mm') END $s try $try exit $LASTEXITCODE after $mins min"
    if (Test-Path "logs\$s.done") { break }
    # short run usually means usage limit / startup failure -> wait longer
    if ($mins -lt 3) { Start-Sleep -Seconds 900 } else { Start-Sleep -Seconds 60 }
  }
  if (-not (Test-Path "logs\$s.done")) { Add-Content $log "$(Get-Date -Format 'dd.MM HH:mm') GAVE UP $s after 10 tries, moving on" }
}
Add-Content $log "ALL DONE $(Get-Date -Format 'dd.MM HH:mm')"
[W.P]::SetThreadExecutionState([uint32]2147483648) | Out-Null
