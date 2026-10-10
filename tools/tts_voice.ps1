# Placeholder voice lines via Windows System.Speech (prefers the Russian voice "Irina").
# Reads tools/voice_lines.tsv and writes raw TTS to %TEMP%\fmvr_tts\<id>_<lang>.wav, then runs
# tools/voice_post.py to pitch the female TTS voice down to a male register and add pain strain,
# breathiness and tremor -> Assets/Audio/Voice/<id>_<lang>.wav.
# Kazakh lines are spoken phonetically with Russian letters (column kz_tts).
# Usage: powershell -ExecutionPolicy Bypass -File tools\tts_voice.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voices = $synth.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo }
$ru = $voices | Where-Object { $_.Culture.Name -eq 'ru-RU' } | Select-Object -First 1
if ($ru) { $synth.SelectVoice($ru.Name); Write-Host "[tts] voice: $($ru.Name)" }
else { Write-Host "[tts] no Russian voice installed, using default $($synth.Voice.Name)" -ForegroundColor Yellow }

$tmp = Join-Path $env:TEMP 'fmvr_tts'
New-Item -ItemType Directory -Force $tmp | Out-Null
$lines = Import-Csv -Path (Join-Path $root 'tools\voice_lines.tsv') -Delimiter "`t" -Encoding UTF8
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
foreach ($l in $lines) {
    foreach ($lang in @('ru', 'kz')) {
        $text = if ($lang -eq 'ru') { $l.ru } else { $l.kz_tts }
        # emotional delivery: screams fast and loud, weak lines slow
        $rate = switch ($l.emotion) { 'scream' { 2 } 'panic' { 3 } 'gasp' { 0 } 'weak' { -3 } 'dazed' { 1 } default { 1 } }
        $synth.Rate = $rate
        $synth.Volume = 100
        $out = Join-Path $tmp ("{0}_{1}.wav" -f $l.id, $lang)
        $synth.SetOutputToWaveFile($out, $fmt)
        $synth.Speak($text)
        $synth.SetOutputToNull()
    }
}
$synth.Dispose()
Write-Host "[tts] raw lines in $tmp"
& python (Join-Path $root 'tools\voice_post.py') $tmp (Join-Path $root 'Assets\Audio\Voice') (Join-Path $root 'tools\voice_lines.tsv')
