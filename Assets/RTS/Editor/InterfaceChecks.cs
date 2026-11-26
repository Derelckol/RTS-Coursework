using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rts.Editor
{
    internal static class InterfaceChecks
    {
        private static readonly RectInt Map = new RectInt(-24, -18, 48, 36);
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Run()
        {
            string scene = SceneManager.GetActiveScene().path;
            CheckLayout(new HudLayout(1366, 768, true), 1f);
            CheckLayout(new HudLayout(1920, 1080, true), 1.4f);
            CheckMinimap(new Fixture());
            EditorSceneManager.OpenScene(scene);
            CheckPause(new Fixture());
        }

        private static void CheckLayout(HudLayout layout, float minimumScale)
        {
            Rect panel = layout.MinimapPanel(Map);
            Rect content = layout.MinimapContent(Map);
            var zones = new[] { layout.Resources, layout.Actions(true), layout.Menu, panel, layout.PausePanel };
            for (int left = 0; left < zones.Length; left++)
            {
                Rect bounds = layout.FullScreen;
                Rect zone = zones[left];
                Require(zone.xMin >= bounds.xMin && zone.yMin >= bounds.yMin &&
                    zone.xMax <= bounds.xMax && zone.yMax <= bounds.yMax, "HUD extends beyond the screen.");
                for (int right = left + 1; right < zones.Length; right++)
                {
                    Require(!zone.Overlaps(zones[right]), "HUD panels overlap.");
                }
            }

            Require(layout.Scale >= minimumScale && content.width / content.height > 1.33f &&
                content.xMin > panel.xMin && content.xMax < panel.xMax &&
                layout.Menu.width * layout.Scale >= 114f &&
                layout.Actions(true).height * layout.Scale >= 220f, "HUD layout does not scale to the display.");
        }

        private static void CheckMinimap(Fixture fixture)
        {
            var layout = new HudLayout(1920, 1080, true);
            Rect content = layout.MinimapContent(Map);
            var markers = new List<MapMarker>();
            void Refresh() => fixture.Hud.CollectMarkers(content, fixture.Obstacles, fixture.Actors, markers);
            int Count(MapMarkerKind kind) => markers.Count(marker => marker.Kind == kind);
            Refresh();
            Require(Count(MapMarkerKind.Obstacle) == 3 && Count(MapMarkerKind.Gold) == 1 &&
                Count(MapMarkerKind.AllyUnit) == 4 && Count(MapMarkerKind.EnemyUnit) == 1 &&
                Count(MapMarkerKind.AllyBuilding) == 3 && Count(MapMarkerKind.EnemyBuilding) == 1,
                "The minimap omitted or misclassified map objects.");

            PrototypeUnit worker = fixture.Player(UnitKind.Worker);
            Vector2 original = fixture.Hud.Project(content, worker.transform.position);
            worker.transform.position = new Vector3(-5.5f, 0.85f, -10.5f);
            Vector2 updated = fixture.Hud.Project(content, worker.transform.position);
            Refresh();
            Require(markers.Any(marker => marker.Kind == MapMarkerKind.AllyUnit && marker.Area.center == updated) &&
                !markers.Any(marker => marker.Kind == MapMarkerKind.AllyUnit && marker.Area.center == original),
                "Moving a unit did not update its marker.");

            var blueprint = fixture.Construction.Blueprints.Single(item => item.Kind == BuildingKind.House);
            fixture.Construction.Begin(blueprint, worker);
            CombatTarget foundation = fixture.Construction.Confirm(new Vector3(-1.5f, 0f, -12.5f));
            Require(foundation != null, "Minimap test could not create a foundation.");
            Refresh();
            Require(Count(MapMarkerKind.AllyBuilding) == 4, "A new foundation did not appear.");
            foundation.Damage(foundation.MaxHealth);
            Refresh();
            Require(Count(MapMarkerKind.AllyBuilding) == 3, "A destroyed foundation kept its marker.");

            Require(fixture.Economy.Enqueue(fixture.Hall, fixture.WorkerProduct), "Test pickup order failed.");
            fixture.Tick(115);
            Refresh();
            Require(Count(MapMarkerKind.AllyUnit) == 5, "A produced unit did not appear.");
            CombatTarget enemy = fixture.Actors.Single(actor => actor.Unit != null && actor.Side == Side.Opponent);
            enemy.Damage(enemy.MaxHealth);
            Refresh();
            Require(Count(MapMarkerKind.EnemyUnit) == 0, "A destroyed unit kept its marker.");

            Vector3 world = new Vector3(4, 0, 3);
            Vector2 logical = fixture.Hud.Project(content, world);
            var physical = new Vector2(logical.x * layout.Scale, 1080 - logical.y * layout.Scale);
            Require((layout.FromScreen(physical) - logical).sqrMagnitude < 0.001f &&
                fixture.Hud.TryMinimapFocus(layout, logical, out Vector3 focus) &&
                (focus - world).sqrMagnitude < 0.001f, "Scaled minimap input did not invert its projection.");
            Require(fixture.Covered(layout, logical) && fixture.Covered(layout, layout.Menu.center) &&
                fixture.Covered(layout, layout.Resources.center) && fixture.Covered(layout, layout.Actions(true).center),
                "A HUD region passed input through to the world.");

            Rect before = fixture.Hud.CameraFootprint(content, fixture.Camera);
            Require(fixture.Focus(layout, new Vector2(content.xMax - 1, content.yMin + 1)),
                "Minimap click was not routed to the camera.");
            Rect after = fixture.Hud.CameraFootprint(content, fixture.Camera);
            Require(after.center.x > before.center.x && after.center.y < before.center.y &&
                after.xMax <= content.xMax + 0.01f && after.yMin >= content.yMin - 0.01f &&
                Mathf.Abs(after.xMax - content.xMax) < 0.02f && Mathf.Abs(after.yMin - content.yMin) < 0.02f,
                "The camera moved beyond the minimap bounds or its footprint did not update.");
        }

        private static void CheckPause(Fixture fixture)
        {
            PrototypeUnit mine = fixture.Player(UnitKind.Worker);
            PrototypeUnit mover = fixture.Player(UnitKind.Infantry);
            PrototypeUnit blocked = fixture.Player(UnitKind.Cavalry);
            PrototypeUnit archer = fixture.Player(UnitKind.Archer);
            PrototypeUnit repair = fixture.AddWorker(new Vector3(-15.5f, 0.85f, 1.5f));
            PrototypeUnit builder = fixture.AddWorker(new Vector3(5.5f, 0.85f, -13.5f));
            fixture.AddWorker(new Vector3(-11.6f, 0.85f, -12.5f));
            mine.transform.position = new Vector3(-2.5f, 0.85f, -7.5f);
            mover.transform.position = new Vector3(1.5f, 0.85f, -12.5f);
            blocked.transform.position = new Vector3(-12.5f, 0.85f, -12.5f);
            archer.transform.position = new Vector3(8.5f, 0.85f, 1.5f);
            var blueprint = fixture.Construction.Blueprints.Single(item => item.Kind == BuildingKind.House);

            fixture.Construction.Begin(blueprint, builder);
            fixture.Hud.HandleEscape(fixture.Construction);
            Require(!fixture.Hud.IsPaused && !fixture.Construction.IsPlacing, "First Escape did not cancel placement.");
            fixture.Construction.Begin(blueprint, builder);
            CombatTarget site = fixture.Construction.Confirm(new Vector3(7.5f, 0f, -14.5f));
            Require(site != null && fixture.Economy.Enqueue(fixture.Hall, fixture.WorkerProduct),
                "Pause scenario could not start construction and production.");
            fixture.Orders.Gather(new[] { mine }, fixture.Source);
            fixture.Orders.Interact(new[] { repair }, fixture.Hall);
            CombatTarget enemyBuilding = fixture.Actors.Single(actor =>
                actor.Side == Side.Opponent && actor.Building == BuildingKind.Barracks);
            PrototypeUnit enemy = fixture.Actors.Single(actor => actor.Unit != null && actor.Side == Side.Opponent).Unit;
            fixture.Orders.Interact(new[] { archer }, enemyBuilding);
            fixture.Orders.Move(new[] { mover }, new Vector3(8.5f, 0, -12.5f));
            fixture.Orders.Move(new[] { blocked }, new Vector3(-5.5f, 0, -12.5f));
            fixture.Tick(1);

            var layout = new HudLayout(1366, 768, true);
            Vector2 minimap = layout.MinimapContent(Map).center;
            Vector3 cameraPosition = fixture.Camera.transform.position;
            Vector3 moverPosition = mover.transform.position;
            Vector3 enemyPosition = enemy.transform.position;
            float blockedTime = BlockedTime(blocked);
            float clock = Get<float>(fixture.Orders, "time");
            float health = enemyBuilding.Health;
            float repairHealth = fixture.Hall.Health;
            float progress = site.Site.Progress;
            int stock = fixture.Source.Stock;
            int gold = fixture.Economy.Gold;
            int units = fixture.Economy.Used;
            string queue = fixture.Economy.QueueSummary(fixture.Hall);
            Require(blockedTime > 0 && progress > 0 && health < enemyBuilding.MaxHealth &&
                fixture.Orders.StateOf(enemy) == UnitState.Attack &&
                fixture.Orders.StateOf(mine) == UnitState.Gather &&
                fixture.Orders.StateOf(repair) == UnitState.Repair && !string.IsNullOrEmpty(queue),
                "Pause test did not start all required processes.");

            fixture.Hud.HandleEscape(fixture.Construction);
            Require(fixture.Hud.IsPaused && fixture.Covered(layout, new Vector2(600, 380)) &&
                !fixture.Focus(layout, minimap) && fixture.Camera.transform.position == cameraPosition,
                "Pause did not capture world or minimap input.");
            fixture.Tick(600);
            Require(mover.transform.position == moverPosition && enemy.transform.position == enemyPosition &&
                BlockedTime(blocked) == blockedTime && Get<float>(fixture.Orders, "time") == clock &&
                enemyBuilding.Health == health && fixture.Hall.Health == repairHealth &&
                site.Site.Progress == progress && fixture.Source.Stock == stock && mine.CarriedGold == 0 &&
                fixture.Economy.Gold == gold && fixture.Economy.Used == units &&
                fixture.Economy.QueueSummary(fixture.Hall) == queue,
                "A gameplay process or blockage timer advanced during pause.");

            Invoke(fixture.Controls, "SetPaused", false);
            fixture.Tick(115);
            Require(!fixture.Hud.IsPaused && mover.transform.position != moverPosition &&
                enemy.transform.position != enemyPosition && enemyBuilding.Health < health &&
                fixture.Hall.Health > repairHealth && fixture.Source.Stock < stock && site.Site.Progress > progress &&
                fixture.Economy.Used == units + 1, "Gameplay did not resume from its paused state.");
            fixture.Hud.HandleEscape(fixture.Construction);
            fixture.Hud.HandleEscape(fixture.Construction);
            Require(!fixture.Hud.IsPaused, "Escape did not close pause.");
        }

        private static float BlockedTime(PrototypeUnit unit) =>
            (float)typeof(PrototypeUnit).GetProperty("BlockedTime", InstanceMembers).GetValue(unit);

        private static T Get<T>(object instance, string name) =>
            (T)instance.GetType().GetField(name, InstanceMembers).GetValue(instance);

        private static object Invoke(object instance, string method, params object[] args) =>
            instance.GetType().GetMethod(method, InstanceMembers).Invoke(instance, args);

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class Fixture
        {
            public readonly PrototypeControls Controls;
            public readonly PrototypeInterface Hud;
            public readonly PrototypeConstruction Construction;
            public readonly PrototypeEconomy Economy;
            public readonly UnitOrders Orders;
            public readonly List<CombatTarget> Actors;
            public readonly List<Collider> Obstacles;
            public readonly Camera Camera;
            public readonly GoldDeposit Source;
            public readonly CombatTarget Hall;
            public PrototypeEconomy.Product WorkerProduct => Economy.Products.Single(item => item.Kind == UnitKind.Worker);

            public Fixture()
            {
                foreach (CombatTarget target in UnityEngine.Object.FindObjectsByType<CombatTarget>(FindObjectsSortMode.None))
                {
                    target.Initialize();
                }
                Source = UnityEngine.Object.FindFirstObjectByType<GoldDeposit>();
                Source.Initialize();
                Controls = UnityEngine.Object.FindFirstObjectByType<PrototypeControls>();
                Hud = Controls.GetComponent<PrototypeInterface>();
                Economy = Controls.GetComponent<PrototypeEconomy>();
                Construction = Controls.GetComponent<PrototypeConstruction>();
                Camera = Camera.main;
                Physics.SyncTransforms();
                Invoke(Controls, "Awake");
                Orders = Get<UnitOrders>(Controls, "orders");
                Actors = Get<List<CombatTarget>>(Controls, "activeTargets");
                Obstacles = Get<List<Collider>>(Controls, "activeObstacles");
                Hall = Actors.Single(target => target.Building == BuildingKind.TownHall);
            }

            public PrototypeUnit Player(UnitKind kind) =>
                Actors.Single(target => target.Unit != null && target.Side == Side.Player && target.Unit.Kind == kind).Unit;

            public PrototypeUnit AddWorker(Vector3 position)
            {
                var unit = UnityEngine.Object.Instantiate(WorkerProduct.Prefab, position, Quaternion.identity);
                var target = unit.GetComponent<CombatTarget>();
                target.Initialize();
                Invoke(Controls, "RegisterSpawn", target);
                return unit;
            }

            public bool Focus(HudLayout layout, Vector2 logical) =>
                (bool)Invoke(Controls, "TryFocusMinimap", layout, logical);

            public bool Covered(HudLayout layout, Vector2 logical) =>
                (bool)Invoke(Controls, "IsUiCovered", layout, logical);

            public void Tick(int steps)
            {
                for (int index = 0; index < steps; index++)
                {
                    Invoke(Controls, "AdvanceSimulation", 0.02f);
                }
            }
        }
    }
}
