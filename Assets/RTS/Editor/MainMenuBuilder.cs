using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rts.Editor
{
    public static class MainMenuBuilder
    {
        private const string Folder = "Assets/RTS/Resources/MatchDifficulty";
        private const string MenuPath = "Assets/Scenes/RtsMainMenu.unity";

        [MenuItem("RTS/Open or Create Main Menu Scene")]
        public static void OpenOrCreate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureMenu();
            EditorSceneManager.OpenScene(MenuPath);
        }

        public static void BuildAndVerify()
        {
            EnsureMenu();
            PrototypeSceneBuilder.BuildAndVerifyMatch();
            EditorSceneManager.OpenScene(MenuPath);
            if (UnityEngine.Object.FindFirstObjectByType<PrototypeMainMenu>() == null || Camera.main == null)
            {
                throw new InvalidOperationException("The menu requires its UI controller and camera.");
            }
            Debug.Log("RTS main menu verification passed.");
        }

        private static void EnsureMenu()
        {
            EnsureProfiles();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuPath) != null)
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Main Menu").AddComponent<PrototypeMainMenu>();
            var cameraObject = new GameObject("Menu Camera") { tag = "MainCamera" };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.09f, 0.12f);

            if (!EditorSceneManager.SaveScene(scene, MenuPath))
            {
                throw new InvalidOperationException($"Could not save {MenuPath}.");
            }
            AssetDatabase.SaveAssets();
        }

        internal static void EnsureProfiles()
        {
            if (!AssetDatabase.IsValidFolder("Assets/RTS/Resources"))
            {
                AssetDatabase.CreateFolder("Assets/RTS", "Resources");
            }
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/RTS/Resources", "MatchDifficulty");
            }

            Create(DifficultyLevel.Easy, "Легкий", "Більше часу на освоєння. Перша хвиля з двох бійців приблизно через 3:30.",
                210, 150, 2, 5, 3, 1.2f, 1, 1, 1, 1, 2, 12,
                UnitKind.Infantry, UnitKind.Archer, UnitKind.Infantry, UnitKind.Cavalry);
            Create(DifficultyLevel.Medium, "Середній", "База ШІ розвивається швидше. Перший змішаний загін очікується близько 2:30.",
                150, 105, 3, 7, 4, 0.9f, 2, 2, 3, 1, 3, 8, UnitKind.Infantry, UnitKind.Archer, UnitKind.Cavalry,
                UnitKind.Archer, UnitKind.Infantry);
            Create(DifficultyLevel.Hard, "Важкий", "Ранній тиск і більші загони з вершниками. Перший напад очікується близько 1:45.",
                105, 80, 4, 9, 5, 0.65f, 2, 3, 5, 2, 4, 4, UnitKind.Cavalry, UnitKind.Infantry, UnitKind.Archer,
                UnitKind.Cavalry, UnitKind.Archer, UnitKind.Infantry);
        }

        private static void Create(DifficultyLevel level, string title, string description, float delay, float interval,
            int firstSize, int cap, int workers, float decisionInterval, int capacityBuffer, int garrison,
            int reserve, int barracksLimit, int queueLimit, float raidWait, params UnitKind[] composition)
        {
            string path = $"{Folder}/{level}.asset";
            if (AssetDatabase.LoadAssetAtPath<DifficultyProfile>(path) != null)
            {
                return;
            }

            var profile = ScriptableObject.CreateInstance<DifficultyProfile>();
            var settings = new SerializedObject(profile);
            settings.FindProperty("title").stringValue = title;
            settings.FindProperty("description").stringValue = description;
            settings.FindProperty("firstRaidDelay").floatValue = delay;
            settings.FindProperty("raidInterval").floatValue = interval;
            settings.FindProperty("firstSquadSize").intValue = firstSize;
            settings.FindProperty("squadGrowth").intValue = 1;
            settings.FindProperty("maxSquadSize").intValue = cap;
            settings.FindProperty("desiredWorkers").intValue = workers;
            settings.FindProperty("decisionInterval").floatValue = decisionInterval;
            settings.FindProperty("capacityBuffer").intValue = capacityBuffer;
            settings.FindProperty("garrisonSize").intValue = garrison;
            settings.FindProperty("reserveSoldiers").intValue = reserve;
            settings.FindProperty("barracksLimit").intValue = barracksLimit;
            settings.FindProperty("militaryQueueLimit").intValue = queueLimit;
            settings.FindProperty("raidWait").floatValue = raidWait;
            SerializedProperty kinds = settings.FindProperty("squadComposition");
            kinds.arraySize = composition.Length;
            for (int index = 0; index < composition.Length; index++)
            {
                kinds.GetArrayElementAtIndex(index).enumValueIndex = (int)composition[index];
            }

            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(profile, path);
        }
    }
}
