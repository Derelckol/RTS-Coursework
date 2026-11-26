using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public enum UnitKind { Worker, Infantry, Archer, Cavalry }

    public sealed partial class PrototypeUnit : MonoBehaviour
    {
        [SerializeField] private bool playerOwned = true;
        [SerializeField] private GameObject selectionMarker;
        [SerializeField] private float speed = 5f;
        [SerializeField] private UnitKind kind = UnitKind.Worker;
        [SerializeField, Min(0f)] private float attackDamage = 12f;
        [SerializeField, Min(0f)] private float attackRange = 1.25f;
        [SerializeField, Min(0.05f)] private float attackInterval = 0.8f;
        [SerializeField, Min(0f)] private float detectionRange = 5f;
        [SerializeField, Min(0f)] private float repairAmount = 12f;
        [SerializeField, Min(0f)] private float repairRange = 1.3f;
        [SerializeField, Min(0.05f)] private float repairInterval = 0.45f;

        private readonly Queue<Vector3> waypoints = new Queue<Vector3>();

        internal event System.Action CombatMotion;
        internal void SignalCombatMotion() => CombatMotion?.Invoke();

        public bool PlayerOwned => playerOwned;
        public Side Team => playerOwned ? Side.Player : Side.Opponent;
        public void SetTeam(Side side, Material material)
        {
            playerOwned = side == Side.Player;
            if (material != null)
            {
                GetComponent<Renderer>().sharedMaterial = material;
            }
        }
        public UnitKind Kind => kind;
        public bool CanAttack => kind != UnitKind.Worker && attackDamage > 0f;
        public float Damage => attackDamage;
        public float AttackRange => attackRange;
        public float MoveSpeed => speed;
        public float AttackInterval => attackInterval;
        public float DetectionRange => detectionRange;
        public float RepairAmount => repairAmount;
        public float RepairRange => repairRange;
        public float RepairInterval => repairInterval;
        internal bool IsAvailable => gameObject.activeInHierarchy;
        public int RemainingWaypoints => waypoints.Count;
        public bool IsMoving => waypoints.Count > 0;
        public Vector3 Destination { get; private set; }
        public float Radius { get; private set; }
        public int CarriedGold { get; internal set; }
        internal float PlanarRadius
        {
            get
            {
                Vector3 scale = transform.lossyScale;
                return GetComponent<CapsuleCollider>().radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            }
        }
        internal float GroundedHeight
        {
            get
            {
                CapsuleCollider body = GetComponent<CapsuleCollider>();
                float verticalScale = Mathf.Abs(transform.lossyScale.y);
                return Mathf.Max(body.height * verticalScale * 0.5f, PlanarRadius) -
                    body.center.y * verticalScale;
            }
        }
        internal float BlockedTime { get; set; }
        internal int ReplanAttempts { get; set; }

        internal void MeasureRadius()
        {
            Radius = PlanarRadius;
        }

        public void SetSelected(bool selected)
        {
            selectionMarker.SetActive(selected);
        }

        public void ReplaceRoute(IReadOnlyList<Vector3> route)
        {
            SetRoute(route);
            ReplanAttempts = 0;
        }

        internal void SetRoute(IReadOnlyList<Vector3> route)
        {
            waypoints.Clear();
            foreach (Vector3 point in route)
            {
                waypoints.Enqueue(new Vector3(point.x, transform.position.y, point.z));
                Destination = point;
            }

            BlockedTime = 0f;
        }

        internal void RefreshCombatRoute(IReadOnlyList<Vector3> route)
        {
            float previousBlocked = BlockedTime;
            SetRoute(route);
            BlockedTime = previousBlocked;
        }

        internal void Stop()
        {
            waypoints.Clear();
        }

        internal bool RouteIsClear(GridNavigation grid)
        {
            Vector3 from = transform.position;
            foreach (Vector3 to in waypoints)
            {
                if (!grid.HasClearSegment(from, to))
                {
                    return false;
                }
                from = to;
            }

            return true;
        }

        internal Vector3 NextPosition(float deltaTime)
        {
            return Vector3.MoveTowards(transform.position, waypoints.Peek(), speed * deltaTime);
        }

        internal void Advance(Vector3 position)
        {
            transform.position = position;
            if ((position - waypoints.Peek()).sqrMagnitude < 0.00000001f)
            {
                waypoints.Dequeue();
            }

            BlockedTime = 0f;
        }
    }
}
