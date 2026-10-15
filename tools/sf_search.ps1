$ErrorActionPreference = 'SilentlyContinue'
$qs = @(
 'CAT tourniquet','israeli bandage','combat gauze','chest seal','decompression needle','nasopharyngeal airway',
 'trauma shears','nitrile gloves','emergency blanket mylar','SAM splint','IFAK pouch','smoke grenade',
 'sandbag wall','sandbags','shell crater','burnt car lada','BMP-2','BTR-80','T-72','Su-25','Mi-24','Mi-8',
 'soldier rigged plate carrier','military stretcher','bullet casings','destroyed building ruins'
)
$out = @()
foreach ($q in $qs) {
  $u = 'https://api.sketchfab.com/v3/search?type=models&downloadable=true&count=5&sort_by=-likeCount&q=' + [uri]::EscapeDataString($q)
  try { $r = Invoke-RestMethod -Uri $u -TimeoutSec 25 } catch { $out += "$q :: ERROR $($_.Exception.Message)"; continue }
  $items = $r.results | Select-Object -First 4 | ForEach-Object { '{0} [{1}, {2}f, {3}♥] {4}' -f $_.name, $_.license.label, $_.faceCount, $_.likeCount, $_.viewerUrl }
  $out += "### $q"
  $out += $items
}
$out | Set-Content C:\TacticalMedicine\logs\sf_search.txt -Encoding UTF8
'done ' + $out.Count
