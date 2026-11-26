using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rts.Editor
{
    internal static class ConstructionChecks
    {
        public static void Run()
        {
            string scene = SceneManager.GetActiveScene().path;
            CheckPlacementAndProgress(new Fixture());
            EditorSceneManager.OpenScene(scene);
            CheckNavigationAndDestruction(new Fixture());
            EditorSceneManager.OpenScene(scene);
            CheckEconomyActivation(new Fixture(250));
        }

        private static void CheckPlacementAndProgress(Fixture fixture)
        {
            var system = fixture.Construction;
            var house = fixture.Blueprint(BuildingKind.House);
            var hall = fixture.Blueprint(BuildingKind.TownHall);
            var point = new Vector3(-2.5f, 0f, -10.5f);
            int initialGold = fixture.Economy.Gold;
            int initialLimit = fixture.Economy.Maximum;
            system.Begin(hall, fixture.Worker);
            Require(!system.CanPlace(new Vector3(23.5f, 0f, 10.5f)), "Footprint extends beyond the map.");
            Require(!system.CanPlace(new Vector3(2.5f, 0f, 0.5f)), "Footprint overlaps the edge of a wall.");
            Require(!system.CanPlace(fixture.Hall.transform.position) &&
                !system.CanPlace(fixture.Source.transform.position) &&
                !system.CanPlace(fixture.Worker.transform.position), "An occupied site was accepted.");
            Require(system.Confirm(fixture.Hall.transform.position) == null && fixture.Economy.Gold == initialGold,
                "Invalid placement charged for a building.");

            fixture.Extra.AddRange(new[]
            {
                new Bounds(new Vector3(0.5f, 1f, 12), new Vector3(1, 2, 10)),
                new Bounds(new Vector3(8.5f, 1f, 12), new Vector3(1, 2, 10)),
                new Bounds(new Vector3(4.5f, 1f, 7.5f), new Vector3(9, 2, 1)),
                new Bounds(new Vector3(4.5f, 1f, 16.5f), new Vector3(9, 2, 1))
            });
            Require(!system.CanPlace(new Vector3(4.5f, 0f, 12.5f)),
                "A builder was assigned to a site with inaccessible perimeter.");
            fixture.Extra.Clear();

            system.Begin(house, fixture.Worker);
            system.ShowPreview(point);
            var preview = GameObject.Find("Construction preview");
            Require(preview != null && !preview.GetComponent<Collider>().enabled && system.CanPlace(point),
                "The preview blocked a valid site.");
            var tint = new MaterialPropertyBlock();
            var previewRenderer = preview.GetComponent<Renderer>();
            previewRenderer.GetPropertyBlock(tint);
            Color color = tint.GetColor("_BaseColor");
            Require(color.g > color.r, "Valid placement was not green.");
            system.ShowPreview(fixture.Hall.transform.position);
            previewRenderer.GetPropertyBlock(tint);
            color = tint.GetColor("_BaseColor");
            Require(color.r > color.g, "Invalid placement was not red.");
            system.Cancel();
            Require(!system.IsPlacing && !preview.activeSelf && system.Confirm(point) == null &&
                fixture.Economy.Gold == initialGold && fixture.Constructed.Count == 0,
                "Cancel left a preview, created a building or spent gold.");

            system.Begin(house, fixture.Worker);
            CombatTarget site = system.Confirm(point);
            Require(site != null && !site.IsOperational && fixture.Economy.Gold == initialGold - house.Cost &&
                system.Confirm(point) == null && fixture.Economy.Maximum == initialLimit,
                "A placement was charged twice or activated before completion.");
            system.Begin(hall, fixture.Worker);
            Require(system.CanPlace(new Vector3(4.5f, 0f, -13.5f)) &&
                system.Confirm(new Vector3(4.5f, 0f, -13.5f)) == null &&
                fixture.Economy.Gold == initialGold - house.Cost && fixture.Constructed.Count == 1 &&
                fixture.Economy.Message == "Not enough gold.", "Unfunded placement created or charged a site.");
            system.Cancel();

            fixture.Until(() => site.Site.Progress >= 0.2f, 450);
            fixture.Orders.Move(new[] { fixture.Worker }, new Vector3(-6.5f, 0f, -12.5f));
            float saved = site.Site.Progress;
            fixture.Tick(80);
            Require(site.Site.Progress == saved && site.Site.Builder == null,
                "A move order did not pause construction or preserve progress.");
            fixture.Orders.Interact(new[] { fixture.Worker }, site);
            fixture.Until(() => site.Site.Progress > saved, 250);
            saved = site.Site.Progress;
            var workerActor = fixture.Worker.GetComponent<CombatTarget>();
            workerActor.Damage(workerActor.MaxHealth);
            fixture.Tick(100);
            Require(site.Site.Progress == saved && site.Site.Builder == null &&
                fixture.Economy.Maximum == initialLimit, "Losing the builder changed construction progress.");

            PrototypeUnit replacement = fixture.AddWorker(new Vector3(-6.5f, 0.85f, -12.5f));
            fixture.Orders.Interact(new[] { replacement }, site);
            fixture.Until(() => site.IsOperational, 500);
            fixture.Economy.RegisterBuilding(site);
            Require(site.Site.Progress == 1f && fixture.Economy.Maximum > initialLimit,
                "Completed house did not increase the cap.");
            int completedLimit = fixture.Economy.Maximum;
            fixture.Tick(100);
            Require(fixture.Economy.Maximum == completedLimit, "House capacity was credited multiple times.");
            site.Damage(site.MaxHealth);
            Require(fixture.Economy.Maximum == initialLimit, "House capacity did not revert on defeat.");
        }

        private static void CheckNavigationAndDestruction(Fixture fixture)
        {
            PrototypeUnit mover = fixture.PlayerInfantry;
            mover.transform.position = new Vector3(2.5f, 0.85f, -11.5f);
            fixture.Worker.transform.position = new Vector3(4.5f, 0.85f, -14.5f);
            Vector3 destination = new Vector3(13.5f, 0f, -11.5f);
            fixture.Orders.Move(new[] { mover }, destination);
            Require(mover.RemainingWaypoints == 1, "The original route was not direct.");
            Vector3 sitePoint = new Vector3(7.5f, 0f, -11.5f);
            fixture.Construction.Begin(fixture.Blueprint(BuildingKind.House), fixture.Worker);
            CombatTarget site = fixture.Construction.Confirm(sitePoint);
            Require(site != null && mover.RemainingWaypoints > 1 && mover.Destination == destination,
                "Existing movement was not rerouted around the new foundation.");
            fixture.Orders.Move(new[] { fixture.Worker }, new Vector3(2.5f, 0f, -15.5f));
            for (int step = 0; step < 350 && mover.IsMoving; step++)
            {
                fixture.Tick(1);
                Require(site.DistanceFrom(mover.transform.position, mover.Radius) > 0.01f,
                    "A moving unit entered the new footprint.");
            }
            Require(At(mover, destination) && !site.IsOperational, "The mover did not complete its detour.");

            fixture.Enemy.transform.position = new Vector3(10.5f, 0.85f, -13.5f);
            fixture.Orders.Interact(new[] { fixture.Enemy }, site);
            fixture.Until(() => !site.IsAlive, 700);
            Require(fixture.Orders.StateOf(fixture.Enemy) == UnitState.Idle && !fixture.Constructed.Contains(site),
                "Defeating a foundation did not clean up combat and occupancy.");
            var enemyActor = fixture.Enemy.GetComponent<CombatTarget>();
            enemyActor.Damage(enemyActor.MaxHealth);
            fixture.Orders.Move(new[] { mover }, sitePoint);
            fixture.Until(() => At(mover, sitePoint), 300);
        }

        private static void CheckEconomyActivation(Fixture fixture)
        {
            var construction = fixture.Construction;
            var workerProduct = fixture.Economy.Products.Single(product => product.Kind == UnitKind.Worker);
            var infantryProduct = fixture.Economy.Products.Single(product => product.Kind == UnitKind.Infantry);
            construction.Begin(fixture.Blueprint(BuildingKind.TownHall), fixture.Worker);
            var site = construction.Confirm(new Vector3(-2.5f, 0f, -10.5f));
            Require(site != null && !fixture.Economy.Enqueue(site, workerProduct) &&
                fixture.Economy.FindTownHall(fixture.Worker) != site, "Unfinished town hall was operational.");
            fixture.Until(() => site.IsOperational, 650);
            Require(fixture.Economy.FindTownHall(fixture.Worker) == site && fixture.Economy.Enqueue(site, workerProduct),
                "Completed town hall was unavailable for delivery or production.");
            fixture.Until(() => fixture.Spawned.Count == 1, 200);

            construction.Begin(fixture.Blueprint(BuildingKind.Barracks), fixture.Worker);
            var barracks = construction.Confirm(new Vector3(3.5f, 0f, -13.5f));
            Require(barracks != null && !fixture.Economy.Enqueue(barracks, infantryProduct),
                "Unfinished barracks accepted an order.");
            fixture.Until(() => barracks.IsOperational, 650);
            Require(fixture.Economy.Enqueue(barracks, infantryProduct), "Completed barracks rejected production.");
            fixture.Until(() => fixture.Spawned.Count == 2, 240);
            Require(fixture.Spawned[1].Kind == UnitKind.Infantry, "The wrong unit spawned from new barracks.");
        }

        private static bool At(PrototypeUnit unit, Vector3 point) =>
            GridNavigation.ToPlane(unit.transform.position - point).sqrMagnitude < 0.001f;

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class Fixture
        {
            private readonly List<Collider> obstacles;
            private readonly UnitMovement movement;
            public readonly List<Bounds> Extra = new List<Bounds>();
            public readonly List<CombatTarget> Constructed = new List<CombatTarget>();
            public readonly List<PrototypeUnit> Spawned = new List<PrototypeUnit>();
            public readonly PrototypeConstruction Construction;
            public readonly PrototypeEconomy Economy;
            public readonly UnitOrders Orders;
            public readonly PrototypeUnit Worker;
            public readonly PrototypeUnit PlayerInfantry;
            public readonly PrototypeUnit Enemy;
            public readonly CombatTarget Hall;
            public readonly GoldDeposit Source;

            public Fixture(int? gold = null)
            {
                var controls = UnityEngine.Object.FindFirstObjectByType<PrototypeControls>();
                var configured = new SerializedObject(controls).FindProperty("obstacles");
                obstacles = Enumerable.Range(0, configured.arraySize)
                    .Select(index => (Collider)configured.GetArrayElementAtIndex(index).objectReferenceValue).ToList();
                var actors = UnityEngine.Object.FindObjectsByType<CombatTarget>(FindObjectsSortMode.None);
                foreach (CombatTarget target in actors)
                {
                    target.Initialize();
                }

                var units = actors.Where(target => target.Unit != null).Select(target => target.Unit).ToArray();
                Worker = units.Single(unit => unit.Kind == UnitKind.Worker && unit.PlayerOwned);
                PlayerInfantry = units.Single(unit => unit.Kind == UnitKind.Infantry && unit.PlayerOwned);
                Enemy = units.Single(unit => !unit.PlayerOwned);
                Hall = actors.Single(target => target.Building == BuildingKind.TownHall);
                Source = UnityEngine.Object.FindFirstObjectByType<GoldDeposit>();
                Economy = controls.GetComponent<PrototypeEconomy>();
                Construction = controls.GetComponent<PrototypeConstruction>();
                if (gold.HasValue)
                {
                    var settings = new SerializedObject(Economy);
                    settings.FindProperty("initialGold").intValue = gold.Value;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }

                var map = new RectInt(-24, -18, 48, 36);
                movement = new UnitMovement(map, Footprints(), units, Economy.ProductionClearance());
                Orders = new UnitOrders(movement, actors, Economy);
                Economy.Initialize(movement, actors, target =>
                {
                    Spawned.Add(target.Unit);
                    RegisterUnit(target);
                });
                Construction.Initialize(map, Economy, movement, Orders, Footprints, target =>
                {
                    Constructed.Add(target);
                    obstacles.Add(target.Body);
                    Orders.Register(target);
                    target.Defeated += OnDefeated;
                    movement.UpdateObstacles(Footprints(), reroute: true);
                });
                foreach (CombatTarget target in actors)
                {
                    target.Defeated += OnDefeated;
                }
            }

            private List<Bounds> Footprints() => obstacles
                .Where(obstacle => obstacle != null && obstacle.gameObject.activeInHierarchy)
                .Select(obstacle => obstacle.bounds).Concat(Extra).ToList();

            public PrototypeConstruction.Blueprint Blueprint(BuildingKind kind) =>
                Construction.Blueprints.Single(blueprint => blueprint.Kind == kind);

            public PrototypeUnit AddWorker(Vector3 point)
            {
                var prefab = Economy.Products.Single(product => product.Kind == UnitKind.Worker).Prefab;
                var unit = UnityEngine.Object.Instantiate(prefab, point, Quaternion.identity);
                var target = unit.GetComponent<CombatTarget>();
                target.Initialize();
                RegisterUnit(target);
                return unit;
            }

            private void RegisterUnit(CombatTarget target)
            {
                movement.Register(target.Unit);
                Orders.Register(target);
                target.Defeated += OnDefeated;
            }

            private void OnDefeated(CombatTarget target)
            {
                Orders.Forget(target);
                Economy.OnDefeated(target);
                if (target.Unit != null)
                {
                    movement.Remove(target.Unit);
                    Construction.OnWorkerDefeated(target.Unit);
                }
                else
                {
                    obstacles.Remove(target.Body);
                    Constructed.Remove(target);
                    movement.UpdateObstacles(Footprints());
                }
            }

            public void Tick(int steps)
            {
                for (int index = 0; index < steps; index++)
                {
                    Orders.Tick(0.02f);
                    movement.Tick(0.02f);
                    Economy.Tick(0.02f);
                }
            }

            public void Until(Func<bool> done, int steps)
            {
                for (int index = 0; index < steps && !done(); index++)
                {
                    Tick(1);
                }

                Require(done(), "Construction simulation did not finish in the expected time.");
            }
        }
    }
}
