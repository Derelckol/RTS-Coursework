using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rts
{
    public static class MatchSession
    {
        public const string MenuScene = "RtsMainMenu";
        public const string MatchScene = "RtsMatchPrototype";
        public static DifficultyLevel Selected { get; private set; }
        internal static void SelectSaved(DifficultyLevel level) => Selected = level;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSelection() => Selected = DifficultyLevel.Easy;

        public static DifficultyProfile Profile(DifficultyLevel level)
        {
            DifficultyProfile profile = Resources.Load<DifficultyProfile>($"MatchDifficulty/{level}");
            if (profile == null)
            {
                throw new System.InvalidOperationException($"Missing difficulty profile {level}.");
            }

            return profile;
        }

        public static void StartNewMatch(DifficultyLevel level)
        {
            Selected = level;
            Load(MatchScene);
        }

        public static void ReplayMatch() => Load(MatchScene);

        public static void ReturnToMenu()
        {
            ResetSelection();
            Load(MenuScene);
        }

        private static void Load(string scene)
        {
            if (Application.CanStreamedLevelBeLoaded(scene))
            {
                SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
                return;
            }

#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                $"Assets/Scenes/{scene}.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
            throw new System.InvalidOperationException($"Scene {scene} is missing from this build.");
#endif
        }
    }
}
