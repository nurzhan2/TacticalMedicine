# Stage w9b - new scenario "Самопомощь / Өзіне-өзі көмек" (player is wounded, nobody around)

Branch `w9`, continue after w9a. Same guard rails as w9a (XR guard, Always Included shaders, Quest budget,
no face from w8, no push). Owner's request: "продумай ещё от лица раненого что делать если тебя подбили, а
рядом никого нет". This is a TCCC self-aid drill. The design below is fixed - implement it, keep it simple
and robust, reuse existing systems (bleeding model in Medical/, tourniquet logic, radio, debrief, drones).

## Story (one scenario, ~4-6 min)
The player moves alone between positions (carrying a box of ammo along a trench / tree line, scene
Main_* that already has drones). An FPV drone or mortar fragment hits him. Random wound per run:
- A (always implemented): arterial bleeding from the LEFT forearm or upper arm (or right, mirrored - pick
  the hand opposite to the dominant hand setting; default left).
- B (implement if A is finished and green): bleeding from the left thigh - the player sits/lies on the
  ground; show simple legs when looking down (low-poly trousers + boots, reuse Erlan's lower-body mesh),
  movement only by crawling (slow, head kept low).

Hit moment: strong haptic on both controllers, red flash + vignette (no camera rotation, no shake of the
horizon - comfort), muffled audio with tinnitus ringing for ~5 s, then fast heartbeat sound and pulse
haptics in the hands (reuse Haptics pulse beat), blood spurting from the wound in rhythm with the heart.

Blood loss model: a visible clock in the world is NOT shown; instead the screen slowly desaturates and a
tunnel vignette grows with blood loss; at ~40 % of the lethal amount hands start to shake slightly,
at ~70 % vision dims hard. Lethal time if nothing is done: arm ~3 min, thigh ~2 min (use Medical/ physiology
if possible). A correct tourniquet stops the loss.

## Steps the player must do (TCCC Care Under Fire -> Tactical Field Care, for yourself)
1. Get out of the line of fire: crawl/move into the nearest cover (trench bay, crater, wreck) within
   10-15 m. Staying in the open for >10 s -> the drone comes back for a second pass (danger, debrief error).
2. Self tourniquet with the OTHER hand from the 1st-echelon quick-access pouch (rip-away IFAK on the
   chest - the one from w9a). Arm: put the loop over the wounded hand, slide it high on the upper arm,
   pull the strap tight with the free hand, twist the windlass until bleeding stops, lock it in the clip.
   The wounded hand is weak: it cannot grab anything (grip disabled, glove bloody) until the TQ is on.
   Upper arm position is not tracked - estimate the elbow/shoulder from the head pose and the hand
   (simple 2-bone IK) and place a visible sleeve segment there for the TQ to wrap around.
   Thigh variant: loop over the foot, high and tight near the groin, twist, lock.
3. Check: bleeding stopped (no spurts, pulse haptic in the wounded hand fades out).
4. Mark the time on the TQ tab with the marker (one-handed).
5. Report on the radio (radio button on the left shoulder as in other scenarios): the player picks the
   lines of a short report from a radial/list menu: "Я трёхсотый" (I am wounded) -> where (position name)
   -> what (arm / leg, arterial bleeding) -> what is done (tourniquet at HH:MM) -> "нужна эвакуация". The
   operator answers (new radio lines below).
6. Stay alive until help comes (~60-90 s): keep low in cover, put on the rescue blanket from the kit
   (one-handed: grab, it unfolds over the player as a vignette/overlay + crinkle sound), answer the
   operator's radio check-ins within 10 s each (press the radio button) - if not, the operator calls
   again louder and the debrief records "не отвечал на связь".
7. Comrades arrive (2 existing bots), one says "Держись, брат, свои" and the scene fades -> debrief.

Fail: bleed out (fade to black, debrief explains what would have saved him), or killed by the drone after
staying in the open twice.

## Debrief items (RU/KZ like the other scenarios)
Укрылся до самопомощи / жгут наложен за N с / жгут высоко и туго / кровь остановлена / время записано /
доклад по рации полный / одеяло / отвечал на связь / не выходил из укрытия. Plus a short "почему это
важно" line for each mistake (TCCC: cover first, tourniquet within ~1 min for arterial bleeding, always
carry the TQ where BOTH hands can reach it, write the time, keep talking to your unit, prevent hypothermia).

## New radio/instructor lines (add to tools/radio_lines.tsv, speaker `radio` unless noted)
- sa_hit (instructor): "Ты ранен. Сначала в укрытие, потом жгут." / "Сен жараландың. Алдымен баспанаға, сосын жгут."
- sa_tq (instructor): "Жгут из быстрого подсумка. Свободной рукой. Высоко и туго." / "Жылдам подсумкадан жгут ал. Бос қолыңмен. Жоғары әрі қатты."
- sa_time (instructor): "Запиши время на ленте жгута." / "Жгут таспасына уақытты жаз."
- sa_report (instructor): "Доложи по рации: кто, где, что, что сделано." / "Рациямен баянда: кім, қайда, не болды, не істелді."
- sa_ack: "Принял, трёхсотый. Группа выдвигается к тебе. Оставайся в укрытии, будь на связи." / "Қабылдадым. Топ саған шықты. Баспанада қал, байланыста бол."
- sa_check: "Медик, как ты? Ответь." / "Медик, қалайсың? Жауап бер."
- sa_check2: "Медик! Ответь! Не спи!" / "Медик! Жауап бер! Ұйықтама!"
- sa_blanket (instructor): "Укройся одеялом. Не дай себе замёрзнуть." / "Көрпемен жабын. Тоңып қалма."
- sa_arrive: "Держись, брат, свои!" / "Шыда, бауырым, өзіміздікілер!"
Generate placeholder wavs with the existing pipeline; the real ElevenLabs voices come later via
tools/eleven_tts.py (w9a).

## Menu
Add the scenario to the scenario list (RU "Самопомощь: один под огнём", KZ "Өзіне-өзі көмек: оқ астында
жалғыз") and a short training lesson "Жгут на себя" (one-handed self TQ on the arm, step by step) to the
training menu.

## Tests and done criteria
- A ScenarioBot PlayMode test that plays variant A correctly (cover -> TQ -> time -> report -> blanket
  -> check-ins) and gets a full debrief; one that does nothing and bleeds out; one that stays in the open.
- All tests green, QuestSceneAudit passes, Android build succeeds with the XR guard line.
- Commit on w9, append a Russian summary to docs/PROGRESS.md (including whether variant B got done),
  create logs/w9b-selfaid.done.
