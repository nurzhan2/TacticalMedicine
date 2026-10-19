"""Real radio-operator / instructor voices from ElevenLabs (w9). One command replaces the placeholder TTS.

    python tools/eleven_tts.py --dry-run                 # what would be generated + character count (no key needed)
    python tools/eleven_tts.py                           # generate missing lines, post-process into the game
    python tools/eleven_tts.py --only h_load,evac_board --force
    python tools/eleven_tts.py --model eleven_v3         # newer model (also better Kazakh)
    python tools/eleven_tts.py --kz-model eleven_v3      # multilingual_v2 for Russian, v3 for Kazakh

Reads tools/radio_lines.tsv (id, speaker, ru, kz). Russian text from `ru`, Kazakh from `kz` (real Kazakh, not the
transliterated `kz_tts` column of the placeholder pipeline). One ElevenLabs voice per speaker from
tools/eleven_voices.json ({"radio": "<voice id>", "instructor": "<voice id>"}).

API key: ONLY from the environment variable ELEVENLABS_API_KEY (set it yourself: setx ELEVENLABS_API_KEY "...").
The key is never printed, logged or written anywhere.

Raw audio -> SourceAssets/Voice/eleven_raw/<id>_<ru|kz>.wav (48 kHz mono; the mp3 the API returns is kept next to it
in mp3/). Existing raw lines are skipped unless --force. Then tools/radio_post.py --real processes them into
Assets/Resources/Radio/<id>_<ru|kz>.wav (radio band-pass + squelch for `radio`, clean for `instructor`) - the same
file names the game already loads, so no code change is needed.
mp3 -> wav needs ffmpeg (PATH or the imageio-ffmpeg Python package); without it the script asks the API for raw
48 kHz PCM instead (same result, no mp3 kept).
"""
import argparse
import csv
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
import wave

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
API = "https://api.elevenlabs.io/v1/text-to-speech/{voice}"
PLACEHOLDER = "PASTE_"


def load_rows(path):
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(f, delimiter="\t"))


