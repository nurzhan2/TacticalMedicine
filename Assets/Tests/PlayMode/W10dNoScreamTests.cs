using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FieldMedic.Audio;
using FieldMedic.Battle;
using FieldMedic.Casualty;
using FieldMedic.Environment;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using FieldMedic.Treatment;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w10d, owner (twice): «надо крики убрать, пусть только пули и взрывы на фоне». Nothing the game plays may be a
    /// scream / shout / crowd / "Medic!" / wounded voice: the background policy filters the voice-carrying beds,
    /// the battlefield runs 25 s with only gunfire/explosions/drones, S1 runs with Erlan unmuted and he never plays
    /// a scream recording. Erlan's real ElevenLabs lines exist and answer the AVPU / «где болит?» questions.
    /// </summary>
    public class W10dNoScreamTests
    {
        static readonly Regex Banned = new Regex("scream|shout|yell|crowd|medic|wounded", RegexOptions.IgnoreCase);
        /// <summary>Erlan's own spoken line ids (e.g. "wounded_ru" = «Брат, я ранен!») are words, not background screams.</summary>
        static readonly Regex ErlanLine = new Regex(@"^[a-z_]+_(ru|kz)(_\d+)?$");

        static string[] Offenders(IEnumerable<string> names, bool skipErlanLines) =>
            names.Where(n => Banned.IsMatch(n) && !(skipErlanLines && ErlanLine.IsMatch(n))).ToArray();

        // ------------------------------------------------------------------ static checks
        [Test]
        public void Policy_FlagsVoices_KeepsBulletsAndExplosions()
        {
            foreach (var n in new[] { "385895_battlefield_ambience_loop", "179325_male_scream_7", "381942_man_screaming_i",
                         "695386_male_death_cry_3", "486027_r12_31_artillery_guns_firing", "soldiers_shouting", "crowd_yell", "medic_call" })
                Assert.IsTrue(BackgroundAudioPolicy.IsVoiceLike(n), n);
            foreach (var n in new[] { "675609_s16_31_bombardment_with_distant", "520935_ak_47_burst", "108640_distant_explosion",
                         "250086_phantom_quadcopter_hovers", "773075_wind_in_corn_field_on_20m_0305_p", "715682_130819_night_insects_crickets_co",
                         "165394_heavy_machine_gun", "241840_incoming_mortar_1", "365956_ear_ringing_after_explosion" })
                Assert.IsFalse(BackgroundAudioPolicy.IsVoiceLike(n), n);
        }

#if UNITY_EDITOR
        /// <summary>Every SFX folder used by background layers contains only non-voice clips (the voice folders -
        /// Erlan's pain pools and the battle_bed loop - are excluded or filtered).</summary>
        [Test]
        public void BackgroundSfxFolders_HaveNoVoiceClips()
        {
            var erlanOnly = new[] { "pain_groan", "pain_scream", "breath_heavy", "gasp_choke", "cough", "battle_bed" };
            var bad = new List<string>();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/SFX" }))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
                if (erlanOnly.Contains(folder)) continue;
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (BackgroundAudioPolicy.IsVoiceLike(name) || Banned.IsMatch(name)) bad.Add(path);
            }
            // artillery_rumble/486027 (gun crew commands) is filtered by the policy at runtime - allowed in the folder
            bad.RemoveAll(p => p.Contains("486027"));
            Assert.IsEmpty(bad, "voice-like background clips: " + string.Join(", ", bad));
        }
