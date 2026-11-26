using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public sealed class GridNavigation
    {
        private const float DiagonalCost = 1.41421356f;
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down,
            new Vector2Int(1, 1), new Vector2Int(-1, 1),
            new Vector2Int(-1, -1), new Vector2Int(1, -1)
        };

        private readonly RectInt bounds;
        private readonly float clearance;
        private readonly List<Rect> footprints = new List<Rect>();
        private readonly HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();

        public GridNavigation(RectInt bounds, IEnumerable<Bounds> obstacles, float clearance)
        {
            this.bounds = bounds;
            this.clearance = clearance;
            foreach (Bounds obstacle in obstacles)
            {
                // Round the inflated obstacle out to cell edges. Search and smoothing
                // use the same conservative clearance, including at cell corners.
                Rect footprint = Footprint(obstacle, clearance);
                footprints.Add(footprint);
                for (int x = Mathf.Max(bounds.xMin, (int)footprint.xMin);
                    x < Mathf.Min(bounds.xMax, (int)footprint.xMax); x++)
                {
                    for (int z = Mathf.Max(bounds.yMin, (int)footprint.yMin);
                        z < Mathf.Min(bounds.yMax, (int)footprint.yMax); z++)
                    {
                        blocked.Add(new Vector2Int(x, z));
                    }
                }
            }
        }

        private static Rect Footprint(Bounds obstacle, float clearance) => Rect.MinMaxRect(
            Mathf.Floor(obstacle.min.x - clearance), Mathf.Floor(obstacle.min.z - clearance),
            Mathf.Ceil(obstacle.max.x + clearance), Mathf.Ceil(obstacle.max.z + clearance));

        internal static bool IntersectsObstacle(Bounds obstacle, float clearance, Vector3 start, Vector3 end,
            out float entry)
        {
            Rect rect = Footprint(obstacle, clearance);
            Vector2 from = ToPlane(start);
            Vector2 delta = ToPlane(end) - from;
            entry = 0f;
            float exit = 1f;
            return ClipAxis(from.x, delta.x, rect.xMin, rect.xMax, ref entry, ref exit) &&
                ClipAxis(from.y, delta.y, rect.yMin, rect.yMax, ref entry, ref exit);
        }

        public static Vector2 ToPlane(Vector3 point) => new Vector2(point.x, point.z);

        public static Vector2Int CellAt(Vector3 point)
        {
            return new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.z));
        }

        public static Vector3 Center(Vector2Int cell)
        {
            return new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f);
        }

        public bool IsWalkable(Vector2Int cell)
        {
            return bounds.Contains(cell) && !blocked.Contains(cell);
        }

        public List<Vector3> DestinationsNear(Vector3 target, bool includeCellEdges = false,
            float searchRadius = float.PositiveInfinity)
        {
            var points = new List<Vector3>();
            RectInt search = bounds;
            if (!float.IsPositiveInfinity(searchRadius))
            {
                int xMin = Mathf.Max(bounds.xMin, Mathf.FloorToInt(target.x - searchRadius));
                int zMin = Mathf.Max(bounds.yMin, Mathf.FloorToInt(target.z - searchRadius));
                int xMax = Mathf.Min(bounds.xMax, Mathf.CeilToInt(target.x + searchRadius));
                int zMax = Mathf.Min(bounds.yMax, Mathf.CeilToInt(target.z + searchRadius));
                search = new RectInt(xMin, zMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, zMax - zMin));
            }
            foreach (Vector2Int cell in search.allPositionsWithin)
            {
                if (IsWalkable(cell))
                {
                    points.Add(Center(cell));
                    if (includeCellEdges)
                    {
                        // A melee position can fit inside a free cell even when its center is out of range.
                        const float inset = 0.03f;
                        points.Add(new Vector3(
                            Mathf.Clamp(target.x, cell.x + inset, cell.x + 1f - inset), 0f,
                            Mathf.Clamp(target.z, cell.y + inset, cell.y + 1f - inset)));
                    }
                }
            }

            Vector2 origin = ToPlane(target);
            points.Sort((left, right) =>
            {
                int distance = (ToPlane(left) - origin).sqrMagnitude.CompareTo((ToPlane(right) - origin).sqrMagnitude);
                if (distance != 0)
                {
                    return distance;
                }

                int column = left.x.CompareTo(right.x);
                return column != 0 ? column : left.z.CompareTo(right.z);
            });
            points.Insert(0, target);
            return points;
        }

        public bool HasClearSegment(Vector3 start, Vector3 end)
        {
            if (!IsSafePoint(start) || !IsSafePoint(end))
            {
                return false;
            }

            Vector2 from = ToPlane(start);
            Vector2 delta = ToPlane(end) - from;
            foreach (Rect footprint in footprints)
            {
                float enter = 0f;
                float exit = 1f;
                if (ClipAxis(from.x, delta.x, footprint.xMin, footprint.xMax, ref enter, ref exit) &&
                    ClipAxis(from.y, delta.y, footprint.yMin, footprint.yMax, ref enter, ref exit))
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryFindRoute(Vector3 start, Vector3 target, List<Vector3> route,
            UnitMovement.UnitClearance units = default)
        {
            route.Clear();
            Vector2Int startCell = CellAt(start);
            Vector2Int targetCell = CellAt(target);
            if (!IsSafePoint(start) || !IsSafePoint(target))
            {
                return false;
            }

            if (CanTravel(start, target, units))
            {
                route.Add(target);
                return true;
            }

            units = units.Freeze();
            var open = new List<Vector2Int> { startCell };
            var costs = new Dictionary<Vector2Int, float> { [startCell] = 0f };
            var previous = new Dictionary<Vector2Int, Vector2Int>();
            while (open.Count > 0)
            {
                int best = 0;
                for (int index = 1; index < open.Count; index++)
                {
                    if (Score(open[index], targetCell, costs) < Score(open[best], targetCell, costs))
                    {
                        best = index;
                    }
                }

                Vector2Int current = open[best];
                open.RemoveAt(best);
                if (current == targetCell && current != startCell)
                {
                    route.Add(target);
                    current = previous[current];
                    while (current != startCell)
                    {
                        route.Add(Center(current));
                        current = previous[current];
                    }

                    route.Reverse();
                    Smooth(start, route, units);
                    return true;
                }

                Vector3 from = current == startCell ? start : Center(current);
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = current + direction;
                    bool diagonal = direction.x != 0 && direction.y != 0;
                    if (!IsWalkable(next) ||
                        (diagonal && (!IsWalkable(current + new Vector2Int(direction.x, 0)) ||
                                      !IsWalkable(current + new Vector2Int(0, direction.y)))))
                    {
                        continue;
                    }

                    float nextCost = costs[current] + (diagonal ? DiagonalCost : 1f);
                    Vector3 to = next == targetCell ? target : Center(next);
                    if ((costs.TryGetValue(next, out float cost) && cost <= nextCost) ||
                        !CanTravel(from, to, units))
                    {
                        continue;
                    }

                    costs[next] = nextCost;
                    previous[next] = current;
                    if (!open.Contains(next))
                    {
                        open.Add(next);
                    }
                }
            }

            return false;
        }

        private bool IsSafePoint(Vector3 point)
        {
            return IsWalkable(CellAt(point)) &&
                point.x >= bounds.xMin + clearance && point.x <= bounds.xMax - clearance &&
                point.z >= bounds.yMin + clearance && point.z <= bounds.yMax - clearance;
        }

        private bool CanTravel(Vector3 from, Vector3 to, UnitMovement.UnitClearance units)
        {
            return HasClearSegment(from, to) && units.IsClear(from, to);
        }

        private void Smooth(Vector3 start, List<Vector3> route, UnitMovement.UnitClearance units)
        {
            int write = 0;
            for (int first = 0; first < route.Count;)
            {
                int farthest = route.Count - 1;
                while (farthest > first && !CanTravel(start, route[farthest], units))
                {
                    farthest--;
                }

                start = route[farthest];
                route[write++] = start;
                first = farthest + 1;
            }

            route.RemoveRange(write, route.Count - write);
        }

        private static bool ClipAxis(float origin, float delta, float min, float max, ref float enter, ref float exit)
        {
            if (Mathf.Abs(delta) < 0.000001f)
            {
                return origin >= min && origin <= max;
            }

            float first = (min - origin) / delta;
            float second = (max - origin) / delta;
            enter = Mathf.Max(enter, Mathf.Min(first, second));
            exit = Mathf.Min(exit, Mathf.Max(first, second));
            return enter <= exit;
        }

        private static float Score(Vector2Int cell, Vector2Int target, Dictionary<Vector2Int, float> costs)
        {
            int x = Mathf.Abs(cell.x - target.x);
            int y = Mathf.Abs(cell.y - target.y);
            return costs[cell] + Mathf.Max(x, y) + (DiagonalCost - 1f) * Mathf.Min(x, y);
        }
    }
}
