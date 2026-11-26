using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rts.Editor
{
    internal static class EconomyChecks
    {
        public static void Run()
        {
            string scene = SceneManager.GetActiveScene().path;
            CheckGathering(new Fixture());
            EditorSceneManager.OpenScene(scene);
            CheckMissingTownHall(new Fixture());
            EditorSceneManager.OpenScene(scene);
            CheckProduction(new Fixture());
        }

        private static void CheckGathering(Fixture fixture)
        {
            PrototypeUnit first = fixture.Worker;
            var second = UnityEngine.Object.Instantiate(first, new Vector3(-4.5f, 0.85f, -5.5f),
                Quaternion.identity);
            second.GetComponent<CombatTarget>().Initialize();
            fixture.Register(second.GetComponent<CombatTarget>());
            first.transform.position = new Vector3(-2.5f, 0.85f, -7.5f);
            fixture.Source.Take(fixture.Source.Stock - 35);
            var workers = new[] { first, second };
            int initial = fixture.Economy.Gold;

            fixture.Orders.Gather(workers, fixture.Source);
            fixture.Until(() => first.CarriedGold > 0 && second.CarriedGold > 0, 200);
            Require(fixture.Economy.Gold == initial && first.CarriedGold + second.CarriedGold == 30 &&
                fixture.Source.Stock == 5, "Extraction credited gold early or duplicated stock.");

            fixture.Orders.Move(workers, new Vector3(0.5f, 0, -8.5f));
            fixture.Tick(120);
            Require(fixture.Economy.Gold == initial && first.CarriedGold + second.CarriedGold == 30 &&
                fixture.Source.Stock == 5, "Interrupted mining lost or credited carried gold.");

            fixture.Orders.Gather(workers, fixture.Source);
            fixture.Until(() => fixture.Economy.Gold == initial + 35, 1400);
            fixture.Tick(30);
            Require(fixture.Source.Stock == 0 && first.CarriedGold == 0 && second.CarriedGold == 0 &&
                fixture.Orders.StateOf(first) == UnitState.Idle && fixture.Orders.StateOf(second) == UnitState.Idle &&
                fixture.Economy.Gold == initial + 35, "Workers did not finish the last partial load correctly.");
        }

        private static void CheckMissingTownHall(Fixture fixture)
        {
            PrototypeUnit worker = fixture.Worker;
            worker.transform.position = new Vector3(-2.5f, 0.85f, -7.5f);
            fixture.Orders.Gather(new[] { worker }, fixture.Source);
            fixture.Until(() => worker.CarriedGold > 0, 200);
            int cargo = worker.CarriedGold;
            int initial = fixture.Economy.Gold;
            fixture.TownHall.Damage(fixture.TownHall.MaxHealth);
            fixture.Tick(220);
            Require(worker.CarriedGold == cargo && fixture.Economy.Gold == initial &&
                fixture.Orders.StateOf(worker) == UnitState.Deliver, "Loss of the town hall consumed carried gold.");
        }

        private static void CheckProduction(Fixture fixture)
        {
            PrototypeEconomy economy = fixture.Economy;
            PrototypeEconomy.Product worker = economy.Products.Single(product => product.Kind == UnitKind.Worker);
            PrototypeEconomy.Product archer = economy.Products.Single(product => product.Kind == UnitKind.Archer);
            PrototypeEconomy.Product cavalry = economy.Products.Single(product => product.Kind == UnitKind.Cavalry);
            int initialGold = economy.Gold;
            int initialPopulation = economy.Used;
            Require(initialPopulation == 4 && economy.Maximum == 7 &&
                !economy.Enqueue(fixture.TownHall, archer) && !economy.Enqueue(fixture.Barracks, worker),
                "Invalid building product or starting population.");
            Require(economy.Enqueue(fixture.Barracks, cavalry), "Cavalry order was rejected.");
            int remaining = economy.Gold;
            Require(!economy.Enqueue(fixture.Barracks, archer) && economy.Gold == remaining &&
                economy.Message == "Not enough gold.", "A shortage charged gold or accepted a job.");
            fixture.Barracks.Damage(fixture.Barracks.MaxHealth);
            Require(economy.Reserved == 0 && economy.Gold == initialGold,
                "Destroyed producer did not clear and refund the queue.");

            Require(economy.Enqueue(fixture.TownHall, worker) && economy.Enqueue(fixture.TownHall, worker) &&
                economy.Enqueue(fixture.TownHall, worker), "Worker queue did not accept valid orders.");
            remaining = economy.Gold;
            Require(!economy.Enqueue(fixture.TownHall, worker) && economy.Gold == remaining &&
                economy.Reserved == 3 && economy.Message == "Population limit reached.",
                "The queue accepted population beyond the cap.");

            fixture.BlockExit(true);
            fixture.Tick(350);
            Require(economy.Used == initialPopulation && economy.Reserved == 3 &&
                fixture.Spawned.Count == 0 && economy.QueueSummary(fixture.TownHall).Contains("free exit"),
                "A completed job did not wait for a blocked exit.");
            fixture.BlockExit(false);
            fixture.Until(() => fixture.Spawned.Count == 1, 40);
            Require(economy.Used == 5 && economy.Reserved == 2 &&
                Mathf.Abs(fixture.Spawned[0].transform.position.y - 0.85f) < 0.01f,
                "Opening the exit did not convert one reservation into one grounded unit.");

            fixture.House.Damage(fixture.House.MaxHealth);
            fixture.Tick(250);
            Require(economy.Used == 5 && economy.Maximum == 4 && economy.Reserved == 2 &&
                fixture.Spawned.Count == 1 && !economy.Enqueue(fixture.TownHall, worker) && economy.Gold == remaining,
                "Losing a house removed units, overproduced, or accepted a new job.");

            var owned = fixture.Units.Where(unit => unit.Team == Side.Player).Take(2).ToArray();
            foreach (PrototypeUnit unit in owned)
            {
                CombatTarget target = unit.GetComponent<CombatTarget>();
                target.Damage(target.MaxHealth);
            }
            fixture.Tick(250);
            Require(economy.Used == 4 && economy.Reserved == 1 && fixture.Spawned.Count == 2,
                "Production did not resume once population was available.");
            fixture.TownHall.Damage(fixture.TownHall.MaxHealth);
            Require(economy.Reserved == 0 && economy.Gold == remaining + worker.Cost,
                "Destroying the town hall did not refund the remaining job.");
        }

        private static void Require(bool passed, string reason)
        {
            if (!passed)
            {
                throw new InvalidOperationException(reason);
            }
        }

        private sealed class Fixture
        {
            private readonly CombatTarget[] targets;
            private readonly UnitMovement movement;
            private Collider[] staticObjects;
            public readonly List<PrototypeUnit> Units;
            public readonly List<PrototypeUnit> Spawned = new List<PrototypeUnit>();
            public readonly PrototypeEconomy Economy;
            public readonly UnitOrders Orders;
            public readonly GoldDeposit Source;
            public readonly CombatTarget TownHall;
            public readonly CombatTarget Barracks;
            public readonly CombatTarget House;
            public readonly PrototypeUnit Worker;

            public Fixture()
            {
                targets = UnityEngine.Object.FindObjectsByType<CombatTarget>(FindObjectsSortMode.None)
                    .Where(target => { target.Initialize(); return target.Side == Side.Player; }).ToArray();
                Units = targets.Where(target => target.Unit != null).Select(target => target.Unit).ToList();
                Economy = UnityEngine.Object.FindFirstObjectByType<PrototypeEconomy>();
                Source = UnityEngine.Object.FindFirstObjectByType<GoldDeposit>();
                Source.Initialize();
                TownHall = targets.Single(target => target.Building == BuildingKind.TownHall);
                Barracks = targets.Single(target => target.Building == BuildingKind.Barracks);
                House = targets.Single(target => target.Building == BuildingKind.House);
                Worker = Units.Single(unit => unit.Kind == UnitKind.Worker);
                var controls = UnityEngine.Object.FindFirstObjectByType<PrototypeControls>();
                var obstacles = new UnityEditor.SerializedObject(controls).FindProperty("obstacles");
                staticObjects = Enumerable.Range(0, obstacles.arraySize)
                    .Select(index => (Collider)obstacles.GetArrayElementAtIndex(index).objectReferenceValue).ToArray();
                Physics.SyncTransforms();
                movement = new UnitMovement(new RectInt(-24, -18, 48, 36), Footprints(), Units.ToArray(),
                    Economy.ProductionClearance());
                Orders = new UnitOrders(movement, targets, Economy);
                Economy.Initialize(movement, targets, target =>
                {
                    Spawned.Add(target.Unit);
                    Register(target);
                });
                foreach (CombatTarget target in targets)
                {
                    target.Defeated += OnDefeated;
                }
            }

            private IEnumerable<Bounds> Footprints() => staticObjects
                .Where(body => body != null && body.gameObject.activeInHierarchy).Select(body => body.bounds);

            public void Register(CombatTarget target)
            {
                Units.Add(target.Unit);
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
                    Units.Remove(target.Unit);
                }
                else
                {
                    BlockExit(false);
                }
            }

            public void BlockExit(bool blocked)
            {
                IEnumerable<Bounds> footprints = Footprints();
                if (blocked)
                {
                    footprints = footprints.Append(new Bounds(TownHall.transform.position, new Vector3(14, 2, 14)));
                }
                movement.UpdateObstacles(footprints);
            }

            public void Until(Func<bool> done, int steps)
            {
                for (int iteration = 0; iteration < steps && !done(); iteration++)
                {
                    Tick(1);
                }

                Require(done(), "Economy simulation did not complete within the expected time.");
            }

            public void Tick(int steps)
            {
                for (int iteration = 0; iteration < steps; iteration++)
                {
                    Orders.Tick(0.02f);
                    movement.Tick(0.02f);
                    Economy.Tick(0.02f);
                    for (int left = 0; left < Units.Count; left++)
                    {
                        for (int right = left + 1; right < Units.Count; right++)
                        {
                            PrototypeUnit first = Units[left];
                            PrototypeUnit second = Units[right];
                            float distance = GridNavigation.ToPlane(first.transform.position -
                                second.transform.position).magnitude;
                            Require(distance + 0.0001f >= first.Radius + second.Radius + 0.05f,
                                "Economy units overlapped.");
                        }
                    }
                }
            }
        }
    }
}
