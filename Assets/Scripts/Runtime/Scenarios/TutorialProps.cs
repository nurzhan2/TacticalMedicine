using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>Real models for the practice dummy (w8 tutorial): Resources/TutorialProps, filled by W8Builder.</summary>
    [CreateAssetMenu(menuName = "FieldMedic/Tutorial Props")]
    public sealed class TutorialProps : ScriptableObject
    {
        [Tooltip("Long wooden rifle crate with rope handles at both ends (two-hand drag).")]
        public GameObject crate;
        public float crateScale = 0.75f;
        [Tooltip("Metal ammo can with a top handle (grab + lift).")]
        public GameObject ammoCan;
        public float ammoCanScale = 1f;

        public static TutorialProps Load() => Resources.Load<TutorialProps>("TutorialProps");
    }
}
