using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Rts.Editor
{
    internal static class CombatChecks
    {
        public static void Run()
        {
            var targets = UnityEngine.Object.FindObjectsByType<CombatTarget>(FindObjectsSortMode.None);
            foreach (CombatTarget target in targets)
            {
                target.Initialize();
            }

            var worker = FindUnit(targets, UnitKind.Worker);
            var infantry = FindUnit(targets, UnitKind.Infantry);
            var archer = FindUnit(targets, UnitKind.Archer);
            var cavalry = FindUnit(targets, UnitKind.Cavalry);
            var enemy = targets.Single(target => target.Unit != null && target.Side == Side.Opponent);
            var townHall = targets.Single(target => target.Building == BuildingKind.TownHall);
            var barracks = targets.Single(target => target.Building == BuildingKind.Barracks);
            var house = targets.Single(target => target.Building == BuildingKind.House);
            var controls = UnityEngine.Object.FindFirstObjectByType<PrototypeControls>();
            var fields = new SerializedObject(controls);
            Require(fields.FindProperty("combatTargets").arraySize == 8 &&
                fields.FindProperty("obstacles").arraySize == 6 &&
                fields.FindProperty("units").arraySize == 5, "Combat scene references are incomplete.");
            Require(!worker.CanAttack && infantry.CanAttack && archer.AttackRange > infantry.AttackRange &&
                Speed(cavalry) > Speed(infantry), "Unit roles are configured incorrectly.");
            Require(townHall.Side == Side.Player && house.Side == Side.Player && barracks.Side == Side.Opponent &&
                townHall.Health < townHall.MaxHealth, "Building setup is incorrect.");

            Place(worker, -8.5f, 1.5f);
            var repair = new Scenario(worker, townHall);
            repair.Orders.Interact(new[] { worker }, townHall);
            Require(repair.Orders.StateOf(worker) == UnitState.Repair, "Repair order was not accepted.");
            repair.Tick(100);
            Require(townHall.Health > townHall.MaxHealth * 0.5f && townHall.Health < townHall.MaxHealth,
                "Worker did not approach and repair over time.");
            repair.Orders.Move(new[] { worker }, new Vector3(-7.5f, 0, 3.5f));
            float interruptedHealth = townHall.Health;
            repair.Tick(70);
            Require(townHall.Health == interruptedHealth, "Movement did not interrupt repair.");
            repair.Orders.Interact(new[] { worker }, townHall);
            repair.Tick(450);
            Require(townHall.Health == townHall.MaxHealth && repair.Orders.StateOf(worker) == UnitState.Idle,
                "Repair did not stop at full health.");
            repair.Dispose();

            Place(infantry, 5.5f, 1.5f);
            Place(archer, 6.5f, -0.5f);
            Place(worker, 8.5f, -1.5f);
            var siege = new Scenario(infantry, archer, worker, barracks, house);
            siege.Orders.Interact(new[] { worker }, barracks);
            siege.Orders.Interact(new[] { infantry }, house);
            Require(siege.Orders.StateOf(worker) == UnitState.Idle && siege.Orders.StateOf(infantry) == UnitState.Idle,
                "A worker or friendly attack was accepted.");
            siege.Orders.Interact(new[] { infantry, archer }, barracks);
            siege.Tick(1);
            float health = barracks.Health;
            siege.Tick(5);
            Require(health < barracks.MaxHealth && barracks.Health == health, "Attack interval was not respected.");
            siege.Orders.Move(new[] { archer }, new Vector3(7.5f, 0, -2.5f));
            Require(siege.Orders.StateOf(archer) == UnitState.Move, "Movement did not interrupt attack.");
            siege.Tick(500, infantry, archer, worker);
            Require(!barracks.IsAlive && !barracks.gameObject.activeSelf &&
                siege.Orders.StateOf(infantry) == UnitState.Idle && !infantry.IsMoving,
                $"Building defeat did not clear attacker orders. HP {barracks.Health}, " +
                $"infantry {infantry.transform.position}, state {siege.Orders.StateOf(infantry)}.");
            siege.Orders.Move(new[] { infantry }, barracks.transform.position);
            siege.Tick(150);
            Require(Vector3.Distance(infantry.transform.position, new Vector3(12.5f, 0.85f, 1.5f)) < 0.01f,
                "Destroyed building still blocked navigation.");
            siege.Dispose();

            Place(cavalry, -11.5f, -4.5f);
            Place(enemy.Unit, -5.5f, -4.5f);
            var chase = new Scenario(cavalry, enemy);
            chase.Orders.Move(new[] { enemy.Unit }, new Vector3(3.5f, 0, -4.5f));
            chase.Orders.Interact(new[] { cavalry }, enemy);
            chase.Tick(125, cavalry, enemy.Unit);
            Require(enemy.Unit.transform.position.x > -3.5f && enemy.Health < enemy.MaxHealth,
                "Cavalry did not hit the moving target while pursuing.");
            chase.Tick(600, cavalry, enemy.Unit);
            Require(!enemy.IsAlive && !enemy.gameObject.activeSelf &&
                chase.Orders.StateOf(cavalry) == UnitState.Idle && !cavalry.IsMoving,
                "Unit defeat did not clear attacker orders.");
            var defeatedUnit = enemy.Unit;
            Vector3 freed = enemy.transform.position;
            chase.Orders.Move(new[] { cavalry }, freed);
            chase.Tick(100);
            Require((GridNavigation.ToPlane(cavalry.transform.position - freed)).sqrMagnitude < 0.01f &&
                chase.Orders.StateOf(defeatedUnit) == UnitState.Idle, "A defeated unit retained movement occupancy.");
            chase.Dispose();

            enemy.gameObject.SetActive(true);
            enemy.Initialize();
            Place(enemy.Unit, 5.5f, -4.5f);
            Place(worker, 2.5f, -4.5f);
            float workerHealth = worker.GetComponent<CombatTarget>().Health;
            var ai = new Scenario(worker, enemy);
            ai.Tick(100, worker, enemy.Unit);
            Require(ai.Orders.StateOf(enemy.Unit) == UnitState.Attack &&
                worker.GetComponent<CombatTarget>().Health < workerHealth,
                "Enemy AI did not acquire and attack a nearby player target.");
            ai.Dispose();
        }

        private static PrototypeUnit FindUnit(CombatTarget[] targets, UnitKind kind)
        {
            return targets.Single(target => target.Unit != null && target.Side == Side.Player &&
                target.Unit.Kind == kind).Unit;
        }

        private static float Speed(PrototypeUnit unit) =>
            new SerializedObject(unit).FindProperty("speed").floatValue;

        private static void Place(PrototypeUnit unit, float x, float z)
        {
            unit.transform.position = new Vector3(x, 0.85f, z);
        }

        private static void Require(bool passed, string reason)
        {
            if (!passed)
            {
                throw new InvalidOperationException(reason);
            }
        }

        private sealed class Scenario
        {
            private readonly CombatTarget[] targets;
            private readonly UnitMovement movement;
            public UnitOrders Orders { get; }

            public Scenario(params Component[] participants)
            {
                targets = participants.Select(participant => participant.GetComponent<CombatTarget>()).ToArray();
                Physics.SyncTransforms();
                movement = new UnitMovement(new RectInt(-24, -18, 48, 36), Footprints(),
                    targets.Where(target => target.Unit != null).Select(target => target.Unit).ToArray());
                Orders = new UnitOrders(movement, targets);
                foreach (CombatTarget actor in targets)
                {
                    actor.Defeated += Defeated;
                }
            }

            public void Tick(int steps, params PrototypeUnit[] verifySpacing)
            {
                for (int index = 0; index < steps; index++)
                {
                    Orders.Tick(0.02f);
                    movement.Tick(0.02f);
                    for (int left = 0; left < verifySpacing.Length; left++)
                    {
                        for (int right = left + 1; right < verifySpacing.Length; right++)
                        {
                            var first = verifySpacing[left];
                            var second = verifySpacing[right];
                            if (first.gameObject.activeSelf && second.gameObject.activeSelf)
                            {
                                Vector2 delta = GridNavigation.ToPlane(first.transform.position - second.transform.position);
                                Require(delta.magnitude + 0.0001f >= first.Radius + second.Radius + 0.05f,
                                    "Combat units overlapped.");
                            }
                        }
                    }
                }
            }

            private Bounds[] Footprints() => targets
                .Where(target => target.Building != BuildingKind.None && target.IsAlive)
                .Select(target => target.Body.bounds).ToArray();

            private void Defeated(CombatTarget target)
            {
                Orders.Forget(target);
                if (target.Building != BuildingKind.None)
                {
                    movement.UpdateObstacles(Footprints());
                }
            }

            public void Dispose()
            {
                foreach (CombatTarget target in targets)
                {
                    target.Defeated -= Defeated;
                }
            }
        }
    }
}
