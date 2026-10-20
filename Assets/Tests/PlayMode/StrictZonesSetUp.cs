using NUnit.Framework;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w10b: the V1-W10 bot/regression tests were written for the realistic (exam) zone sizes. Outside the exam the
    /// game now uses big forgiving zones (<see cref="FieldMedic.Interaction.Forgiving"/>); the suite keeps checking
    /// the strict logic by default, tests of the forgiving path set Forgiving.Override = false themselves.
    /// </summary>
    [SetUpFixture]
    public sealed class StrictZonesSetUp
    {
        [OneTimeSetUp] public void Strict() => FieldMedic.Interaction.Forgiving.Override = true;
        [OneTimeTearDown] public void Reset() => FieldMedic.Interaction.Forgiving.Override = null;
    }
}
