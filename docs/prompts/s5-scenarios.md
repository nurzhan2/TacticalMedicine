# Stage S5 — Scenarios, training, menu, debrief

Read CLAUDE.md, docs/GDD.md (§3, §4, §9, §11), docs/RESEARCH.md (§7), docs/PROGRESS.md. Time budget ~120 min.

## Tasks
1. **Scenario framework** (`Runtime/Scenarios`): `ScenarioDefinition` (id, titles RU/KZ, briefing, setup: casualty pose/position, wounds with variants, phase timeline, scripted events from CombatAmbience, complications with triggers/timers, evac time), `ScenarioDirector` (state: Briefing → Incident → CareUnderFire → TacticalFieldCare → Evac/Debrief; drives casualty animations, physiology, threats, radio lines), `ScenarioRegistry`. Variants: side L/R, second drone drop, delayed onset.
2. Implement **S1 «Сброс»** fully first (the showcase), then S2 «Снайпер», S4 «Миномёт», S6 «КАБ», S3 «Мина», S5 «FPV», S7 «Под огнём», S8 «До рассвета» (time-skip milestones UI on the wrist watch). Extend the Medical model for anything missing (amputation stump TQ rules, hidden back wounds, eardrum rupture flag, vomit/airway, conversion rules & timings, TQ slipping) with EditMode tests.
3. **Training mode**: lessons (Tourniquet, Packing & pressure, Chest seal & needle, Drag & log roll, Airway/NPA) with instructor voice (TTS RU/KZ), ghost-hand guide arrows, step checklist panel, instructor vitals monitor (only in training).
4. **Menu & debrief board** in the dugout: big **round animated buttons** (pulse on hover, press depth), mode select, scenario cards, language RU/KZ, hints toggle, gore level, seated mode, dominant hand. Debrief: timeline of actions with timestamps, mistakes with explanation & consequence, survival / limb outcome, score, best score saved (PlayerPrefs).
5. **Radio** (handheld/vest radio): commander lines («Воздух!», «Эвакуация через 40 минут», «Доложи обстановку»), player can press-to-talk to "request MEDEVAC" (simplified checklist).
6. **Bots & tests**: PlayMode test per scenario — MedicBot performs the correct protocol → survival, score ≥ 85; a "wrong" variant per scenario → expected mistakes recorded. Desktop simulator: make S1 comfortably playable with mouse/keyboard (document controls).
7. Screenshots per scenario and of menu/debrief (`docs/screens/s5_*.png`).

## Done criteria
All 8 scenarios selectable and playable end-to-end (S1–S2 polished), tests green, pushed; `logs/s5-scenarios.done`.

## Morning checkpoint (added by night supervisor 07:05 - mandatory)
The owner wakes up around 09:30 local time and stages run ~65-70 min each, so S6 would come too late. Therefore, as the LAST step of this stage (before writing logs/<stage>.done):
1. Build the APK with BuildTools.BuildAndroid (the Android build cache was cleared to save disk, first build is a full IL2CPP build ~20 min; check free disk >= 5 GB first).
2. Copy it to C:\Users\user\Desktop\FieldMedicVR.apk (overwrite).
3. Write/refresh docs/MORNING_REPORT.md (Russian, short, honest): what works now, how to install (MQDH drag&drop or adb install -r), what to try first in the headset, screenshots list, what is still in progress (the night pipeline keeps running).
4. Commit + push.
If the local time is already past 09:00 when you start this stage, do the checkpoint FIRST (build what exists), then continue the stage.