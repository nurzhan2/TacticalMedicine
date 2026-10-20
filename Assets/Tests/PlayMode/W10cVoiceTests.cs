using System.IO;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Medical;
using FieldMedic.UI;
using NUnit.Framework;
using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w10c: the real ElevenLabs voice-over (Russian only). Every radio/instructor line id has its Russian recording,
    /// the old robotic KZ placeholders are gone, the KZ UI plays the same Russian recording, the casualty never
    /// speaks with the TTS placeholder words, the credits name ElevenLabs.
    /// </summary>
    public class W10cVoiceTests
    {
        static string[] LineIds()
        {
            var path = Path.Combine(Application.dataPath, "..", "tools", "radio_lines.tsv");
            return File.ReadAllLines(path).Skip(1).Where(l => l.Trim().Length > 0).Select(l => l.Split('\t')[0]).ToArray();
        }

        [Test]
        public void EveryLine_HasItsRussianRecording_AndNoKazakhPlaceholder()
        {
            var ids = LineIds();
            Assert.GreaterOrEqual(ids.Length, 69);
            foreach (var id in ids)
            {
                Assert.IsNotNull(VoiceOver.Clip(id), "no Russian recording for " + id);
                Assert.IsNull(Resources.Load<AudioClip>("Radio/" + id + "_kz"), "robotic KZ placeholder still in the build: " + id);
            }
        }

        [Test]
        public void KazakhUi_PlaysTheRussianRecording()
        {
            var was = GameSettings.Language;
            try
            {
                GameSettings.Language = GameLanguage.KZ;
                Assert.IsNotNull(VoiceOver.Clip("air"));
                Assert.AreEqual("air_ru", VoiceOver.Clip("air").name);
            }
            finally { GameSettings.Language = was; }
        }

        [Test]
        public void LessonVoices_AllExist()
        {
            foreach (var l in TrainingCatalog.Defaults())
                foreach (var s in l.steps)
                    if (!string.IsNullOrEmpty(s.voice)) Assert.IsNotNull(VoiceOver.Clip(s.voice), $"lesson {l.id} step {s.id}: voice {s.voice} missing");
        }

        [Test]
        public void Casualty_NeverSpeaksTtsWords()
        {
            Assert.IsFalse(VoiceOver.CasualtyTtsWords);
            var go = new GameObject("voice");
            try
            {
                var v = go.AddComponent<FieldMedic.Casualty.CasualtyVoice>();
                v.bank = ScriptableObject.CreateInstance<VoiceBank>();
                var tts = AudioClip.Create("hit_leg_ru", 100, 1, 22050, false);
                var groan = AudioClip.Create("groan_real", 100, 1, 22050, false);
                v.bank.lines = new[] { new VoiceBank.Line { id = "hit_leg", ru = new[] { tts }, kz = new[] { tts } } };
                v.bank.scream = new[] { groan };
                v.bank.groan = new[] { groan };
                v.voice = FieldMedic.Casualty.CasualtyVoice.MakeSource(go, false);
                Assert.IsTrue(v.Say("hit_leg", true));
                Assert.AreEqual("groan_real", v.LastLine);
                Assert.AreNotEqual(tts, v.voice.clip);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Credits_NameElevenLabs()
        {
            StringAssert.Contains("ElevenLabs", MenuBoard.Credits);
        }
    }
}
