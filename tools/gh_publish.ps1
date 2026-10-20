# Creates private repo nurzhan2/TacticalMedicine (if missing), pushes all branches (main = w9), uploads the APK as a release.
# Uses the git credential already stored on this PC; never prints it. ASCII only.
$ErrorActionPreference = 'Continue'  # PS 5.1: git progress on stderr must not abort the script
Set-Location C:\TacticalMedicine
$o = "protocol=https`nhost=github.com`n`n" | git credential fill
$tok = (($o | Select-String '^password=') -replace 'password=','')
$h = @{ Authorization = "token $tok"; 'User-Agent' = 'fmvr'; Accept = 'application/vnd.github+json' }
$owner = 'nurzhan2'; $name = 'TacticalMedicine'
try { $repo = Invoke-RestMethod -Headers $h "https://api.github.com/repos/$owner/$name"; "repo exists" }
catch {
  $body = @{ name = $name; private = $true; description = 'Bauyr - Field Medic VR (Meta Quest 3S, Unity 6): tactical combat casualty care trainer' } | ConvertTo-Json
  $repo = Invoke-RestMethod -Method Post -Headers $h -Body $body -ContentType 'application/json' https://api.github.com/user/repos
  "repo created private=$($repo.private)"
}
git remote set-url origin "https://github.com/$owner/$name.git"
git merge-base --is-ancestor main w9; if ($LASTEXITCODE -ne 0) { throw 'main is not an ancestor of w9' }
git branch -f main w9
git push -u origin main 2>&1 | Select-Object -Last 3
git push origin w9 xr-hotfix w8-interact feature/medical-core 2>&1 | Select-Object -Last 5
# release with the APK
$tag = 'w9-art-2026-10-20'
git tag -f $tag w9; git push -f origin $tag 2>&1 | Select-Object -Last 1
$rb = @{ tag_name = $tag; name = 'W9 ART build (20.10.2026)'; body = "FieldMedicVR_W9_ART.apk - Quest 3S build from branch w9 (main). Install: adb install -r -g FieldMedicVR_W9_ART.apk. Notes: docs/W9_WHATS_NEW.md"; prerelease = $false } | ConvertTo-Json
try { $rel = Invoke-RestMethod -Method Post -Headers $h -Body $rb -ContentType 'application/json' "https://api.github.com/repos/$owner/$name/releases" }
catch { $rel = Invoke-RestMethod -Headers $h "https://api.github.com/repos/$owner/$name/releases/tags/$tag" }
$apk = 'C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W9_ART.apk'
$up = "https://uploads.github.com/repos/$owner/$name/releases/$($rel.id)/assets?name=FieldMedicVR_W9_ART.apk"
$a = Invoke-RestMethod -Method Post -Headers $h -InFile $apk -ContentType 'application/vnd.android.package-archive' -TimeoutSec 3600 $up
"asset: $($a.name) $([math]::Round($a.size/1MB,1)) MB state=$($a.state)"
"release: $($rel.html_url)"
