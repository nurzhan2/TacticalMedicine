# Progress log (append, newest at bottom)

## 10.10 02:45 — setup (Claude in chat session)
- Project retargeted to Unity 6.3 LTS (6000.3.26f1); Meta XR SDK 207 (all) imported; opens in batchmode; 11 EditMode tests pass.
- Medical core done: `Assets/Scripts/Medical` (Wound, PatientPhysiology, CareProtocol) + tests.
- Assets downloaded: `SourceAssets/Mixamo/*.fbx` (Swat Guy skin 115 MB git-ignored + 19 animations), `SourceAssets/Audio/freesound_manifest.tsv` (~270 CC0/CC-BY candidates).
- Docs: CLAUDE.md, docs/GDD.md, docs/RESEARCH.md, docs/prompts/s1..s7.
- Night runner: run_all.ps1 + watchdog.ps1 (Claude Code CLI, stages s1..s7).
- Owner decisions: character = Mixamo Swat Guy; gore = maximum naturalistic; voice = Russian + Kazakh; power settings handled by owner.
