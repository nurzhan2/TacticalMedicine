# Voice script — Ерлан (casualty) and player lines

Placeholder audio is Windows TTS (Microsoft Irina, ru-RU) pitched down to a male register with pain strain
(`tools/tts_voice.ps1` → `tools/voice_post.py`). Kazakh lines are spoken *phonetically* by the Russian voice — **must be re-recorded by a native speaker**.

## How to replace with real recordings
1. Record each line as mono WAV (44.1 or 48 kHz, 16-bit), dry (no reverb), peak about −1 dBFS, 150 ms silence trimmed at the ends.
2. Name the file exactly `<id>_ru.wav` or `<id>_kz.wav` and overwrite it in `Assets/Audio/Voice/`.
3. Unity re-imports automatically; `VoiceBank` (Assets/Data/Voice/VoiceBank.asset) finds clips by file name — run *FieldMedic → Casualty → Rebuild Voice Bank* if you add new ids.
4. Record 2–3 takes of the screams/groans if you can (`hurts_ru_2.wav`, …) — the bank picks random variants with suffix `_2`, `_3`.

Vocalisations without words (groans, screams, gasps, coughs, breathing, snoring) come from Freesound (see ATTRIBUTION.md) and are pooled in the VoiceBank.

| id | speaker | emotion / delivery | strain | RU | KZ | KZ (TTS phonetic) |
|---|---|---|---|---|---|---|
| `hit_leg` | Ерлан | крик боли | 1.0 | Нога! Ногу не чувствую! | Аяғым! Аяғым! | Аягым! Аягым! |
| `hit_mixed` | Ерлан | крик боли | 1.0 | Аяғым! Нога! | Аяғым! Нога! | Аягым! Нога! |
| `wounded` | Ерлан | паника, быстро | 0.8 | Брат, я ранен! | Бауырым, жараландым! | Бауырым, жараландым! |
| `blood_much` | Ерлан | паника, быстро | 0.7 | Кровь... много крови... | Қан кетіп жатыр... | Кан кетип жатыр... |
| `cant_breathe` | Ерлан | задыхается, рвано | 0.9 | Не могу дышать... задыхаюсь... | Тыныс ала алмаймын... | Тыныс ала алмаймын... |
| `cold` | Ерлан | слабо, тихо, медленно | 0.5 | Холодно... | Суық... | Суык... |
| `water` | Ерлан | слабо, тихо, медленно | 0.5 | Пить... дай пить... | Су бер... | Су бер... |
| `take_off` | Ерлан | крик боли | 1.0 | Сними, сними! Больно! | Шеш, шеш! Ауырады! | Шеш, шеш! Ауырады! |
| `hurts` | Ерлан | крик боли | 0.9 | Больно! | Ауырады! | Ауырады! |
| `help` | Ерлан | паника, быстро | 0.8 | Помоги! | Көмектес! | Кёмектес! |
| `will_i_die` | Ерлан | слабо, тихо, медленно | 0.6 | Я умру? | Өлемін бе? | Олемин бе? |
| `tell_mom` | Ерлан | слабо, тихо, медленно | 0.7 | Маме скажи... | Анама айт... | Анама айт... |
| `where_leg` | Ерлан | паника, быстро | 0.8 | Где нога?.. Где моя нога? | Аяғым қайда? | Аягым кайда? |
| `what_happened` | Ерлан | оглушён, громко, переспрашивает | 0.4 | Что было? Где мы? | Не болды? Қайдамыз? | Не болды? Кайдамыз? |
| `cant_hear` | Ерлан | оглушён, громко, переспрашивает | 0.4 | Не слышу! Что?! | Естімеймін! | Естимеймин! |
| `dizzy` | Ерлан | слабо, тихо, медленно | 0.5 | Голова кружится... | Басым айналады... | Басым айналады... |
| `dont_leave` | Ерлан | паника, быстро | 0.7 | Не бросай меня! | Мені тастама! | Мени тастама! |
| `holding` | Ерлан | слабо, тихо, медленно | 0.5 | Держусь... держусь... | Шыдаймын... | Шыдаймын... |
| `brother` | Ерлан | слабо, тихо, медленно | 0.6 | Бауырым... | Бауырым... | Бауырым... |
| `thanks` | Ерлан | слабо, тихо, медленно | 0.4 | Спасибо, брат... | Рахмет, бауырым... | Рахмет, бауырым... |
| `drone_above` | Ерлан | ровно, сдержанно | 0.0 | Птичка над нами. Тихо. | Дрон! Үстімізде! | Дрон! Устимизде! |
| `radio_air` | Ерлан | ровно, сдержанно | 0.0 | Воздух! Все на дно! | Ауа! Жатыңдар! | Ауа! Жатындар! |
| `look_at_me` | игрок | ровно, сдержанно | 0.0 | Смотри на меня! | Маған қара! | Маган кара! |
| `breathe` | игрок | ровно, сдержанно | 0.0 | Дыши! Дыши, брат! | Дем ал! Дем ал, бауырым! | Дем ал! Дем ал, бауырым! |
| `hold_on` | игрок | ровно, сдержанно | 0.0 | Держись, я рядом. | Шыда, мен осындамын. | Шыда, мен осындамын. |

Strain 0..1 = how much pain/effort is audible (used by the TTS post-process; for actors: 0 = normal voice, 1 = through clenched teeth / screaming).
Kazakh phrases from docs/RESEARCH.md §6 — verify with a native speaker ("Жараландым", "Естімеймін", "Шыда, мен осындамын" are my additions).
