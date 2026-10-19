using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w9: bot tests that build their own little world (floor, Erlan, hands, kit) must not share the play session with
    /// a battlefield loaded by an earlier test (second Erlan, director, two audio listeners, the player's own kit) — the
    /// "order-dependent bot flakes" of w3/w7. Starts every such test in a new empty scene and unloads the rest.
    /// </summary>
    public static class FreshScene
    {
        static int _n;

        public static IEnumerator Ensure()
        {
            var fresh = SceneManager.CreateScene("~BotTest_" + (++_n));
            SceneManager.SetActiveScene(fresh);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = SceneManager.GetSceneAt(i);
                // never the test runner's own scene (unloading it stops the run)
                if (s != fresh && s.isLoaded && !s.name.StartsWith("InitTestScene")) yield return SceneManager.UnloadSceneAsync(s);
            }
            yield return null;
        }
    }
}
