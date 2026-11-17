# Бауыр — Field Medic VR: передача проекта (17.11.2026)

VR-тренажёр тактической медицины (TCCC) для Meta Quest 3S. Unity 6000.3.26f1, URP, OpenXR + Meta XR SDK 207.
Пакет на очках: `kz.qorsa.fieldmedic`.

## Что где
- **Ветка `main`** — версия, которая стоит на очках и была на презентации 21.10 (APK `FieldMedicVR_W10.apk`
  в релизе `w10-final`). С неё и начинать.
- **`w10`** — то же самое + режим «автопрохождение» (кнопка БОТ на брифинге: игра сама делает каждый шаг раз
  в 3 с). Тесты зелёные, но на очках он **не проверялся**.
- `w9`, `xr-hotfix`, `feature/medical-core` — история. `w8-interact` — заброшенная ветка с лицом Ерлана
  (владелец лицо отклонил, не вливать).
- **Releases** на GitHub: APK (финальный W10 и W9_ART), исходники моделей, сырые записи голосов.

## Чего нет в репозитории (лицензии)
- `Assets/_Licensed/` — модели BlenderKit (Royalty-Free, распространять нельзя). Скачать своим ключом:
  `python tools/bk_fetch.py` (ключ BlenderKit — свой, в репозиторий не коммитить).
- `SourceAssets/Mixamo/` — исходники Mixamo (скачать со своего аккаунта Adobe Mixamo, имена в docs).
- Ключи API (ElevenLabs и др.) в репозитории нет и не должно быть — только переменные окружения.

## Сборка
1. Открыть проект в Unity 6000.3.26f1 (Android Build Support).
2. `tools/final_build.ps1` — сборка + проверка + копия + `adb install` на подключённые очки (без запуска).
   Или вручную: `. .\tools\unity.ps1; Invoke-UnityBatch -Method FieldMedic.EditorTools.BuildTools.BuildAndroid`.
3. **Важно (причина чёрного экрана в старых сборках):** в логе сборки должна быть строка
   `[FMVR] XR settings registered: android loaders=1 initOnStart=True`, а в APK — `lib/arm64-v8a/libUnityOpenXR.so`.
   `BuildTools.EnsureXrSettings()` это проверяет; не редактировать `Assets/XR/*.asset` руками.
   Все шейдеры через `Shader.Find` должны быть в Always Included (QuestSceneAudit).
4. Лог игры на очках: `/sdcard/Android/data/kz.qorsa.fieldmedic/files/fmvr_log.txt`. После установки игру не
   запускать скриптом с непрерывным выходом — иначе следующий запуск уйдёт в SAFE-режим (`adb shell pm clear` лечит).

## Тесты
EditMode 175/175. PlayMode: сценарные боты S1–S4, S6–S8 и самопомощь проходят; известные красные — см. ниже.

## Известные проблемы
- **Демо-обучение:** жгут на демо-ране может не засчитаться (регрессия 21.10 04:20–04:40, тест DemoFlow красный).
- **S5 «FPV»:** бот набирает 80 < 85 — не разрезает штанину над раной голени (сам сценарий проходим).
- ~10 бот-тестов (TreatmentBot, W4 CUF/TCCC) настроены под старое тело Ерлана — перенастроить под новое.
- Руки от первого лица (из модели солдата) — рабочие, но не доведены (размер/посадка на контроллере).
- Подробно: `docs/W10_AUDIT.md` (все найденные баги и проходимость сценариев), `docs/PROGRESS.md` (история).

## Управление (Quest Touch)
Хват — grip или курок (рядом с предметом, 13 см). **X** — аптечка перед глазами. **Y (держать)** — рация.
**A (держать 1,5 с)** — пропустить шаг (не в экзамене). **B (держать 2 с)** — выход в меню.

## Озвучка
ElevenLabs, русский: оператор/инструктор — голос «Egor Gadzhiyev», Ерлан — «Prince Nur».
Тексты: `tools/radio_lines.tsv`, `tools/voice_lines.tsv`. Генерация: `tools/eleven_tts.py` (ключ из
переменной `ELEVENLABS_API_KEY`). Сырые записи — в релизе (`voice_elevenlabs_raw.zip`).

## Модели
Предметы аптечки — GPT-6 Astra (исходники FBX/GLB/blend + текстуры в релизе, `models_incoming.zip`),
импорт: меню `FieldMedic/Art/Import Incoming` (`docs/ART_IMPORT.md`). Солдат (Ерлан) — модель владельца
`Soldier_KZ.fbx` (в релизе `Soldier_original.zip`).
