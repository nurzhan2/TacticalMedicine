# Radio (commander) + training instructor placeholder lines via Windows System.Speech (Irina), then
# tools/radio_post.py (male register, radio band-pass/crackle) -> Assets/Resources/Radio/<id>_<lang>.wav.
# Kazakh lines are spoken phonetically with Russian letters (column kz_tts). Swap the WAVs for real recordings.
# Usage: powershell -ExecutionPolicy Bypass -File tools\tts_radio.ps1 [-Only id1,id2] [-Out <dir>]
# Real voices: tools/eleven_tts.py (ElevenLabs, see tools/eleven_tts.md) writes the same files.
param([string]$Only = '', [string]$Out = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$ru = $synth.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo } | Where-Object { $_.Culture.Name -eq 'ru-RU' } | Select-Object -First 1
if ($ru) { $synth.SelectVoice($ru.Name) }
$tmp = Join-Path $env:TEMP 'fmvr_tts_radio'
New-Item -ItemType Directory -Force $tmp | Out-Null
$lines = Import-Csv -Path (Join-Path $root 'tools\radio_lines.tsv') -Delimiter "`t" -Encoding UTF8
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$onlyIds = @($Only -split ',' | Where-Object { $_ -ne '' })
if ($onlyIds.Count -gt 0) { Get-ChildItem $tmp -Filter *.wav | Remove-Item -Force }
foreach ($l in $lines) {
    if ($onlyIds.Count -gt 0 -and $onlyIds -notcontains $l.id) { continue }
    foreach ($lang in @('ru', 'kz')) {
        $text = if ($lang -eq 'ru') { $l.ru } else { $l.kz_tts }
        $synth.Rate = if ($l.speaker -eq 'radio') { 2 } else { 0 }
        $synth.Volume = 100
        $wav = Join-Path $tmp ("{0}_{1}.wav" -f $l.id, $lang)
        $synth.SetOutputToWaveFile($wav, $fmt)
        $synth.Speak($text)
        $synth.SetOutputToNull()
    }
}
$synth.Dispose()
if ($Out -eq '') { $Out = Join-Path $root 'Assets\Resources\Radio' }
$postArgs = @((Join-Path $root 'tools\radio_post.py'), $tmp, $Out, (Join-Path $root 'tools\radio_lines.tsv'))
if ($onlyIds.Count -gt 0) { $postArgs += @('--only', ($onlyIds -join ',')) }
& python @postArgs
