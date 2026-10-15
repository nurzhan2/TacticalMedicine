# Unity batch helpers for Field Medic VR. ASCII only (PowerShell 5).
# Usage:  . .\tools\unity.ps1 ; Invoke-UnityBatch -Method FieldMedic.EditorTools.ProjectSetup.Apply
#         Invoke-UnityTests -Platform EditMode
$Global:UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'
$Global:FmvrProject = 'C:\TacticalMedicine'
# w4: the editor's D3D11 path on this laptop's Iris Xe renders nondeterministic speckle/block noise (driver);
# Vulkan renders clean and matches the Quest's API. Every batch run uses it.
$Global:FmvrGfx = @('-force-vulkan')

function Test-UnityProjectFree {
    $lock = Join-Path $Global:FmvrProject 'Temp\UnityLockfile'
    if (Test-Path $lock) {
        try {
            $fs = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
            $fs.Close()
        } catch {
            Write-Host "[unity.ps1] project is locked by another Unity instance ($lock)" -ForegroundColor Red
            return $false
        }
    }
    return $true
}

function Show-UnityLogProblems([string]$LogFile) {
    if (-not (Test-Path $LogFile)) { Write-Host "[unity.ps1] no log $LogFile"; return 0 }
    $errs = Select-String -Path $LogFile -Pattern 'error CS\d+' | Select-Object -ExpandProperty Line | Sort-Object -Unique
    foreach ($e in $errs) { Write-Host $e -ForegroundColor Red }
    Select-String -Path $LogFile -Pattern '\[FMVR\]' | ForEach-Object { Write-Host $_.Line -ForegroundColor Cyan }
    Select-String -Path $LogFile -Pattern '(Exception|Build Failed|Failed to|Error building)' |
        Where-Object { $_.Line -notmatch 'NullReferenceException: Object reference not set.*Meta.XR.Editor' } |
        Select-Object -First 25 | ForEach-Object { Write-Host $_.Line -ForegroundColor Yellow }
    return @($errs).Count
}

function Start-UnityProcess([string[]]$UnityArgs, [int]$TimeoutMin) {
    $quoted = $UnityArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $Global:UnityExe -ArgumentList $quoted -PassThru -NoNewWindow
    $null = $p.Handle  # cache the handle, otherwise ExitCode is empty after WaitForExit(timeout)
    if (-not $p.WaitForExit($TimeoutMin * 60 * 1000)) {
        Write-Host "[unity.ps1] timeout after $TimeoutMin min, killing Unity" -ForegroundColor Red
        try { $p.Kill() } catch {}
        return 999
    }
    $p.WaitForExit()
    return $p.ExitCode
}

function Invoke-UnityBatch {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [string]$LogName = '',
        [switch]$NoGraphics,
        [string[]]$ExtraArgs = @(),
        [int]$TimeoutMin = 60
    )
    if (-not (Test-UnityProjectFree)) { return $false }
    if ($LogName -eq '') { $LogName = ($Method -split '\.')[-1] }
    $log = Join-Path $Global:FmvrProject "Logs\$LogName.log"
    New-Item -ItemType Directory -Force (Join-Path $Global:FmvrProject 'Logs') | Out-Null
    $a = @('-batchmode', '-quit', '-projectPath', $Global:FmvrProject, '-buildTarget', 'Android',
           '-executeMethod', $Method, '-logFile', $log)
    if ($NoGraphics) { $a += '-nographics' } else { $a += $Global:FmvrGfx }
    $a += $ExtraArgs
    $t0 = Get-Date
    $code = Start-UnityProcess $a $TimeoutMin
    $mins = [math]::Round(((Get-Date) - $t0).TotalMinutes, 1)
    $nerr = Show-UnityLogProblems $log
    $ok = ($code -eq 0) -and ($nerr -eq 0)
    Write-Host "[unity.ps1] $Method exit=$code compileErrors=$nerr time=${mins}min -> $(if ($ok) {'OK'} else {'FAILED'})"
    return $ok
}

function Invoke-UnityTests {
    param(
        [ValidateSet('EditMode', 'PlayMode')][string]$Platform = 'EditMode',
        [string]$Filter = '',
        [string]$Category = '',
        [int]$TimeoutMin = 40
    )
    if (-not (Test-UnityProjectFree)) { return $false }
    $p = $Platform.ToLower()
    $xml = Join-Path $Global:FmvrProject "Logs\$p.xml"
    $log = Join-Path $Global:FmvrProject "Logs\$p.log"
    if (Test-Path $xml) { Remove-Item $xml -Force }
    $a = @('-batchmode', '-projectPath', $Global:FmvrProject, '-buildTarget', 'Android', '-runTests',
           '-testPlatform', $Platform, '-testResults', $xml, '-logFile', $log)
    $a += $Global:FmvrGfx
    if ($Filter -ne '') { $a += @('-testFilter', $Filter) }
    if ($Category -ne '') { $a += @('-testCategory', $Category) }
    $t0 = Get-Date
    $code = Start-UnityProcess $a $TimeoutMin
    $mins = [math]::Round(((Get-Date) - $t0).TotalMinutes, 1)
    $nerr = Show-UnityLogProblems $log
    if (-not (Test-Path $xml)) {
        Write-Host "[unity.ps1] no test results ($xml), exit=$code" -ForegroundColor Red
        return $false
    }
    [xml]$r = Get-Content $xml
    $run = $r.'test-run'
    Write-Host "[unity.ps1] $Platform tests: total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped) time=${mins}min"
    $r.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
        Write-Host "  FAIL $($_.fullname)" -ForegroundColor Red
        $m = $_.SelectSingleNode('failure/message')
        if ($m) { Write-Host "     $($m.InnerText.Trim())" -ForegroundColor Red }
    }
    return ([int]$run.failed -eq 0) -and ($nerr -eq 0) -and ([int]$run.total -gt 0)
}
