using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rts.Editor
{
    public static class SavePersistenceChecks
    {
        [MenuItem("RTS/Verify JSON persistence")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RtsMatchPrototype.unity");
            foreach (var actor in UnityEngine.Object.FindObjectsByType<CombatTarget>(FindObjectsSortMode.None)) actor.Initialize();
            foreach (var mine in UnityEngine.Object.FindObjectsByType<GoldDeposit>(FindObjectsSortMode.None)) mine.Initialize();
            var controls = UnityEngine.Object.FindFirstObjectByType<PrototypeControls>();
            typeof(PrototypeControls).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controls, null);
            controls.GetComponent<PrototypeMatch>().StartMatch();
            var snapshot = controls.CaptureSave();
            SaveStore.Validate(snapshot);
            string path = null;
            try
            {
                path = SaveStore.Write(snapshot, "JSON verification " + Guid.NewGuid().ToString("N"));
                var read = SaveStore.Read(path);
                Require(read.actors.Select(a => a.id).SequenceEqual(snapshot.actors.Select(a => a.id)), "Stable IDs changed in JSON.");
                string original = File.ReadAllText(path);
                Reject(() => SaveStore.Write(snapshot, "\n", path));
                Require(File.ReadAllText(path) == original, "Failed write modified prior save.");
                SaveStore.Write(snapshot, snapshot.name, path);
                Require(File.Exists(path + ".bak"), "Overwrite did not retain backup.");
                read.version++;
                Reject(() => SaveStore.Validate(read));
                read = SaveStore.Read(path); read.actors[1].id = read.actors[0].id;
                Reject(() => SaveStore.Validate(read));
                read = SaveStore.Read(path); read.actors.First(a => a.order != null).order.target = "missing-target";
                Reject(() => SaveStore.Validate(read));
                read = SaveStore.Read(path); read.economies[0].reserved++;
                Reject(() => SaveStore.Validate(read));
                read = SaveStore.Read(path); read.knowledge[0].explored = new int[3];
                Reject(() => SaveStore.Validate(read));
                File.WriteAllText(path, "{broken JSON");
                Reject(() => SaveStore.Read(path));
                Debug.Log("JSON persistence checks passed: round trip, IDs, safe overwrite/backup, invalid name, version, links, population, fog grid and corrupt JSON.");
            }
            finally
            {
                if (path != null) SaveStore.Delete(path);
                EditorSceneManager.OpenScene("Assets/Scenes/RtsMainMenu.unity");
            }
        }
        private static void Require(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
        private static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (Exception) { rejected = true; }
            Require(rejected, "Invalid save was accepted.");
        }
    }
}