def load_voices(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return {k: v for k, v in data.items() if not k.startswith("_")}


def find_ffmpeg():
    exe = shutil.which("ffmpeg")
    if exe:
        return exe
    try:
        import imageio_ffmpeg  # optional
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        return None


def jobs_for(rows, langs, only, force, raw_dir):
    out = []
    for r in rows:
        if only and r["id"] not in only:
            continue
        for lang in langs:
            text = (r.get(lang) or "").strip()
            if not text:
                continue
            wav = os.path.join(raw_dir, f"{r['id']}_{lang}.wav")
            if os.path.exists(wav) and not force:
                continue
            out.append({"id": r["id"], "speaker": r["speaker"], "lang": lang, "text": text, "wav": wav})
    return out


def request(voice, text, model, fmt, key, settings, retries=4):
    url = API.format(voice=voice) + "?output_format=" + fmt
    body = json.dumps({"text": text, "model_id": model, "voice_settings": settings}).encode("utf-8")
    for attempt in range(retries):
        req = urllib.request.Request(url, data=body, method="POST", headers={
            "xi-api-key": key, "Content-Type": "application/json",
            "Accept": "audio/mpeg" if fmt.startswith("mp3") else "application/octet-stream"})
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                return resp.read()
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")[:400]
            if e.code in (429, 500, 502, 503) and attempt < retries - 1:
                wait = 5 * (attempt + 1)
                print(f"   HTTP {e.code}, retry in {wait} s")
                time.sleep(wait)
                continue
            raise RuntimeError(f"HTTP {e.code}: {detail}")
        except urllib.error.URLError as e:
            if attempt < retries - 1:
                time.sleep(5)
                continue
            raise RuntimeError(f"network error: {e.reason}")


def write_pcm_wav(path, pcm, rate=48000):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(pcm)


def main():
    try:  # Windows consoles default to cp1251/cp866: Kazakh letters must not crash the printout
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description="ElevenLabs voices for Field Medic VR (radio + instructor lines)")
    ap.add_argument("--table", default=os.path.join(ROOT, "tools", "radio_lines.tsv"))
    ap.add_argument("--voices", default=os.path.join(ROOT, "tools", "eleven_voices.json"))
    ap.add_argument("--raw", default=os.path.join(ROOT, "SourceAssets", "Voice", "eleven_raw"))
    ap.add_argument("--out", default=os.path.join(ROOT, "Assets", "Resources", "Radio"))
    ap.add_argument("--model", default="eleven_multilingual_v2", help="eleven_multilingual_v2 (default) or eleven_v3")
    ap.add_argument("--kz-model", default="eleven_v3", help="model for Kazakh lines (default eleven_v3: Kazakh is supported ONLY by Eleven v3)")
    ap.add_argument("--lang", default="ru,kz", help="ru,kz | ru | kz")
    ap.add_argument("--only", default="", help="comma list of line ids")
    ap.add_argument("--force", action="store_true", help="regenerate lines that already exist")
    ap.add_argument("--dry-run", action="store_true", help="print what would be generated and the character count")
    ap.add_argument("--no-post", action="store_true", help="only download raw audio, skip radio_post.py")
    ap.add_argument("--stability", type=float, default=0.55)
    ap.add_argument("--similarity", type=float, default=0.8)
    ap.add_argument("--style", type=float, default=0.0)
    args = ap.parse_args()

    rows = load_rows(args.table)
    voices = load_voices(args.voices)
    only = set(x.strip() for x in args.only.split(",") if x.strip())
    langs = [x.strip() for x in args.lang.split(",") if x.strip()]
    unknown = only - {r["id"] for r in rows}
    if unknown:
        print("unknown ids:", ", ".join(sorted(unknown)))
        return 2
    jobs = jobs_for(rows, langs, only, args.force, args.raw)
    chars = sum(len(j["text"]) for j in jobs)
    by_speaker = {}
    for j in jobs:
        by_speaker[j["speaker"]] = by_speaker.get(j["speaker"], 0) + len(j["text"])

    if args.dry_run:
        for j in jobs:
            model = args.kz_model if (j["lang"] == "kz" and args.kz_model) else args.model
            print(f"  {j['id']:<14} {j['lang']}  {j['speaker']:<10} {model:<24} {len(j['text']):>4}  {j['text']}")
        print(f"[eleven] dry run: {len(jobs)} line(s), {chars} characters "
              f"({', '.join(f'{k} {v}' for k, v in sorted(by_speaker.items()))}); nothing was sent.")
        missing = sorted({j["speaker"] for j in jobs if not voices.get(j["speaker"]) or voices[j["speaker"]].startswith(PLACEHOLDER)})
        if missing:
            print("[eleven] note: voice id not set yet for: " + ", ".join(missing) + " (tools/eleven_voices.json)")
        return 0

    if not jobs:
        print("[eleven] nothing to do (all lines exist; use --force to regenerate)")
    key = os.environ.get("ELEVENLABS_API_KEY", "").strip()
    if jobs and not key:
        print("[eleven] ELEVENLABS_API_KEY is not set. Run:  setx ELEVENLABS_API_KEY \"<your key>\"  and open a new terminal.")
        return 3
    for sp in sorted({j["speaker"] for j in jobs}):
        v = voices.get(sp, "")
        if not v or v.startswith(PLACEHOLDER):
            print(f"[eleven] no voice id for speaker '{sp}' in {args.voices}")
            return 4

    os.makedirs(args.raw, exist_ok=True)
    ffmpeg = find_ffmpeg()
    fmt = "mp3_44100_128" if ffmpeg else "pcm_48000"
    if not ffmpeg:
        print("[eleven] ffmpeg not found - requesting 48 kHz PCM directly")
    settings = {"stability": args.stability, "similarity_boost": args.similarity, "style": args.style, "use_speaker_boost": True}
    done, failed, sent = [], [], 0
    for k, j in enumerate(jobs, 1):
        model = args.kz_model if (j["lang"] == "kz" and args.kz_model) else args.model
        print(f"[{k}/{len(jobs)}] {j['id']}_{j['lang']} ({j['speaker']}, {model}, {len(j['text'])} chars)")
        try:
            audio = request(voices[j["speaker"]], j["text"], model, fmt, key, settings)
            sent += len(j["text"])
            if fmt.startswith("mp3"):
                mp3_dir = os.path.join(args.raw, "mp3")
                os.makedirs(mp3_dir, exist_ok=True)
                mp3 = os.path.join(mp3_dir, f"{j['id']}_{j['lang']}.mp3")
                with open(mp3, "wb") as f:
                    f.write(audio)
                subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-i", mp3, "-ac", "1", "-ar", "48000", "-sample_fmt", "s16", j["wav"]], check=True)
            else:
                write_pcm_wav(j["wav"], audio, 48000)
            done.append(j["id"])
        except Exception as e:  # keep going, report at the end
            print("   FAILED:", e)
            failed.append(f"{j['id']}_{j['lang']}")
    print(f"[eleven] generated {len(done)} file(s), {sent} characters sent" + (f"; failed: {', '.join(failed)}" if failed else ""))

    if not args.no_post:
        ids = sorted(set(done)) if done else sorted(only) if only else []
        cmd = [sys.executable, os.path.join(ROOT, "tools", "radio_post.py"), args.raw, args.out, args.table, "--real", "--sr", "44100"]
        if ids:
            cmd += ["--only", ",".join(ids)]
        if ids or not jobs:
            subprocess.run(cmd, check=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
