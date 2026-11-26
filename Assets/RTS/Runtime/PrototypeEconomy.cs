using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public sealed partial class PrototypeEconomy : MonoBehaviour
    {
        [Serializable]
        public sealed class Product
        {
            [SerializeField] private PrototypeUnit prefab;
            [SerializeField, Min(0)] private int cost = 25;
            [SerializeField, Min(0.01f)] private float seconds = 3f;

            public PrototypeUnit Prefab => prefab;
            public UnitKind Kind => prefab.Kind;
            public int Cost => cost;
            public float Seconds => seconds;
        }

        private sealed class Job
        {
            public readonly Product Product;
            public float Remaining;
            public float RetryIn;

            public Job(Product product)
            {
                Product = product;
                Remaining = product.Seconds;
            }
        }

        [SerializeField, Min(0)] private int initialGold = 80;
        [SerializeField, Min(0)] private int baseCapacity = 4;
        [SerializeField, Min(0)] private int houseCapacity = 3;
        [SerializeField, Min(1)] private int loadSize = 15;
        [SerializeField, Min(0.01f)] private float gatherSeconds = 0.85f;
        [SerializeField, Min(0.1f)] private float workRange = 1.45f;
        [SerializeField, Min(0.1f)] private float spawnRange = 1.8f;
        [SerializeField] private Product[] products = Array.Empty<Product>();
        [SerializeField] private Side owner;
        [SerializeField] private PrototypeEconomy sharedRules;
        [SerializeField] private Material unitMaterial;

        private readonly Dictionary<CombatTarget, Queue<Job>> queues =
            new Dictionary<CombatTarget, Queue<Job>>();
        private readonly List<CombatTarget> buildings = new List<CombatTarget>();
        private UnitMovement movement;
        private Action<CombatTarget> onSpawn;
        private float messageTime;

        [SerializeField, Min(0)] private int upgradeCost = 40;
        [SerializeField, Min(.1f)] private float upgradeSeconds = 8;
        [SerializeField, Min(1)] private float upgradeHealth = 50;

        public int UpgradeCost => Rules.upgradeCost;
        public float UpgradeSeconds => Rules.upgradeSeconds;

        public bool TryUpgrade(CombatTarget building)
        {
            if (!buildings.Contains(building) || !building.IsAlive || !building.IsOperational ||
                building.Upgraded || building.UpgradeRemaining > 0 || !TrySpend(UpgradeCost)) return false;
            building.RestoreUpgrade(false, UpgradeSeconds, building.NextBuildingAttack);
            return true;
        }

        public int Gold { get; private set; }
        public int Used { get; private set; }
        public int Reserved { get; private set; }
        public int Maximum { get; private set; }
        private PrototypeEconomy Rules => sharedRules != null ? sharedRules : this;
        public Side Owner => owner;
        public int LoadSize => Rules.loadSize;
        public float GatherSeconds => Rules.gatherSeconds;
        public float WorkRange => Rules.workRange;
        public IReadOnlyList<Product> Products => Rules.products;
        public string Message { get; private set; }

        public float ProductionClearance()
        {
            float radius = 0f;
            foreach (Product product in Products)
            {
                radius = Mathf.Max(radius, product.Prefab.PlanarRadius);
            }

            return radius;
        }

        public void Initialize(UnitMovement navigation, IEnumerable<CombatTarget> actors, Action<CombatTarget> register)
        {
            movement = navigation;
            onSpawn = register;
            Gold = Rules.initialGold;
            Used = 0;
            Reserved = 0;
            Maximum = Rules.baseCapacity;
            Message = null;
            messageTime = 0;
            buildings.Clear();
            queues.Clear();
            foreach (CombatTarget actor in actors)
            {
                if (actor.Side != Owner || !actor.IsAlive)
                {
                    continue;
                }

                if (actor.Unit != null)
                {
                    Used++;
                    continue;
                }

                RegisterBuilding(actor);
            }
        }

        public void RegisterBuilding(CombatTarget building)
        {
            if (!building.IsAlive || !building.IsOperational || building.Side != Owner ||
                building.Building == BuildingKind.None || buildings.Contains(building))
            {
                return;
            }

            buildings.Add(building);
            if (building.Building == BuildingKind.House)
            {
                Maximum += Rules.houseCapacity;
            }
            else
            {
                queues.Add(building, new Queue<Job>());
            }
        }

        public bool TrySpend(int amount)
        {
            if (Gold < amount)
            {
                Notify("Not enough gold.");
                return false;
            }

            Gold -= amount;
            return true;
        }

        public bool CanProduce(CombatTarget building, Product product)
        {
            return building != null && building.IsAlive && building.IsOperational && building.Side == Owner &&
                ((building.Building == BuildingKind.TownHall && product.Kind == UnitKind.Worker) ||
                 (building.Building == BuildingKind.Barracks && product.Kind != UnitKind.Worker));
        }

        public bool Enqueue(CombatTarget building, Product product)
        {
            if (!CanProduce(building, product) || !queues.TryGetValue(building, out Queue<Job> queue))
            {
                return false;
            }

            if (Gold < product.Cost)
            {
                Notify("Not enough gold.");
                return false;
            }

            if (Used + Reserved >= Maximum)
            {
                Notify("Population limit reached.");
                return false;
            }

            queue.Enqueue(new Job(product));
            Gold -= product.Cost;
            Reserved++;
            return true;
        }

        public void Deposit(PrototypeUnit unit)
        {
            if (unit.Team != Owner)
            {
                return;
            }
            Gold += unit.CarriedGold;
            unit.CarriedGold = 0;
        }

        public CombatTarget FindTownHall(PrototypeUnit worker)
        {
            CombatTarget closest = null;
            float best = float.PositiveInfinity;
            foreach (CombatTarget building in buildings)
            {
                if (!building.IsAlive || building.Building != BuildingKind.TownHall)
                {
                    continue;
                }

                float distance = (building.transform.position - worker.transform.position).sqrMagnitude;
                if (distance < best)
                {
                    closest = building;
                    best = distance;
                }
            }

            return closest;
        }

        public void OnDefeated(CombatTarget actor)
        {
            if (actor.Side != Owner)
            {
                return;
            }

            if (actor.Unit != null)
            {
                Used--;
                return;
            }

            if (!buildings.Remove(actor))
            {
                return;
            }
            if (actor.Building == BuildingKind.House)
            {
                Maximum -= Rules.houseCapacity;
            }

            if (queues.Remove(actor, out Queue<Job> queue))
            {
                Reserved -= queue.Count;
                foreach (Job job in queue)
                {
                    Gold += job.Product.Cost;
                }
            }
        }

        public int Pending(UnitKind kind)
        {
            int total = 0;
            foreach (Queue<Job> queue in queues.Values)
            {
                foreach (Job job in queue)
                {
                    if (job.Product.Kind == kind)
                    {
                        total++;
                    }
                }
            }
            return total;
        }

        public int PendingAt(CombatTarget building) =>
            queues.TryGetValue(building, out Queue<Job> queue) ? queue.Count : 0;

        public bool IsExitBlocked(CombatTarget building) =>
            queues.TryGetValue(building, out Queue<Job> queue) && queue.Count > 0 && Used < Maximum &&
            queue.Peek().Remaining <= 0f && queue.Peek().RetryIn > 0f;

        public string QueueSummary(CombatTarget building, bool localized = false)
        {
            if (building == null || !queues.TryGetValue(building, out Queue<Job> queue) || queue.Count == 0)
            {
                return string.Empty;
            }

            Job job = queue.Peek();
            if (localized)
            {
                string remaining = job.Remaining > 0 ? $"{job.Remaining:F1} с" :
                    Used >= Maximum ? "ліміт населення" : "вихід зайнято";
                return $"Черга: {queue.Count} · {HudPresentation.Name(job.Product.Kind)} · {remaining}";
            }
            string progress = job.Remaining > 0 ? $"{job.Remaining:F1}s" :
                Used >= Maximum ? "waiting for population" : "waiting for a free exit";
            return $"Queue {queue.Count} · {job.Product.Kind} · {progress}";
        }

        public void Tick(float deltaTime)
        {
            foreach (var building in buildings) building.TickUpgrade(deltaTime, Rules.upgradeHealth);
            if (messageTime > 0f)
            {
                messageTime -= deltaTime;
                if (messageTime <= 0f)
                {
                    Message = null;
                }
            }

            foreach (var entry in queues)
            {
                Queue<Job> queue = entry.Value;
                if (queue.Count == 0)
                {
                    continue;
                }

                Job job = queue.Peek();
                job.Remaining = Mathf.Max(0f, job.Remaining - deltaTime);
                if (job.Remaining > 0f || Used >= Maximum)
                {
                    continue;
                }

                job.RetryIn -= deltaTime;
                if (job.RetryIn > 0f)
                {
                    continue;
                }

                CombatTarget building = entry.Key;
                PrototypeUnit prefab = job.Product.Prefab;
                float radius = prefab.PlanarRadius;
                if (!movement.TryFindSpawn(building.transform.position, radius,
                        point => building.DistanceFrom(point, radius) <= Rules.spawnRange, out Vector3 location,
                        building.ApproachRadius(radius, Rules.spawnRange)))
                {
                    job.RetryIn = 0.25f;
                    continue;
                }

                location.y = prefab.GroundedHeight;
                PrototypeUnit unit = Instantiate(prefab, location, prefab.transform.rotation);
                unit.SetTeam(Owner, unitMaterial);
                var target = unit.GetComponent<CombatTarget>();
                if (!Application.isPlaying)
                {
                    target.Initialize();
                }

                queue.Dequeue();
                Reserved--;
                Used++;
                onSpawn(target);
            }
        }

        public void Notify(string message)
        {
            Message = message;
            messageTime = 4f;
        }
    }
}
