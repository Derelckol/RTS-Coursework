using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public sealed class UnitMovement
    {
        private const float UnitGap = 0.05f;
        private const float RetryDelay = 0.35f;
        private const float RetryInterval = 0.65f;
        private const int MaxReplans = 3;
        private const float StopDelay = 8f;

        private GridNavigation navigation;
        private readonly RectInt mapBounds;
        private readonly float clearance;
        private readonly List<PrototypeUnit> units;
        private readonly List<Vector3> route = new List<Vector3>();
        private List<Bounds> obstacles;

        public UnitMovement(RectInt mapBounds, IEnumerable<Bounds> obstacles, PrototypeUnit[] units,
            float minimumClearance = 0f)
        {
            this.units = new List<PrototypeUnit>(units);
            this.mapBounds = mapBounds;
            clearance = minimumClearance;
            Physics.SyncTransforms();
            foreach (PrototypeUnit unit in units)
            {
                unit.MeasureRadius();
                clearance = Mathf.Max(clearance, unit.Radius);
            }

            UpdateObstacles(obstacles);
        }

        public void Register(PrototypeUnit unit)
        {
            unit.MeasureRadius();
            units.Add(unit);
        }

        public void Remove(PrototypeUnit unit) => units.Remove(unit);

        public bool TryFindSpawn(Vector3 origin, float radius, Predicate<Vector3> nearBuilding, out Vector3 point,
            float searchRadius = float.PositiveInfinity)
        {
            foreach (Vector3 candidate in navigation.DestinationsNear(origin, true, searchRadius))
            {
                if (nearBuilding(candidate) && navigation.HasClearSegment(candidate, candidate) &&
                    DestinationAvailable(null, candidate, radius))
                {
                    point = candidate;
                    return true;
                }
            }

            point = default;
            return false;
        }

        public void UpdateObstacles(IEnumerable<Bounds> obstacles, bool reroute = false)
        {
            this.obstacles = new List<Bounds>(obstacles);
            navigation = new GridNavigation(mapBounds, this.obstacles, clearance);
            if (!reroute)
            {
                return;
            }

            foreach (PrototypeUnit unit in units)
            {
                if (unit == null || !unit.IsAvailable || !unit.IsMoving || unit.RouteIsClear(navigation))
                {
                    continue;
                }

                Vector3 destination = unit.Destination;
                if (navigation.TryFindRoute(unit.transform.position, destination, route))
                {
                    unit.ReplaceRoute(route);
                }
                else if (navigation.HasClearSegment(destination, destination))
                {
                    unit.Stop();
                }
                else
                {
                    unit.Stop();
                    TryMoveNear(unit, destination,
                        point => (GridNavigation.ToPlane(point - destination)).sqrMagnitude <= 36f, 6f);
                }
            }
        }

        public bool HasRoute(PrototypeUnit unit, Vector3 goal) =>
            navigation.TryFindRoute(unit.transform.position, goal, route);

        public bool IsDestinationTraversable(Vector3 point) => navigation.HasClearSegment(point, point);

        internal bool HasShot(Vector3 origin, CombatTarget target, CombatTarget shooter = null)
        {
            Vector3 start = origin;
            Vector3 end = target.Body.bounds.center;
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < .0001f) return true;
            foreach (var hit in Physics.RaycastAll(start, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var actor = hit.collider.GetComponentInParent<CombatTarget>();
                if (actor == target || shooter != null && actor == shooter || actor != null && actor.Unit != null) continue;
                if (actor != null || hit.collider.GetComponent<GoldDeposit>() == null && hit.collider is BoxCollider)
                    return false;
            }
            return true;
        }

        internal bool CanStrike(PrototypeUnit unit, CombatTarget target, Vector3 point) =>
            target.DistanceFrom(point, unit.Radius) <= unit.AttackRange &&
            (unit.Kind != UnitKind.Archer || HasShot(new Vector3(point.x, unit.transform.position.y, point.z), target));

        public bool TryReachAttackPosition(PrototypeUnit unit, CombatTarget target,
            Predicate<Vector3> routeAllowed = null)
        {
            return TryMoveNear(unit, target.transform.position,
                point => CanStrike(unit, target, point),
                target.ApproachRadius(unit.Radius, unit.AttackRange), routeAllowed);
        }

        public bool TryReachDefensePosition(PrototypeUnit unit, CombatTarget target,
            Predicate<Vector3> routeAllowed, bool continuing)
        {
            bool InRange(Vector3 point) => CanStrike(unit, target, point);
            if (continuing && unit.IsMoving && unit.BlockedTime < 0.30f &&
                InRange(unit.Destination) && DestinationAvailable(unit, unit.Destination) &&
                (routeAllowed == null || routeAllowed(unit.Destination)))
            {
                return true;
            }

            Vector3 point = target.transform.position;
            float radius = target.ApproachRadius(unit.Radius, unit.AttackRange);
            var nearby = new UnitClearance(unit, units, radius + Vector3.Distance(unit.transform.position, point) + 4f);
            if (!TryPlanNear(navigation, unit, point, InRange, route, radius, routeAllowed, nearby, 12) &&
                !TryPlanNear(navigation, unit, point, InRange, route, radius, routeAllowed))
            {
                return false;
            }

            if (continuing)
            {
                unit.RefreshCombatRoute(route);
            }
            else
            {
                unit.ReplaceRoute(route);
            }
            return true;
        }

        public CombatTarget TryPlanBreach(PrototypeUnit unit, Vector3 destination, List<CombatTarget> candidates)
        {
            var potential = new List<Vector3>();
            while (candidates.Count > 0)
            {
                // Keep permanent and friendly geometry. A path through this grid is
                // a prerequisite for considering any destruction.
                var passable = new List<Bounds>(obstacles);
                foreach (CombatTarget building in candidates)
                {
                    passable.Remove(building.Body.bounds);
                }
                var relaxed = new GridNavigation(mapBounds, passable, clearance);
                if (!relaxed.TryFindRoute(unit.transform.position, destination, potential))
                {
                    return null;
                }

                CombatTarget first = null;
                Vector3 from = unit.transform.position;
                foreach (Vector3 to in potential)
                {
                    float earliest = float.PositiveInfinity;
                    foreach (CombatTarget building in candidates)
                    {
                        if (GridNavigation.IntersectsObstacle(building.Body.bounds, clearance, from, to,
                                out float entry) && entry < earliest)
                        {
                            earliest = entry;
                            first = building;
                        }
                    }
                    if (first != null)
                    {
                        break;
                    }
                    from = to;
                }

                if (first == null || first.DistanceFrom(unit.transform.position, unit.Radius) <= unit.AttackRange ||
                    TryReachAttackPosition(unit, first))
                {
                    return first;
                }

                // This obstacle has no attack position reachable in the current
                // grid. Keep it blocked while looking for another complete route.
                candidates.Remove(first);
            }

            return null;
        }

        public bool TryPlanConstruction(PrototypeUnit builder, Bounds footprint, List<Bounds> obstacles,
            float workRange, List<Vector3> plannedRoute)
        {
            foreach (PrototypeUnit unit in units)
            {
                if (unit != null && unit.IsAvailable &&
                    CombatTarget.DistanceToFootprint(unit.transform.position, unit.Radius, footprint) <= 0f)
                {
                    return false;
                }
            }

            obstacles.Add(footprint);
            var prospective = new GridNavigation(mapBounds, obstacles, clearance);
            foreach (PrototypeUnit unit in units)
            {
                if (unit != null && unit.IsAvailable &&
                    navigation.HasClearSegment(unit.transform.position, unit.transform.position) &&
                    !prospective.HasClearSegment(unit.transform.position, unit.transform.position))
                {
                    return false;
                }
            }

            var blockers = new UnitClearance(builder, units);
            return TryPlanNear(prospective, builder, footprint.center,
                point => CombatTarget.DistanceToFootprint(point, builder.Radius, footprint) <= workRange,
                plannedRoute, GridNavigation.ToPlane(footprint.extents).magnitude + workRange + builder.Radius,
                blockers: blockers);
        }

        public void IssueOrder(IReadOnlyList<PrototypeUnit> group, Vector3 target,
            Action<PrototypeUnit> onIssued = null)
        {
            if (!navigation.IsWalkable(GridNavigation.CellAt(target)))
            {
                return;
            }

            List<Vector3> candidates = navigation.DestinationsNear(target);
            var ordered = new List<PrototypeUnit>(group);
            Vector2 destination = GridNavigation.ToPlane(target);
            ordered.Sort((left, right) =>
                (GridNavigation.ToPlane(left.transform.position) - destination).sqrMagnitude.CompareTo(
                    (GridNavigation.ToPlane(right.transform.position) - destination).sqrMagnitude));
            foreach (PrototypeUnit unit in ordered)
            {
                foreach (Vector3 candidate in candidates)
                {
                    if (DestinationAvailable(unit, candidate) &&
                        navigation.TryFindRoute(unit.transform.position, candidate, route))
                    {
                        unit.ReplaceRoute(route);
                        onIssued?.Invoke(unit);
                        break;
                    }
                }
            }
        }

        public bool TryMoveNear(PrototypeUnit unit, Vector3 target, Predicate<Vector3> inRange,
            float searchRadius = float.PositiveInfinity, Predicate<Vector3> routeAllowed = null)
        {
            if (!TryPlanNear(navigation, unit, target, inRange, route, searchRadius, routeAllowed))
            {
                return false;
            }

            unit.ReplaceRoute(route);
            return true;
        }

        private bool TryPlanNear(GridNavigation grid, PrototypeUnit unit, Vector3 target,
            Predicate<Vector3> inRange, List<Vector3> plannedRoute, float searchRadius,
            Predicate<Vector3> routeAllowed = null, UnitClearance blockers = default,
            int maxSearches = int.MaxValue)
        {
            foreach (Vector3 candidate in grid.DestinationsNear(target, true, searchRadius))
            {
                if (!inRange(candidate) || !DestinationAvailable(unit, candidate))
                {
                    continue;
                }
                if (maxSearches-- <= 0)
                {
                    return false;
                }
                if (grid.TryFindRoute(unit.transform.position, candidate, plannedRoute, blockers) &&
                    (routeAllowed == null || plannedRoute.TrueForAll(routeAllowed)))
                {
                    return true;
                }
            }

            return false;
        }

        public void Tick(float deltaTime)
        {
            for (int index = 0; index < units.Count; index++)
            {
                PrototypeUnit unit = units[index];
                if (unit == null || !unit.IsAvailable || !unit.IsMoving)
                {
                    continue;
                }

                Vector3 position = unit.transform.position;
                Vector3 next = unit.NextPosition(deltaTime);
                var others = new UnitClearance(unit, units);
                if (others.IsClear(position, next))
                {
                    unit.Advance(next);
                    continue;
                }

                unit.BlockedTime += deltaTime;
                if (unit.BlockedTime >= StopDelay)
                {
                    unit.Stop();
                    continue;
                }

                // Try the existing route every tick, but run the expensive search
                // at most a few times per order. Stagger retries so opposing units
                // do not try to sidestep each other at the same instant.
                float retryAt = RetryDelay + 0.3f * (index % 5) + unit.ReplanAttempts * RetryInterval;
                if (unit.ReplanAttempts < MaxReplans && unit.BlockedTime >= retryAt)
                {
                    unit.ReplanAttempts++;
                    if (navigation.TryFindRoute(position, unit.Destination, route, others))
                    {
                        unit.SetRoute(route);
                    }
                }
            }
        }

        private bool DestinationAvailable(PrototypeUnit unit, Vector3 point, float radius = 0f)
        {
            if (unit != null)
            {
                radius = unit.Radius;
            }
            foreach (PrototypeUnit other in units)
            {
                if (other == null || other == unit || !other.IsAvailable)
                {
                    continue;
                }

                if (TooClose(radius, other, point, other.transform.position) ||
                    (other.IsMoving && TooClose(radius, other, point, other.Destination)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TooClose(float radius, PrototypeUnit other, Vector3 point, Vector3 center)
        {
            float distance = radius + other.Radius + UnitGap;
            return (GridNavigation.ToPlane(point) - GridNavigation.ToPlane(center)).sqrMagnitude < distance * distance;
        }

        public readonly struct UnitClearance
        {
            private readonly PrototypeUnit unit;
            private readonly IReadOnlyList<PrototypeUnit> units;
            private readonly Disc[] snapshot;

            private readonly struct Disc
            {
                public readonly Vector2 center;
                public readonly float radius;
                public Disc(Vector2 center, float radius)
                {
                    this.center = center;
                    this.radius = radius;
                }
            }

            private UnitClearance(Disc[] snapshot)
            {
                unit = null;
                units = null;
                this.snapshot = snapshot;
            }

            // A route search is synchronous. Cache the positions once for all its edge checks.
            internal UnitClearance Freeze()
            {
                if (units == null) return this;
                var circles = new List<Disc>(units.Count);
                foreach (var other in units)
                    if (other != null && other != unit && other.IsAvailable)
                        circles.Add(new Disc(GridNavigation.ToPlane(other.transform.position),
                            unit.Radius + other.Radius + UnitGap));
                return new UnitClearance(circles.ToArray());
            }


            public UnitClearance(PrototypeUnit unit, IReadOnlyList<PrototypeUnit> units,
                float nearbyRadius = float.PositiveInfinity)
            {
                this.unit = unit;
                snapshot = null;
                if (float.IsPositiveInfinity(nearbyRadius))
                {
                    this.units = units;
                    return;
                }
                var nearby = new List<PrototypeUnit>();
                Vector2 origin = GridNavigation.ToPlane(unit.transform.position);
                foreach (PrototypeUnit other in units)
                {
                    if (other != null && other != unit && other.IsAvailable &&
                        (GridNavigation.ToPlane(other.transform.position) - origin).sqrMagnitude <=
                        nearbyRadius * nearbyRadius)
                    {
                        nearby.Add(other);
                    }
                }
                this.units = nearby;
            }

            public bool IsClear(Vector3 start, Vector3 end)
            {
                if (snapshot == null && units == null) return true;
                Vector2 from = GridNavigation.ToPlane(start);
                Vector2 to = GridNavigation.ToPlane(end);
                Vector2 segment = to - from;
                float length = segment.sqrMagnitude;
                if (snapshot != null)
                {
                    Vector2 min = Vector2.Min(from,to), max = Vector2.Max(from,to);
                    foreach (var disc in snapshot)
                    {
                        if (disc.center.x < min.x - disc.radius || disc.center.x > max.x + disc.radius ||
                            disc.center.y < min.y - disc.radius || disc.center.y > max.y + disc.radius) continue;
                        if (Blocks(disc.center - from, segment, length, disc.radius)) return false;
                    }
                    return true;
                }
                foreach (PrototypeUnit other in units)
                {
                    if (other == null || other == unit || !other.IsAvailable) continue;
                    if (Blocks(GridNavigation.ToPlane(other.transform.position) - from, segment, length,
                        unit.Radius + other.Radius + UnitGap)) return false;
                }
                return true;
            }

            private static bool Blocks(Vector2 offset, Vector2 segment, float squaredLength, float distance)
            {
                float fraction = squaredLength > 0f
                    ? Mathf.Clamp01(Vector2.Dot(offset, segment) / squaredLength) : 0f;
                float squaredRadius = distance * distance;
                // If initialized too close, permit only separating motion.
                return (offset - segment * fraction).sqrMagnitude < squaredRadius &&
                    (fraction > 0f || offset.sqrMagnitude >= squaredRadius || squaredLength == 0f);
            }
        }
    }
}
