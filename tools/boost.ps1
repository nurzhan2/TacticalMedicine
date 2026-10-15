# Overnight booster: keeps build-related processes at High priority until 09:00. ASCII only.
$names = @('Unity','Unity.Licensing.Client','java','il2cpp','bee_backend','clang','clang++','ld.lld','Bee.BeeDriver','gradle','UnityShaderCompiler')
$end = (Get-Date).Date.AddHours(9); if ((Get-Date) -gt $end) { $end = $end.AddDays(1) }
while ((Get-Date) -lt $end) {
  foreach ($n in $names) {
    Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object {
      try { if ($_.PriorityClass -ne 'High') { $_.PriorityClass = 'High' } } catch {}
    }
  }
  Start-Sleep -Seconds 20
}