#endif

        [Test]
        public void ErlanLines_AllRecorded_IncludingTheAnswers()
        {
            foreach (var id in new[] { "hit_leg", "wounded", "hurts", "take_off", "cold", "hear_you", "where_hurts_leg", "where_hurts_chest", "yes", "look_at_me", "breathe", "hold_on" })
            {
                var c = VoiceOver.ErlanClip(id);
                Assert.IsNotNull(c, "no Erlan recording " + id);
                Assert.AreEqual(1, c.channels, id + " must be mono");
                Assert.Less(c.length, 6f, id);
            }
            Assert.IsNull(VoiceOver.ErlanClip("no_such_line"));
        }

        [Test]
        public void ErlanSpeaks_RealRecording_InBothLanguages_SubtitlePerLanguage()
        {
            var was = GameSettings.Language;
            var go = new GameObject("voice");
            try
            {
                var v = go.AddComponent<CasualtyVoice>();
                v.bank = ScriptableObject.CreateInstance<VoiceBank>();
                v.voice = CasualtyVoice.MakeSource(go, false);
                GameSettings.Language = GameLanguage.KZ;
                Assert.IsTrue(v.Say("hear_you", true));
                Assert.AreEqual("hear_you_ru", v.voice.clip.name);
                StringAssert.Contains("Ест", v.Subtitle);
                GameSettings.Language = GameLanguage.RU;
                Assert.IsTrue(v.Say("yes", true));
                Assert.AreEqual("yes_ru", v.voice.clip.name);
                Assert.AreEqual("Да...", v.Subtitle);
            }
            finally { GameSettings.Language = was; Object.DestroyImmediate(go); }
        }

        [Test]
        public void PainSounds_NeverTheScreamPool_AndQuiet()
        {
            var go = new GameObject("voice");
            try
            {
                var v = go.AddComponent<CasualtyVoice>();
                v.bank = ScriptableObject.CreateInstance<VoiceBank>();
                var scream = AudioClip.Create("male_scream_test", 100, 1, 22050, false);
                var groan = AudioClip.Create("groan_test", 100, 1, 22050, false);
                v.bank.scream = new[] { scream };
                v.bank.groan = new[] { groan };
                v.voice = CasualtyVoice.MakeSource(go, false);
                for (int i = 0; i < 5; i++)
                {
                    Assert.IsTrue(v.Vocalise(v.bank.scream, 1f, true));
                    Assert.AreEqual("groan_test", v.LastLine);
                    Assert.LessOrEqual(v.voice.volume, 0.6f);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ------------------------------------------------------------------ battlefield soak
        [UnityTest]
        public IEnumerator Battlefield_25s_PlaysOnlyBulletsExplosionsDrones()
        {
            var guard = new GlobalStateGuard();
            try
            {
                ClipLog.Clear();
                BattleDirector.Mode = BattleMode.Combat;
#if UNITY_EDITOR
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BattlefieldLayout.ScenePath,
                    new LoadSceneParameters(LoadSceneMode.Single));
#endif
                yield return null;
                yield return null;
                AudioListener.volume = 0f;
                var ba = BattleAudio.Instance;
                Assert.IsNotNull(ba, "BattleAudio missing in the Battlefield scene");
                Assert.IsFalse((ba.battleBed ?? new AudioClip[0]).Any(c => c != null && BackgroundAudioPolicy.IsVoiceLike(c.name)), "battle bed still has a voice clip");
                Assert.IsFalse((ba.artilleryBed ?? new AudioClip[0]).Any(c => c != null && BackgroundAudioPolicy.IsVoiceLike(c.name)), "artillery bed still has a voice clip");
                foreach (var s in ba.GetComponentsInChildren<AudioSource>())
                    if (s.isPlaying && s.clip != null) Assert.IsFalse(BackgroundAudioPolicy.IsVoiceLike(s.clip.name), "playing voice: " + s.clip.name);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 25f) yield return null;
                Assert.Greater(ClipLog.Background.Count, 0, "nothing played - the hook is not wired");
                var bad = Offenders(ClipLog.Background, false);
                Assert.IsEmpty(bad, "background voices: " + string.Join(", ", bad));
                foreach (var src in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                    if (src.isPlaying && src.clip != null && src.GetComponentInParent<CasualtyVoice>() == null)
                        Assert.IsFalse(Banned.IsMatch(src.clip.name), "audible: " + src.clip.name);
            }
            finally { guard.Restore(); }
        }

        // ------------------------------------------------------------------ S1 with Erlan's voice on
        [UnityTest]
        public IEnumerator S1_Erlan_NeverPlaysScreams()
        {
            yield return FreshScene.Ensure();
            ClipLog.Clear();
            bool tMuted = TreatmentAudio.Muted, uMuted = UiAudio.Muted;
            float vol = AudioListener.volume;
            TreatmentAudio.Muted = true;
            UiAudio.Muted = true;
            var spawned = new List<Object>();
            try
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.transform.localScale = new Vector3(30f, 1f, 30f);
                spawned.Add(floor);
                var ear = new GameObject("Listener");
                ear.AddComponent<AudioListener>();
                spawned.Add(ear);
                AudioListener.volume = 0f;
#if UNITY_EDITOR
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Erlan.prefab");
#else
                GameObject prefab = null;
#endif
                Assert.That(prefab, Is.Not.Null);
                var erlan = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
                spawned.Add(erlan);
                var c = erlan.GetComponent<CasualtyController>();
                c.voice.muted = false;
                var dgo = new GameObject("Director");
                spawned.Add(dgo);
                var d = dgo.AddComponent<ScenarioDirector>();
                d.autoStart = false;
                d.testMode = true;
                d.casualty = c;
                var anchor = new GameObject("Anchor").transform;
                anchor.SetParent(dgo.transform, false);
                d.spots = System.Enum.GetValues(typeof(CasualtySpot)).Cast<CasualtySpot>()
                    .Select(s => new ScenarioDirector.SpotAnchor { spot = s, casualty = anchor }).ToArray();
                yield return null;
                d.Begin(ScenarioCatalog.Get("S1"), default);
                float t0 = Time.time;
                while (Time.time - t0 < 15f)
                {
                    // pain spikes as when handled - the old code answered with the scream recordings here
                    if (c.IsDown && c.voice != null && Random.value < 0.02f) c.voice.OnPainSpike(c, Random.value < 0.5f);
                    yield return null;
                }
                Assert.Greater(ClipLog.Played.Count, 0, "Erlan was silent - hook not wired");
                Assert.IsFalse(ClipLog.Played.Any(n => n.Contains("scream") || n.Contains("death_cry")), "scream recording played: " + string.Join(", ", ClipLog.Played));
                var bad = Offenders(ClipLog.Played, true);
                Assert.IsEmpty(bad, "voices: " + string.Join(", ", bad));
                if (c.Patient.AVPU <= Consciousness.Voice)
                {
                    var id = CasualtyVoice.WhereHurtsLine(c);
                    Assert.AreEqual("where_hurts_leg", id, "S1 is a leg wound");
                    Assert.IsTrue(c.voice.AnswerWhereHurts(c));
                    Assert.AreEqual(id + "_ru", c.voice.voice.clip.name);
                }
            }
            finally
            {
                foreach (var o in spawned) if (o != null) Object.Destroy(o);
                TreatmentAudio.Muted = tMuted;
                UiAudio.Muted = uMuted;
                AudioListener.volume = vol;
            }
        }
    }
}
