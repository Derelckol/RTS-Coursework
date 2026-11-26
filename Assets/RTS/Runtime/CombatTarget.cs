using System;
using UnityEngine;

namespace Rts
{
    public enum Side { Player, Opponent }
    public enum BuildingKind { None, TownHall, Barracks, House }

    [DefaultExecutionOrder(-10)]
    public sealed partial class CombatTarget : MonoBehaviour
    {
        [SerializeField] private Side side;
        [SerializeField] private BuildingKind building;
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField, Range(0.01f, 1f)] private float initialHealthFraction = 1f;

        public bool Upgraded { get; private set; }
        public float UpgradeRemaining { get; private set; }
        internal float NextBuildingAttack { get; set; }

        internal void RestoreUpgrade(bool upgraded, float remaining, float nextAttack)
        {
            Upgraded = upgraded;
            UpgradeRemaining = remaining;
            NextBuildingAttack = nextAttack;
        }

        internal void TickUpgrade(float deltaTime, float healthBonus)
        {
            if (UpgradeRemaining <= 0) return;
            UpgradeRemaining = Mathf.Max(0, UpgradeRemaining - deltaTime);
            if (UpgradeRemaining > 0) return;
            Upgraded = true;
            maxHealth += healthBonus;
            Health += healthBonus;
        }

        public Side Side => Unit != null ? Unit.Team : side;
        public BuildingKind Building => building;
        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsAlive => Health > 0f && gameObject.activeInHierarchy;
        public ConstructionSite Site { get; private set; }
        public bool IsOperational => Site == null || Site.Completed;
        public Collider Body { get; private set; }
        public PrototypeUnit Unit { get; private set; }

        public event Action<CombatTarget> Defeated;

        private void Awake() => Initialize();

        public void InitializeConstruction(BuildingKind kind, Side owner, float health, ConstructionSite site)
        {
            side = owner;
            building = kind;
            maxHealth = health;
            initialHealthFraction = 1f;
            Site = site;
            Initialize();
        }

        public void Initialize()
        {
            Body = GetComponent<Collider>();
            Unit = GetComponent<PrototypeUnit>();
            Health = maxHealth * initialHealthFraction;
        }

        public void Damage(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return;
            }

            Health = Mathf.Max(0f, Health - amount);
            if (Health == 0f)
            {
                gameObject.SetActive(false);
                Defeated?.Invoke(this);
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
            }
        }

        public void Repair(float amount)
        {
            if (IsAlive && building != BuildingKind.None && amount > 0f)
            {
                Health = Mathf.Min(maxHealth, Health + amount);
            }
        }

        public float DistanceFrom(Vector3 origin, float unitRadius)
        {
            Vector2 point = GridNavigation.ToPlane(origin);
            if (Unit != null)
            {
                return Mathf.Max(0f,
                    Vector2.Distance(point, GridNavigation.ToPlane(transform.position)) - unitRadius - Unit.Radius);
            }

            return DistanceToFootprint(origin, unitRadius, Body.bounds);
        }

        internal float ApproachRadius(float radius, float range) =>
            (Unit != null ? Unit.Radius : GridNavigation.ToPlane(Body.bounds.extents).magnitude) + radius + range;

        internal static float DistanceToFootprint(Vector3 origin, float radius, Bounds bounds)
        {
            Vector2 point = GridNavigation.ToPlane(origin);
            var closest = new Vector2(Mathf.Clamp(point.x, bounds.min.x, bounds.max.x),
                Mathf.Clamp(point.y, bounds.min.z, bounds.max.z));
            return Mathf.Max(0f, Vector2.Distance(point, closest) - radius);
        }

        public string DisplayName => Unit != null ? Unit.Kind.ToString() : building switch
        {
            BuildingKind.TownHall => "Town Hall",
            _ => building.ToString()
        };
    }
}
