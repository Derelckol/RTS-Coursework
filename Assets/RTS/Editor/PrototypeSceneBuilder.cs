using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Rts.Editor
{
    public static class PrototypeSceneBuilder
    {
        internal static readonly RectInt MatchMap = new RectInt(-40, -30, 80, 60);
        private const string ScenePath = "Assets/Scenes/RtsPrototype.unity";
        private const string CombatScenePath = "Assets/Scenes/RtsCombatPrototype.unity";
        private const string EconomyScenePath = "Assets/Scenes/RtsEconomyPrototype.unity";
        private const string ConstructionScenePath = "Assets/Scenes/RtsConstructionPrototype.unity";
        private const string InterfaceScenePath = "Assets/Scenes/RtsInterfacePrototype.unity";
        private const string MatchScenePath = "Assets/Scenes/RtsMatchPrototype.unity";
        private const string MaterialFolder = "Assets/RTS/PrototypeMaterials";
        private const string PrefabFolder = "Assets/RTS/EconomyPrefabs";

        [MenuItem("RTS/Open or Create Prototype Scene")]
        public static void OpenOrCreate()
        {
            OpenOrCreateScene(ScenePath);
        }

        [MenuItem("RTS/Open or Create Combat Prototype Scene")]
        public static void OpenOrCreateCombat()
        {
            OpenOrCreateScene(CombatScenePath);
        }

        [MenuItem("RTS/Open or Create Economy Prototype Scene")]
        public static void OpenOrCreateEconomy()
        {
            OpenOrCreateScene(EconomyScenePath);
        }

        [MenuItem("RTS/Open or Create Construction Prototype Scene")]
        public static void OpenOrCreateConstruction()
        {
            OpenOrCreateScene(ConstructionScenePath);
        }

        [MenuItem("RTS/Open or Create Interface Prototype Scene")]
        public static void OpenOrCreateInterface()
        {
            OpenOrCreateScene(InterfaceScenePath);
        }

        [MenuItem("RTS/Open or Create Match Prototype Scene")]
        public static void OpenOrCreateMatch()
        {
            MainMenuBuilder.EnsureProfiles();
            OpenOrCreateScene(MatchScenePath);
        }

        private static void OpenOrCreateScene(string path)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
            {
                EditorSceneManager.OpenScene(path);
                return;
            }

            CreateScene(path);
        }

        public static void BuildAndVerify()
        {
            RunNavigationChecks();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateScene(ScenePath);
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            var units = UnityEngine.Object.FindObjectsByType<PrototypeUnit>(FindObjectsSortMode.None);
            var controls = UnityEngine.Object.FindFirstObjectByType<PrototypeControls>();
            Require(scene.IsValid() && controls != null && units.Length == 5 &&
                Camera.main != null && Camera.main.orthographic, "Prototype scene wiring is incomplete.");
            var fields = new SerializedObject(controls);
            Require(fields.FindProperty("viewCamera").objectReferenceValue == Camera.main &&
                fields.FindProperty("obstacles").arraySize == 3 &&
                fields.FindProperty("units").arraySize == units.Length, "Control references are incomplete.");
            int ownUnits = 0;
            foreach (PrototypeUnit unit in units)
            {
                if (unit.PlayerOwned)
                {
                    ownUnits++;
                }

                var marker = (GameObject)new SerializedObject(unit)
                    .FindProperty("selectionMarker").objectReferenceValue;
                Require(marker != null && !marker.activeSelf && unit.GetComponent<Collider>() != null,
                    "A unit marker or collider is missing.");
            }

            Require(ownUnits == 4, "Expected four player units.");
            units[0].ReplaceRoute(new[] { Vector3.zero, Vector3.one });
            units[0].ReplaceRoute(new[] { Vector3.right });
            Require(units[0].RemainingWaypoints == 1, "A new route must replace the previous one.");
            Debug.Log("RTS prototype verification passed.");
        }

        public static void BuildAndVerifyCombat()
        {
            BuildAndVerify();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(CombatScenePath) == null)
            {
                CreateScene(CombatScenePath);
            }

            EditorSceneManager.OpenScene(CombatScenePath);
            CombatChecks.Run();
            Debug.Log("RTS combat verification passed.");
        }

        public static void BuildAndVerifyEconomy()
        {
            BuildAndVerifyCombat();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(EconomyScenePath) == null)
            {
                CreateScene(EconomyScenePath);
            }

            EditorSceneManager.OpenScene(EconomyScenePath);
            EconomyChecks.Run();
            Debug.Log("RTS economy verification passed.");
        }

        public static void BuildAndVerifyConstruction()
        {
            BuildAndVerifyEconomy();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ConstructionScenePath) == null)
            {
                CreateScene(ConstructionScenePath);
            }

            EditorSceneManager.OpenScene(ConstructionScenePath);
            ConstructionChecks.Run();
            Debug.Log("RTS construction verification passed.");
        }

        public static void BuildAndVerifyInterface()
        {
            BuildAndVerifyConstruction();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(InterfaceScenePath) == null)
            {
                CreateScene(InterfaceScenePath);
            }

            EditorSceneManager.OpenScene(InterfaceScenePath);
            InterfaceChecks.Run();
            Debug.Log("RTS interface verification passed.");
        }

        public static void BuildAndVerifyMatch()
        {
            MainMenuBuilder.EnsureProfiles();
            BuildAndVerifyInterface();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MatchScenePath) == null)
            {
                CreateScene(MatchScenePath);
            }

            EditorSceneManager.OpenScene(MatchScenePath);
            if (UnityEngine.Object.FindFirstObjectByType<MatchVisibility>() != null)
                VisibilityChecks.BuildAndVerify();
            else
                MatchChecks.Run();
            Debug.Log("RTS match verification passed.");
        }

        private static void RunNavigationChecks()
        {
            var route = new List<Vector3>();
            var start = new Vector3(1.5f, 0f, 1.5f);
            var goal = new Vector3(8.25f, 0f, 6.75f);
            var map = new RectInt(0, 0, 10, 8);
            var open = new GridNavigation(map, Array.Empty<Bounds>(), 0.4f);
            Require(open.TryFindRoute(start, goal, route) && route.Count == 1 && route[0] == goal,
                "An open route should be one straight segment to the exact target.");

            var grid = new GridNavigation(map,
                new[] { new Bounds(new Vector3(4.5f, 0f, 3f), new Vector3(1f, 2f, 4f)) }, 0.4f);
            start = new Vector3(1.5f, 0f, 3.5f);
            goal = new Vector3(8.5f, 0f, 3.5f);
            Require(!grid.HasClearSegment(start, goal) && grid.TryFindRoute(start, goal, route) &&
                route.Count > 1 && route[route.Count - 1] == goal, "A* did not return a detour.");
            foreach (Vector3 point in route)
            {
                Require(grid.HasClearSegment(start, point), "A smoothed segment crossed the clearance around a wall.");
                start = point;
            }

            Require(!grid.TryFindRoute(start, new Vector3(4.5f, 0f, 3.5f), route) && route.Count == 0,
                "Blocked destination was accepted.");
            Require(!grid.TryFindRoute(start, new Vector3(10.5f, 0f, 3.5f), route),
                "Out-of-bounds destination was accepted.");

            var corners = new GridNavigation(new RectInt(0, 0, 2, 2), new[]
            {
                new Bounds(new Vector3(1.5f, 0f, 0.5f), new Vector3(0.1f, 2f, 0.1f)),
                new Bounds(new Vector3(0.5f, 0f, 1.5f), new Vector3(0.1f, 2f, 0.1f))
            }, 0.4f);
            Require(!corners.TryFindRoute(new Vector3(0.5f, 0f, 0.5f), new Vector3(1.5f, 0f, 1.5f), route),
                "A diagonal cut between two blocked cells.");

            RunMovementChecks();
        }

        private static void RunMovementChecks()
        {
            var units = new PrototypeUnit[3];
            for (int index = 0; index < units.Length; index++)
            {
                var item = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                item.transform.localScale = new Vector3(0.8f, 0.85f, 0.8f);
                units[index] = item.AddComponent<PrototypeUnit>();
            }

            try
            {
                var map = new RectInt(0, 0, 16, 10);
                SetPosition(units[0], 2.5f, 4.5f);
                SetPosition(units[1], 10.5f, 4.5f);
                SetPosition(units[2], 14.5f, 8.5f);
                var movement = new UnitMovement(map, Array.Empty<Bounds>(), units);
                var east = new Vector3(12.5f, 0f, 4.5f);
                var west = new Vector3(0.5f, 0f, 4.5f);
                movement.IssueOrder(new[] { units[0] }, east);
                movement.IssueOrder(new[] { units[1] }, west);
                Simulate(movement, units, 600);
                Require(At(units[0], east) && At(units[1], west),
                    $"Head-on units stopped at {units[0].transform.position} and {units[1].transform.position}.");

                SetPosition(units[0], 1.5f, 4.5f);
                SetPosition(units[1], 2.5f, 4.5f);
                SetPosition(units[2], 3.5f, 4.5f);
                movement = new UnitMovement(map, new[]
                {
                    new Bounds(new Vector3(8f, 0f, 1.75f), new Vector3(2f, 2f, 3.5f)),
                    new Bounds(new Vector3(8f, 0f, 7.75f), new Vector3(2f, 2f, 4.5f))
                }, units);
                movement.IssueOrder(units, new Vector3(13.5f, 0f, 4.5f));
                Simulate(movement, units, 700);
                foreach (PrototypeUnit unit in units)
                {
                    Require(!unit.IsMoving && unit.transform.position.x > 10f && At(unit, unit.Destination),
                        "A group member did not get through the one-cell passage.");
                }

                SetPosition(units[0], 2.5f, 4.5f);
                SetPosition(units[1], 7.5f, 4.5f);
                SetPosition(units[2], 3.5f, 4.5f);
                movement = new UnitMovement(map, Array.Empty<Bounds>(), units);
                var occupied = new Vector3(7.5f, 0f, 4.5f);
                movement.IssueOrder(new[] { units[0] }, occupied);
                movement.IssueOrder(new[] { units[2] }, occupied);
                Require((GridNavigation.ToPlane(units[0].Destination) - GridNavigation.ToPlane(occupied)).sqrMagnitude == 1f &&
                    units[0].Destination != units[2].Destination, "An occupied or reserved destination was assigned.");
                Simulate(movement, units, 600);
                Require(At(units[0], units[0].Destination) && At(units[2], units[2].Destination) &&
                    At(units[1], occupied), "Units did not reach free positions around a stationary unit.");

                SetPosition(units[0], 2.5f, 4.5f);
                SetPosition(units[1], 4.5f, 4.5f);
                SetPosition(units[2], 6.5f, 4.5f);
                movement = new UnitMovement(new RectInt(0, 4, 9, 1), Array.Empty<Bounds>(), units);
                var unreachableWhileParked = new Vector3(8.5f, 0f, 4.5f);
                movement.IssueOrder(new[] { units[0] }, unreachableWhileParked);
                Simulate(movement, units, 600);
                Require(!units[0].IsMoving && !At(units[0], unreachableWhileParked),
                    "An indefinitely blocked order must stop without crossing the parked unit.");
            }
            finally
            {
                foreach (PrototypeUnit unit in units)
                {
                    UnityEngine.Object.DestroyImmediate(unit.gameObject);
                }
            }
        }

        private static void Simulate(UnitMovement movement, PrototypeUnit[] units, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                movement.Tick(0.02f);
                for (int left = 0; left < units.Length; left++)
                {
                    for (int right = left + 1; right < units.Length; right++)
                    {
                        var delta = GridNavigation.ToPlane(units[left].transform.position - units[right].transform.position);
                        Require(delta.magnitude + 0.0001f >= units[left].Radius + units[right].Radius + 0.05f,
                            "Units overlapped or violated minimum separation.");
                    }
                }
            }
        }

        private static void SetPosition(PrototypeUnit unit, float x, float z)
        {
            unit.transform.position = new Vector3(x, 0.85f, z);
        }

        private static bool At(PrototypeUnit unit, Vector3 point)
        {
            return GridNavigation.ToPlane(unit.transform.position - point).sqrMagnitude < 0.000001f;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void CreateScene(string path)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.53f, 0.58f, 0.63f);

            Material ground = GetMaterial("Ground", new Color(0.31f, 0.40f, 0.32f));
            Material stone = GetMaterial("Obstacle", new Color(0.36f, 0.28f, 0.22f));
            Material ally = GetMaterial("Player", new Color(0.06f, 0.56f, 0.88f));
            Material rival = GetMaterial("Opponent", new Color(0.82f, 0.19f, 0.12f));
            Material selection = GetMaterial("Selection", new Color(0.96f, 0.91f, 0.11f));

            bool isMatch = path == MatchScenePath;
            Vector3 mapScale = isMatch ? new Vector3(MatchMap.width / 10f, 1, MatchMap.height / 10f)
                : new Vector3(4.8f, 1, 3.6f);
            GameObject map = Primitive(PrimitiveType.Plane, "Map", Vector3.zero, mapScale, ground);
            var obstacles = new List<Collider>
            {
                Primitive(PrimitiveType.Cube, "Central wall", new Vector3(0, 1.25f, 0),
                    new Vector3(3, 2.5f, 10), stone).GetComponent<Collider>(),
                Primitive(PrimitiveType.Cube, "Northwest block", isMatch ? new Vector3(-14, 1, 9) : new Vector3(-10, 1, 7),
                    new Vector3(5, 2, 3), stone).GetComponent<Collider>(),
                Primitive(PrimitiveType.Cube, "Southeast block", isMatch ? new Vector3(14, 1, -9) : new Vector3(9, 1, -7),
                    new Vector3(5, 2, 4), stone).GetComponent<Collider>()
            };
            var targets = new List<CombatTarget>();
            var units = isMatch ? ConfigureMatch(map, obstacles, targets, ally, rival, selection) : new[]
            {
                CreateUnit("Player 1", -6.5f, -2.5f, ally, selection, true),
                CreateUnit("Player 2", -4.5f, -1.5f, ally, selection, true),
                CreateUnit("Player 3", -6.5f, 1.5f, ally, selection, true),
                CreateUnit("Player 4", -4.5f, 2.5f, ally, selection, true),
                CreateUnit("Opponent", 6.5f, 2.5f, rival, selection, false)
            };
            if (path != ScenePath && !isMatch)
            {
                // Temporary tuning values for the test scene. Each can be edited in the Inspector.
                ConfigureUnit(units[0], UnitKind.Worker, 70, 0, 0, 4.6f, 0, 0.8f, targets);
                ConfigureUnit(units[1], UnitKind.Infantry, 110, 17, 1.05f, 5f, 0, 0.8f, targets);
                ConfigureUnit(units[2], UnitKind.Archer, 75, 12, 5f, 4.7f, 0, 0.9f, targets);
                ConfigureUnit(units[3], UnitKind.Cavalry, 125, 22, 1.15f, 7.3f, 0, 1f, targets);
                ConfigureUnit(units[4], UnitKind.Infantry, 105, 14, 1.05f, 4.8f, 5f, 0.85f, targets);
                AddBuilding("Town Hall", BuildingKind.TownHall, Side.Player, -12.5f, 1.5f, 3, 220, 0.5f,
                    ally, obstacles, targets);
                AddBuilding("House", BuildingKind.House, Side.Player, -10.5f, -6.5f, 2, 130, 1f,
                    ally, obstacles, targets);
                AddBuilding("Barracks", BuildingKind.Barracks, Side.Opponent, 12.5f, 1.5f, 3, 180, 1f,
                    rival, obstacles, targets);
            }

            if (path == EconomyScenePath || path == ConstructionScenePath || path == InterfaceScenePath)
            {
                AddBuilding("Player Barracks", BuildingKind.Barracks, Side.Player, -10.5f, 5.5f, 3, 180, 1f,
                    ally, obstacles, targets);
                Material gold = GetMaterial("GoldDeposit", new Color(0.91f, 0.66f, 0.14f));
                GameObject source = Primitive(PrimitiveType.Cube, "Gold Deposit", new Vector3(-4.5f, 0.65f, -7.5f),
                    new Vector3(2f, 1.3f, 2f), gold);
                source.AddComponent<GoldDeposit>();
                obstacles.Add(source.GetComponent<Collider>());
                ConfigureEconomy(map, units);
                if (path == ConstructionScenePath || path == InterfaceScenePath)
                {
                    ConfigureConstruction(map, ally);
                }
                if (path == InterfaceScenePath)
                {
                    map.AddComponent<PrototypeInterface>();
                }
            }

            var cameraObject = new GameObject("RTS Camera") { tag = "MainCamera" };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.14f, 0.17f);
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            camera.transform.position = -camera.transform.forward * 22f;

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -35, 0);

            var controls = new SerializedObject(map.AddComponent<PrototypeControls>());
            controls.FindProperty("viewCamera").objectReferenceValue = camera;
            if (isMatch)
            {
                controls.FindProperty("mapBounds").rectIntValue = MatchMap;
            }
            SetArray(controls.FindProperty("obstacles"), obstacles.ToArray());
            SetArray(controls.FindProperty("units"), units);
            SetArray(controls.FindProperty("combatTargets"), targets.ToArray());
            controls.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new InvalidOperationException($"Could not save {path}.");
            }
            AssetDatabase.SaveAssets();
        }

        private static PrototypeEconomy ConfigureEconomy(GameObject map, PrototypeUnit[] units)
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets/RTS", "EconomyPrefabs");
            }

            var economy = map.AddComponent<PrototypeEconomy>();
            var settings = new SerializedObject(economy);
            SerializedProperty products = settings.FindProperty("products");
            products.arraySize = 4;
            // Temporary prototype values, editable on the Map object.
            var costs = new[] { 20, 30, 40, 55 };
            var durations = new[] { 2f, 3f, 3.5f, 4.5f };
            for (int index = 0; index < products.arraySize; index++)
            {
                var kind = (UnitKind)index;
                string path = $"{PrefabFolder}/{kind}.prefab";
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    PrototypeUnit unit = Array.Find(units, item => item.PlayerOwned && item.Kind == kind);
                    Require(unit != null, $"Missing production prefab for {kind}.");
                    asset = PrefabUtility.SaveAsPrefabAsset(unit.gameObject, path);
                }

                SerializedProperty product = products.GetArrayElementAtIndex(index);
                product.FindPropertyRelative("prefab").objectReferenceValue = asset.GetComponent<PrototypeUnit>();
                product.FindPropertyRelative("cost").intValue = costs[index];
                product.FindPropertyRelative("seconds").floatValue = durations[index];
            }

            settings.ApplyModifiedPropertiesWithoutUndo();
            return economy;
        }

        private static PrototypeUnit[] ConfigureMatch(GameObject map, List<Collider> obstacles,
            List<CombatTarget> targets, Material ally, Material rival, Material selection)
        {
            Material gold = GetMaterial("GoldDeposit", new Color(0.91f, 0.66f, 0.14f));
            var units = new List<PrototypeUnit>();
            foreach (Side side in new[] { Side.Player, Side.Opponent })
            {
                bool player = side == Side.Player;
                float direction = player ? -1f : 1f;
                Material material = player ? ally : rival;
                AddBuilding($"{side} Town Hall", BuildingKind.TownHall, side, direction * 29.5f, 0.5f,
                    3, 220, 1f, material, obstacles, targets);
                AddMatchMine($"{side} Gold", direction * 25.5f, -7.5f, 1600, gold, obstacles);
                AddMatchMine($"{side} Reserve Gold", direction * 8.5f, 20.5f, 1400, gold, obstacles);

                PrototypeUnit worker = CreateUnit("Worker", direction * 27.5f, -3.5f, material, selection, player);
                PrototypeUnit infantry = CreateUnit("Infantry", direction * 23.5f, 1.5f, material, selection, player);
                PrototypeUnit archer = CreateUnit("Archer", direction * 25.5f, 3.5f, material, selection, player);
                ConfigureUnit(worker, UnitKind.Worker, 70, 0, 0, 4.6f, 0, 0.8f, targets);
                ConfigureUnit(infantry, UnitKind.Infantry, 110, 17, 1.05f, 5f, 0, 0.8f, targets);
                ConfigureUnit(archer, UnitKind.Archer, 75, 12, 5f, 4.7f, 0, 0.9f, targets);
                units.AddRange(new[] { worker, infantry, archer });
            }

            PrototypeUnit[] roster = units.ToArray();
            PrototypeEconomy rules = ConfigureEconomy(map, roster);
            var trainingEconomy = new SerializedObject(rules);
            trainingEconomy.FindProperty("initialGold").intValue = 140;
            trainingEconomy.FindProperty("loadSize").intValue = 20;
            trainingEconomy.FindProperty("gatherSeconds").floatValue = 0.75f;
            trainingEconomy.ApplyModifiedPropertiesWithoutUndo();
            var opponent = new SerializedObject(map.AddComponent<PrototypeEconomy>());
            opponent.FindProperty("owner").enumValueIndex = (int)Side.Opponent;
            opponent.FindProperty("sharedRules").objectReferenceValue = rules;
            opponent.FindProperty("unitMaterial").objectReferenceValue = rival;
            opponent.ApplyModifiedPropertiesWithoutUndo();
            ConfigureConstruction(map, ally, rival);
            map.AddComponent<PrototypeInterface>();
            var visibility = map.AddComponent<MatchVisibility>();
            var director = new SerializedObject(map.AddComponent<OpponentDirector>());
            director.FindProperty("visibilitySource").objectReferenceValue = visibility;
            director.ApplyModifiedPropertiesWithoutUndo();
            map.AddComponent<PrototypeMatch>();
            return roster;
        }

        private static void AddMatchMine(string name, float x, float z, int amount, Material gold,
            List<Collider> obstacles)
        {
            GameObject deposit = Primitive(PrimitiveType.Cube, name, new Vector3(x, 0.65f, z),
                new Vector3(2f, 1.3f, 2f), gold);
            var stock = new SerializedObject(deposit.AddComponent<GoldDeposit>());
            stock.FindProperty("initialStock").intValue = amount;
            stock.ApplyModifiedPropertiesWithoutUndo();
            obstacles.Add(deposit.GetComponent<Collider>());
        }

        private static void ConfigureConstruction(GameObject map, Material material, Material rival = null)
        {
            var settings = new SerializedObject(map.AddComponent<PrototypeConstruction>());
            settings.FindProperty("buildingMaterial").objectReferenceValue = material;
            settings.FindProperty("opponentMaterial").objectReferenceValue = rival;
            SerializedProperty blueprints = settings.FindProperty("blueprints");
            blueprints.arraySize = 3;
            var kinds = new[] { BuildingKind.TownHall, BuildingKind.Barracks, BuildingKind.House };
            var widths = new[] { 3f, 3f, 2f };
            var costs = new[] { 70, 55, 30 };
            var durations = new[] { 7f, 6f, 5f };
            var health = new[] { 220f, 180f, 130f };
            for (int index = 0; index < blueprints.arraySize; index++)
            {
                SerializedProperty blueprint = blueprints.GetArrayElementAtIndex(index);
                blueprint.FindPropertyRelative("kind").enumValueIndex = (int)kinds[index];
                blueprint.FindPropertyRelative("size").vector3Value = new Vector3(widths[index], 1.8f, widths[index]);
                blueprint.FindPropertyRelative("cost").intValue = costs[index];
                blueprint.FindPropertyRelative("seconds").floatValue = durations[index];
                blueprint.FindPropertyRelative("health").floatValue = health[index];
            }

            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureUnit(PrototypeUnit unit, UnitKind kind, float health, float damage,
            float range, float speed, float detection, float interval, List<CombatTarget> targets)
        {
            unit.name = $"{unit.Team} {kind}";
            var fields = new SerializedObject(unit);
            fields.FindProperty("kind").enumValueIndex = (int)kind;
            fields.FindProperty("attackDamage").floatValue = damage;
            fields.FindProperty("attackRange").floatValue = range;
            fields.FindProperty("speed").floatValue = speed;
            fields.FindProperty("detectionRange").floatValue = detection;
            fields.FindProperty("attackInterval").floatValue = interval;
            fields.ApplyModifiedPropertiesWithoutUndo();
            targets.Add(CreateCombatTarget(unit.gameObject, BuildingKind.None, unit.Team, health, 1f));
        }

        private static void AddBuilding(string name, BuildingKind kind, Side side, float x, float z, float size,
            float health, float initialHealth, Material material, List<Collider> obstacles, List<CombatTarget> targets)
        {
            GameObject item = Primitive(PrimitiveType.Cube, name, new Vector3(x, 0.9f, z),
                new Vector3(size, 1.8f, size), material);
            obstacles.Add(item.GetComponent<Collider>());
            targets.Add(CreateCombatTarget(item, kind, side, health, initialHealth));
        }

        private static CombatTarget CreateCombatTarget(GameObject item, BuildingKind kind, Side side,
            float health, float initialHealth)
        {
            var target = item.AddComponent<CombatTarget>();
            var fields = new SerializedObject(target);
            fields.FindProperty("building").enumValueIndex = (int)kind;
            fields.FindProperty("side").enumValueIndex = (int)side;
            fields.FindProperty("maxHealth").floatValue = health;
            fields.FindProperty("initialHealthFraction").floatValue = initialHealth;
            fields.ApplyModifiedPropertiesWithoutUndo();
            return target;
        }

        private static void SetArray(SerializedProperty property, UnityEngine.Object[] values)
        {
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            }
        }

        private static PrototypeUnit CreateUnit(string name, float x, float z, Material color,
            Material selection, bool playerOwned)
        {
            GameObject unit = Primitive(PrimitiveType.Capsule, name, new Vector3(x, 0.85f, z),
                new Vector3(0.8f, 0.85f, 0.8f), color);
            var marker = new GameObject("Selection outline");
            marker.transform.SetParent(unit.transform, false);
            var line = marker.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = 0.085f;
            line.sharedMaterial = selection;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.TransformZ;
            marker.transform.localPosition = new Vector3(0, -0.95f, 0);
            marker.transform.localRotation = Quaternion.Euler(90, 0, 0);
            const int segments = 32;
            line.positionCount = segments;
            for (int index = 0; index < segments; index++)
            {
                float angle = index * Mathf.PI * 2 / segments;
                line.SetPosition(index, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * 0.71f);
            }

            marker.SetActive(false);
            PrototypeUnit component = unit.AddComponent<PrototypeUnit>();
            var fields = new SerializedObject(component);
            fields.FindProperty("playerOwned").boolValue = playerOwned;
            fields.FindProperty("selectionMarker").objectReferenceValue = marker;
            fields.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.position = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            return item;
        }

        private static Material GetMaterial(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/RTS", "PrototypeMaterials");
            }

            string path = $"{MaterialFolder}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Require(shader != null, "URP Unlit shader is unavailable.");
            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}

