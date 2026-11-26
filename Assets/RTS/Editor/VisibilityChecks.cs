using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rts.Editor
{
    public static class VisibilityChecks
    {
        private const string ScenePath = "Assets/Scenes/RtsMatchPrototype.unity";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void BuildAndVerify()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var controls = UnityEngine.Object.FindFirstObjectByType<PrototypeControls>();
            var fog = controls.GetComponent<MatchVisibility>();
            if (fog == null)
            {
                fog = controls.gameObject.AddComponent<MatchVisibility>();
                var director = new SerializedObject(controls.GetComponent<OpponentDirector>());
                director.FindProperty("visibilitySource").objectReferenceValue = fog;
                director.ApplyModifiedPropertiesWithoutUndo();
                foreach (GoldDeposit mine in UnityEngine.Object.FindObjectsByType<GoldDeposit>(FindObjectsSortMode.None))
                    if (mine.name.Contains("Reserve"))
                        mine.transform.position = new Vector3(mine.transform.position.x < 0 ? -8.5f : 8.5f, 0.65f, 20.5f);
                EditorSceneManager.SaveScene(controls.gameObject.scene);
            }
            const string fogPath = "Assets/RTS/PrototypeMaterials/FogOfWar.mat";
            Material fogMaterial = AssetDatabase.LoadAssetAtPath<Material>(fogPath);
            if (fogMaterial == null)
            {
                fogMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                fogMaterial.SetFloat("_Surface", 1);
                fogMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                fogMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                fogMaterial.SetFloat("_ZWrite", 0);
                fogMaterial.SetFloat("_Cull", 0);
                fogMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                fogMaterial.renderQueue = 3100;
                AssetDatabase.CreateAsset(fogMaterial, fogPath);
            }
            var visibilitySettings = new SerializedObject(fog);
            visibilitySettings.FindProperty("fogTemplate").objectReferenceValue = fogMaterial;
            visibilitySettings.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(controls.gameObject.scene);
            AssetDatabase.SaveAssets();
            var actors = UnityEngine.Object.FindObjectsByType<CombatTarget>(FindObjectsSortMode.None);
            foreach (CombatTarget actor in actors) actor.Initialize();
            var mines = UnityEngine.Object.FindObjectsByType<GoldDeposit>(FindObjectsSortMode.None);
            foreach (GoldDeposit mine in mines) mine.Initialize();
            Invoke(controls, "Awake");
            var match = controls.GetComponent<PrototypeMatch>();
            var hud = controls.GetComponent<PrototypeInterface>();
            var directorAI = controls.GetComponent<OpponentDirector>();
            var orders = Read<UnitOrders>(controls, "orders");
            var roster = Read<List<CombatTarget>>(controls, "activeTargets");
            var obstacles = Read<List<Collider>>(controls, "activeObstacles");
            CombatTarget enemy = actors.Single(actor => actor.Side == Side.Opponent &&
                actor.Unit != null && actor.Unit.Kind == UnitKind.Infantry);
            CombatTarget blue = actors.Single(actor => actor.Side == Side.Player &&
                actor.Unit != null && actor.Unit.Kind == UnitKind.Infantry);
            var start = mines.Single(mine => mine.name == "Player Gold");
            var extra = mines.Single(mine => mine.name == "Player Reserve Gold");
            Require(fog.IsKnown(Side.Player, start) && !fog.IsKnown(Side.Player, extra) &&
                !fog.IsVisible(Side.Player, enemy) && !fog.IsVisible(Side.Opponent, blue) &&
                !fog.TryKnownBase(Side.Opponent, out _), "Starting knowledge leaked.");
            var markers = new List<MapMarker>();
            hud.CollectMarkers(new Rect(0, 0, 240, 180), obstacles, roster, markers);
            Require(!markers.Any(marker => marker.Kind == MapMarkerKind.EnemyUnit ||
                marker.Kind == MapMarkerKind.EnemyBuilding) &&
                markers.Count(marker => marker.Kind == MapMarkerKind.Gold) == 1, "Hidden minimap markers leaked.");
            Vector3 screen = Camera.main.WorldToScreenPoint(enemy.transform.position);
            // Explicit hidden attack cannot reveal a target.
            orders.Interact(new[] { blue.Unit }, enemy);
            Require(orders.StateOf(blue.Unit) == UnitState.Idle, "A hidden enemy accepted an attack order.");

            var tick = typeof(PrototypeControls).GetMethod("AdvanceSimulation", Private);
            object[] step = { 0.02f };
            void Tick(int count) { for (int i = 0; i < count; i++) tick.Invoke(controls, step); }
            Tick(3000);
            Require(match.ElapsedSeconds == 0f && Read<float>(directorAI, "elapsed") == 0f, "Intro ran time.");
            match.StartMatch();
            Tick(10);
            hud.SetPaused(true);
            float paused = match.ElapsedSeconds;
            Tick(3000);
            Require(match.ElapsedSeconds == paused, "Pause ran time.");
            hud.SetPaused(false);
            Require(PrototypeMatch.FormatTime(65) == "1:05" && PrototypeMatch.FormatTime(3665) == "1:01:05",
                "Time formatting failed.");

            Vector3 original = blue.transform.position;
            blue.transform.position = new Vector3(8.5f, 0.85f, 20.5f);
            fog.Refresh();
            Require(fog.IsKnown(Side.Player, mines.Single(mine => mine.name == "Opponent Reserve Gold")) &&
                !fog.IsKnown(Side.Opponent, extra), "Knowledge was shared between sides.");
            blue.transform.position = enemy.transform.position + Vector3.left * 4f;
            fog.Refresh();
            Require(fog.IsVisible(Side.Player, enemy), "Approaching did not reveal enemy.");
            orders.Interact(new[] { blue.Unit }, enemy);
            Vector3 last = enemy.transform.position;
            blue.transform.position = original;
            enemy.transform.position += Vector3.forward * 12f;
            fog.Refresh();
            Tick(1);
            Require(!orders.IsAttacking(blue.Unit, enemy) && blue.Unit.Destination != enemy.transform.position,
                "Hidden movement was tracked.");
            Require(fog.State(Side.Player, last) == VisibilityState.Explored, "Explored state was lost.");
            var hall = actors.Single(actor => actor.Side == Side.Opponent && actor.Building == BuildingKind.TownHall);
            blue.transform.position = hall.transform.position + Vector3.left * 5;
            fog.Refresh();
            Require(fog.CaptureBuildings(Side.Player).Any(m => m.type == BuildingKind.TownHall), "Hall not remembered.");
            blue.transform.position = original;
            fog.Refresh();
            var remembered = fog.CaptureBuildings(Side.Player);
            Require(!fog.CaptureBuildings(Side.Opponent).Any(), "Building knowledge shared.");
            hall.Damage(hall.MaxHealth);
            fog.Refresh();
            Require(fog.CaptureBuildings(Side.Player).Length == remembered.Length, "Hidden destruction leaked.");
            fog.RestoreBuildings(Side.Player, remembered);
            blue.transform.position = hall.transform.position + Vector3.left * 5;
            fog.Refresh();
            Require(!fog.CaptureBuildings(Side.Player).Any(m => m.type == BuildingKind.TownHall), "Empty site retained stale memory.");
            blue.transform.position = original;
            var cameraState = controls.CaptureCamera();
            foreach (float size in new[] { 6f, 16f })
            {
                controls.RestoreCamera(new CameraViewState { center = new Vector3(999, 0, 999), size = size });
                var view = controls.CaptureCamera();
                Require(view.size == size && view.center.x < 40 && view.center.z < 30, "Zoom clamp failed.");
                Rect footprint = hud.CameraFootprint(new Rect(0, 0, 240, 180), Camera.main);
                Require(footprint.width > 0 && footprint.height > 0, "Minimap footprint lost at zoom limit.");
            }
            controls.RestoreCamera(cameraState);
            Debug.Log("Building memory checks passed: value snapshot, separate sides, hidden destruction, revisiting, camera bounds and snapshots.");
            Debug.Log("Visibility and timer checks passed: independent knowledge, minimap, hidden orders, three states, intro and pause.");

            EditorSceneManager.OpenScene(ScenePath);
            controls = UnityEngine.Object.FindFirstObjectByType<PrototypeControls>();
            foreach (CombatTarget actor in UnityEngine.Object.FindObjectsByType<CombatTarget>(FindObjectsSortMode.None)) actor.Initialize();
            foreach (GoldDeposit mine in UnityEngine.Object.FindObjectsByType<GoldDeposit>(FindObjectsSortMode.None)) mine.Initialize();
            Invoke(controls, "Awake");
            fog = controls.GetComponent<MatchVisibility>();
            match = controls.GetComponent<PrototypeMatch>();
            directorAI = controls.GetComponent<OpponentDirector>();
            match.StartMatch();
            tick = typeof(PrototypeControls).GetMethod("AdvanceSimulation", Private);
            var opponentMines = UnityEngine.Object.FindObjectsByType<GoldDeposit>(FindObjectsSortMode.None);
            var reserve = opponentMines.Single(mine => mine.name == "Opponent Reserve Gold");
            // Force depletion only in this independent test to exercise resource exploration.
            opponentMines.Single(mine => mine.name == "Opponent Gold").Take(int.MaxValue);
            opponentMines.Single(mine => mine.name == "Player Gold").Take(int.MaxValue);
            for (int i = 0; i < 110; i++) tick.Invoke(controls, step);
            var scoutAtStart = Read<PrototypeUnit>(directorAI, "scout");
            Require(scoutAtStart != null, "No scout was assigned.");
            var scoutActor = scoutAtStart.GetComponent<CombatTarget>();
            scoutActor.Damage(scoutActor.MaxHealth);
            for (int i = 0; i < 16000 && !fog.IsKnown(Side.Opponent, reserve); i++) tick.Invoke(controls, step);
            Require(fog.IsKnown(Side.Opponent, reserve),
                $"Scout failed to discover extra gold. scout={Read<PrototypeUnit>(directorAI, "scout")?.transform.position} " +
                $"goal={Read<Vector3>(directorAI, "scoutGoal")} mines=" +
                string.Join(",", opponentMines.Select(mine => $"{mine.name}:{fog.KnownStock(Side.Opponent, mine)}/{mine.Stock}")) +
                directorAI.DebugRoster);
            Debug.Log($"Scout discovered extra gold at {match.ElapsedSeconds:F2}s.");
            for (int i = 0; i < 14000 && !fog.TryKnownBase(Side.Opponent, out _); i++) tick.Invoke(controls, step);
            Require(fog.TryKnownBase(Side.Opponent, out _), "Scout failed to discover player base.");
            Require(directorAI.RaidsLaunched == 0 || directorAI.LastRaidAt >= directorAI.Profile.FirstRaidDelay,
                "Exploration bypassed raid delay.");
            for (int i = 0; i < 2000 && fog.KnownStock(Side.Opponent, reserve) == reserve.Stock; i++)
                tick.Invoke(controls, step);
            for (int i = 0; i < 3000 && reserve.Stock == 1400; i++) tick.Invoke(controls, step);
            Require(reserve.Stock < 1400, "Workers did not switch to discovered gold.");
            Debug.Log($"Scout discovered base by {match.ElapsedSeconds:F2}s; first raid {directorAI.LastRaidAt:F2}s.");
        }

        private static T Read<T>(object source, string field) =>
            (T)source.GetType().GetField(field, Private).GetValue(source);
        private static void Invoke(object source, string method) => source.GetType().GetMethod(method, Private).Invoke(source, null);
        private static void Require(bool ok, string reason)
        {
            if (!ok) throw new InvalidOperationException(reason);
        }
    }
}
