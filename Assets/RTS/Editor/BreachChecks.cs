using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rts.Editor
{
    internal static class BreachChecks
    {
        private static readonly Vector3 Start = new Vector3(2.5f, 0.85f, 4.5f);
        private static readonly Vector3 Goal = new Vector3(18.5f, 0f, 4.5f);

        public static void Run()
        {
            using (var lane = new Lane())
            {
                CombatTarget partial = lane.Building(7.5f, Side.Opponent, width: 4f);
                lane.Initialize();
                lane.Move(Goal);
                Require(lane.State == UnitState.Move, "An ordinary detour was not preferred.");
                lane.Until(() => lane.At(Goal), 400);
                Require(partial.Health == partial.MaxHealth, "A building on an avoidable route was attacked.");
            }

            using (var lane = new Lane())
            {
                CombatTarget nearDecoy = lane.Building(3.5f, Side.Opponent, width: 1f, z: 7.5f);
                CombatTarget first = lane.Building(7.5f, Side.Opponent);
                CombatTarget second = lane.Building(13.5f, Side.Opponent);
                lane.Initialize();
                lane.Move(Goal);
                Require(lane.State == UnitState.Breach && lane.IsAttacking(first) && !lane.IsAttacking(nearDecoy),
                    "The breach targeted a nearby decoy rather than the blocker on the route.");
                lane.Until(() => !first.IsAlive, 500);
                lane.Until(() => lane.IsAttacking(second), 60);
                // A different unit can remove a barrier before the attacker gets there.
                second.Damage(second.MaxHealth);
                lane.Until(() => lane.At(Goal), 500);
                Require(nearDecoy.Health == nearDecoy.MaxHealth, "Resuming the original route hit a decoy.");
            }

            using (var lane = new Lane())
            {
                CombatTarget first = lane.Building(7.5f, Side.Opponent);
                CombatTarget second = lane.Building(13.5f, Side.Opponent);
                lane.Initialize();
                lane.Move(Goal);
                lane.Until(() => !first.IsAlive && !second.IsAlive && lane.At(Goal), 1000);
            }

            using (var lane = new Lane())
            {
                CombatTarget enemy = lane.Building(7.5f, Side.Opponent);
                lane.Permanent(13.5f);
                lane.Initialize();
                lane.Move(Goal);
                lane.Tick(200);
                Require(lane.State == UnitState.Idle && enemy.Health == enemy.MaxHealth && lane.HasDiagnostic,
                    "A permanent obstruction caused an endless search or unnecessary attack.");
            }

            using (var lane = new Lane())
            {
                CombatTarget ally = lane.Building(7.5f, Side.Player);
                lane.Initialize();
                lane.Move(Goal);
                Require(lane.State == UnitState.Idle && ally.Health == ally.MaxHealth,
                    "An allied obstruction was treated as destructible geometry.");
            }

            using (var lane = new Lane(UnitKind.Worker))
            {
                CombatTarget enemy = lane.Building(7.5f, Side.Opponent);
                lane.Initialize();
                lane.Move(Goal);
                lane.Tick(100);
                Require(lane.State == UnitState.Idle && enemy.Health == enemy.MaxHealth,
                    "A worker attacked a barrier.");
            }

            using (var lane = new Lane())
            {
                CombatTarget enemy = lane.Building(7.5f, Side.Opponent);
                lane.Initialize();
                lane.Move(Goal);
                Require(lane.State == UnitState.Breach, "Expected an active breach.");
                var retreat = new Vector3(1.5f, 0f, 1.5f);
                lane.Move(retreat);
                lane.Until(() => lane.At(retreat), 200);
                Require(enemy.Health == enemy.MaxHealth && !lane.IsAttacking(enemy),
                    "A retreat did not cancel breaching.");
            }

            using (var lane = new Lane())
            {
                CombatTarget enemy = lane.Building(7.5f, Side.Opponent);
                lane.Initialize();
                lane.Move(Goal);
                lane.Stop();
                lane.Tick(200);
                Require(lane.State == UnitState.Idle && enemy.Health == enemy.MaxHealth,
                    "A stop order did not cancel breaching.");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Lane : IDisposable
        {
            private readonly List<GameObject> objects = new List<GameObject>();
            private readonly List<Collider> solids = new List<Collider>();
            private readonly List<CombatTarget> targets = new List<CombatTarget>();
            private readonly PrototypeUnit unit;
            private UnitMovement movement;
            private UnitOrders orders;

            public Lane(UnitKind kind = UnitKind.Infantry)
            {
                var actor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                objects.Add(actor);
                actor.transform.position = Start;
                actor.transform.localScale = new Vector3(0.8f, 0.85f, 0.8f);
                unit = actor.AddComponent<PrototypeUnit>();
                var settings = new SerializedObject(unit);
                settings.FindProperty("kind").enumValueIndex = (int)kind;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var combat = actor.AddComponent<CombatTarget>();
                combat.Initialize();
                targets.Add(combat);
            }

            public UnitState State => orders.StateOf(unit);
            public bool HasDiagnostic => !string.IsNullOrEmpty(orders.LastTacticalIssue);
            public bool IsAttacking(CombatTarget target) => orders.IsAttacking(unit, target);
            public bool At(Vector3 point) => GridNavigation.ToPlane(unit.transform.position - point).sqrMagnitude < 0.1f;
            public void Move(Vector3 goal) => orders.Move(new[] { unit }, goal);
            public void Stop() => orders.Halt(unit);

            public CombatTarget Building(float x, Side side, float width = 8f, float z = 4f)
            {
                GameObject box = Box(x, z, width);
                var target = box.AddComponent<CombatTarget>();
                var fields = new SerializedObject(target);
                fields.FindProperty("side").enumValueIndex = (int)side;
                fields.FindProperty("building").enumValueIndex = (int)BuildingKind.House;
                fields.FindProperty("maxHealth").floatValue = 24f;
                fields.ApplyModifiedPropertiesWithoutUndo();
                target.Initialize();
                targets.Add(target);
                return target;
            }

            public void Permanent(float x) => Box(x, 4f, 8f);

            private GameObject Box(float x, float z, float width)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                objects.Add(box);
                box.transform.position = new Vector3(x, 1f, z);
                box.transform.localScale = new Vector3(2f, 2f, width);
                solids.Add(box.GetComponent<Collider>());
                return box;
            }

            private List<Bounds> Bounds()
            {
                var footprints = new List<Bounds>();
                foreach (Collider body in solids)
                    if (body.gameObject.activeSelf) footprints.Add(body.bounds);
                return footprints;
            }

            public void Initialize()
            {
                Physics.SyncTransforms();
                var training = new GameObject("Breach test rules").AddComponent<PrototypeMatch>();
                objects.Add(training.gameObject);
                movement = new UnitMovement(new RectInt(0, 0, 22, 8), Bounds(), new[] { unit });
                orders = new UnitOrders(movement, targets.ToArray(), training: training);
                foreach (CombatTarget actor in targets)
                    actor.Defeated += Remove;
            }

            private void Remove(CombatTarget actor)
            {
                orders.Forget(actor);
                movement.UpdateObstacles(Bounds());
            }

            public void Tick(int count)
            {
                for (int step = 0; step < count; step++)
                {
                    orders.Tick(0.02f);
                    movement.Tick(0.02f);
                }
            }

            public void Until(Func<bool> reached, int steps)
            {
                for (int step = 0; step < steps && !reached(); step++) Tick(1);
                Require(reached(), $"Breach simulation timed out in {State}. {orders.LastTacticalIssue}");
            }

            public void Dispose()
            {
                foreach (GameObject item in objects)
                    if (item != null) UnityEngine.Object.DestroyImmediate(item);
            }
        }
    }
}
