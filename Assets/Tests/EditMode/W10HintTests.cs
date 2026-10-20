using System.Linq;
using FieldMedic.Medical;
using NUnit.Framework;

namespace FieldMedic.Medical.Tests
{
    /// <summary>
    /// w10 (owner: "I grab the way the hint shows and it does not take"): no hint may describe a gesture or a place
    /// the game does not accept any more. The kit is summoned with X (no "quick pouch on the belt"), the radio is
    /// "hold Y" (no "button on the left shoulder"), grabbing is GRIP or TRIGGER (no "make a fist").
    /// </summary>
    public class W10HintTests
    {
        static readonly string[] ForbiddenRu = { "на левом плече", "рация на левом плече", "кнопку пальцем", "кулак (хват)", "подсумок справа", "быстрый подсумок", "из подсумка" };
        static readonly string[] ForbiddenKz = { "сол иықтағы рация", "жұдырықты қыс", "жедел қалта", "подсумка" };

        [Test]
        public void NoHint_DescribesAnOldGesture()
        {
            foreach (var l in TrainingCatalog.Defaults())
                foreach (var s in l.steps)
                    foreach (var text in new[] { s.ru, s.kz }.Concat(s.actions.SelectMany(a => new[] { a.ru, a.kz })))
                    {
                        if (string.IsNullOrEmpty(text)) continue;
                        string low = text.ToLowerInvariant();
                        foreach (var f in ForbiddenRu.Concat(ForbiddenKz))
                            Assert.IsFalse(low.Contains(f), $"lesson {l.id} step {s.id}: \"{text}\" still says \"{f}\"");
                    }
        }

        [Test]
        public void RadioSteps_SayHoldY_InBothLanguages()
        {
            foreach (var l in TrainingCatalog.Defaults())
                foreach (var s in l.steps.Where(s => s.item == "Radio"))
                {
                    Assert.IsTrue(s.actions.Any(a => a.ru.Contains("Зажми Y")), $"{l.id}/{s.id}: RU names «Зажми Y»");
                    Assert.IsTrue(s.actions.Any(a => a.kz.Contains("Y")), $"{l.id}/{s.id}: KZ names Y");
                }
        }

        [Test]
        public void GrabSteps_FromTheKit_NameX()
        {
            foreach (var l in TrainingCatalog.Defaults().Where(l => !l.selfAid))
                foreach (var s in l.steps)
                {
                    var grab = s.actions.FirstOrDefault(a => a.check != null && a.check.EndsWith("_grab") && a.check != "drag_grab");
                    if (grab == null) continue;
                    Assert.IsTrue(s.actions.Any(a => a.ru.Contains("X")), $"{l.id}/{s.id}: taking an item from the kit says how to open it (X)");
                }
        }
    }
}
