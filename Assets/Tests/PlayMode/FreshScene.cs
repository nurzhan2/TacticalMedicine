using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace FieldMedic.Tests
{
    /// <summary>
    /// Tests that build their own little world (bot tests) start in an empty scene: a Battlefield left loaded by
    /// an earlier test brings its rig camera (standing head → drone hits), its hands and its TACEVAC loader along.
    /// </summary>
    public static class FreshScene
    {
        static int _n;

        public static IEnumerator Make()
        {
            var fresh = SceneManager.CreateScene("TestScene_" + (++_n));
            SceneManager.SetActiveScene(fresh);
            var old = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s != fresh && s.isLoaded) old.Add(s);
            }
            foreach (var s in old)
            {
                var op = SceneManager.UnloadSceneAsync(s);
                if (op != null) while (!op.isDone) yield return null;
            }
            yield return null;
        }
    }
}
