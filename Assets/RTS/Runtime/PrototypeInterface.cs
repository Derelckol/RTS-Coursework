using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public enum MapMarkerKind { Obstacle, Gold, AllyUnit, EnemyUnit, AllyBuilding, EnemyBuilding }

    public readonly struct MapMarker
    {
        public readonly Rect Area;
        public readonly MapMarkerKind Kind;

        public MapMarker(Rect area, MapMarkerKind kind)
        {
            Area = area;
            Kind = kind;
        }
    }

    public sealed class PrototypeInterface : MonoBehaviour
    {
        private static readonly Color Ground = new Color(0.13f, 0.23f, 0.19f);
        private static readonly Color Obstacle = new Color(0.55f, 0.59f, 0.61f);
        private static readonly Color Gold = new Color(1f, 0.8f, 0.25f);
        private static readonly Color Ally = new Color(0.29f, 0.87f, 1f);
        private static readonly Color Enemy = new Color(1f, 0.35f, 0.29f);
        private readonly List<MapMarker> markers = new List<MapMarker>();
        private RectInt mapBounds;
        private MatchVisibility fog;
        public void SetVisibility(MatchVisibility visibility) => fog = visibility;

        public bool IsPaused { get; private set; }

        public void Initialize(RectInt bounds) => mapBounds = bounds;

        public void SetPaused(bool paused) => IsPaused = paused;

        public bool HandleEscape(PrototypeConstruction construction)
        {
            if (!IsPaused && construction != null && construction.IsPlacing)
            {
                construction.Cancel();
                return false;
            }

            SetPaused(!IsPaused);
            return true;
        }

        public bool Covers(HudLayout layout, Vector2 logicalPoint) =>
            IsPaused || layout.Menu.Contains(logicalPoint) || layout.MinimapPanel(mapBounds).Contains(logicalPoint);

        public bool TryMinimapFocus(HudLayout layout, Vector2 logicalPoint, out Vector3 position)
        {
            Rect content = layout.MinimapContent(mapBounds);
            if (IsPaused || !content.Contains(logicalPoint))
            {
                position = default;
                return false;
            }

            position = new Vector3(
                Mathf.Lerp(mapBounds.xMin, mapBounds.xMax, (logicalPoint.x - content.xMin) / content.width),
                0,
                Mathf.Lerp(mapBounds.yMax, mapBounds.yMin, (logicalPoint.y - content.yMin) / content.height));
            return true;
        }

        public Vector2 Project(Rect content, Vector3 world)
        {
            return new Vector2(
                Mathf.Lerp(content.xMin, content.xMax, Mathf.InverseLerp(mapBounds.xMin, mapBounds.xMax, world.x)),
                Mathf.Lerp(content.yMax, content.yMin, Mathf.InverseLerp(mapBounds.yMin, mapBounds.yMax, world.z)));
        }

        public Rect CameraFootprint(Rect content, Camera camera)
        {
            var plane = new Plane(Vector3.up, Vector3.zero);
            Vector2 min = content.max;
            Vector2 max = content.min;
            for (int corner = 0; corner < 4; corner++)
            {
                Ray ray = camera.ViewportPointToRay(new Vector3(corner % 2, corner / 2, 0));
                if (!plane.Raycast(ray, out float distance))
                {
                    return default;
                }

                Vector2 point = Project(content, ray.GetPoint(distance));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public void CollectMarkers(Rect content, IReadOnlyList<Collider> obstacles,
            IReadOnlyList<CombatTarget> targets, List<MapMarker> output)
        {
            output.Clear();
            foreach (Collider body in obstacles)
            {
                if (body == null || !body.gameObject.activeInHierarchy ||
                    body.TryGetComponent(out CombatTarget _))
                {
                    continue;
                }

                MapMarkerKind kind = body.TryGetComponent(out GoldDeposit _)
                    ? MapMarkerKind.Gold : MapMarkerKind.Obstacle;
                if (fog != null && fog.State(Side.Player, body.transform.position) == VisibilityState.Unexplored)
                    continue;
                output.Add(new MapMarker(ProjectBounds(content, body.bounds), kind));
            }

            foreach (CombatTarget target in targets)
            {
                if (target == null || !target.IsAlive || fog != null && !fog.IsVisible(Side.Player, target))
                {
                    continue;
                }

                bool ally = target.Side == Side.Player;
                if (target.Unit == null)
                {
                    var kind = ally ? MapMarkerKind.AllyBuilding : MapMarkerKind.EnemyBuilding;
                    output.Add(new MapMarker(ProjectBounds(content, target.Body.bounds), kind));
                }
                else
                {
                    Vector2 center = Project(content, target.transform.position);
                    var kind = ally ? MapMarkerKind.AllyUnit : MapMarkerKind.EnemyUnit;
                    var rect = new Rect(center.x - 2.5f, center.y - 2.5f, 5f, 5f);
                    output.Add(new MapMarker(rect, kind));
                }
            }
        }

        private Rect ProjectBounds(Rect content, Bounds bounds)
        {
            Vector2 first = Project(content, bounds.min);
            Vector2 second = Project(content, bounds.max);
            return Rect.MinMaxRect(first.x, second.y, second.x, first.y);
        }

        public void Draw(HudLayout layout, Camera camera, IReadOnlyList<Collider> obstacles,
            IReadOnlyList<CombatTarget> targets, Action<bool> setPaused, bool showMenu, PrototypeMatch match)
        {
            Rect panel = layout.MinimapPanel(mapBounds);
            Rect content = layout.MinimapContent(mapBounds);
            HudPresentation.Panel(panel);
            GUI.Box(panel, "Мінікарта");
            if (Event.current.type == EventType.Repaint)
            {
                DrawMap(content, camera, obstacles, targets);
            }
            DrawLegend(panel);

            if (!showMenu)
            {
                return;
            }

            if (!IsPaused)
            {
                if (MatchUi.Button(layout.Menu, "Меню"))
                {
                    setPaused(true);
                }
                return;
            }

            if (match != null)
            {
                match.DrawPause(layout, setPaused);
                return;
            }

            Paint(layout.FullScreen, new Color(0.03f, 0.06f, 0.09f, 0.82f));
            Rect pause = layout.PausePanel;
            Paint(pause, new Color(0.13f, 0.17f, 0.22f));
            GUI.Box(pause, "Пауза");
            if (MatchUi.Button(new Rect(pause.x + 34, pause.y + 55, pause.width - 68, 37), "Продовжити"))
            {
                setPaused(false);
            }
        }

        private void DrawMap(Rect content, Camera camera, IReadOnlyList<Collider> obstacles,
            IReadOnlyList<CombatTarget> targets)
        {
            Paint(new Rect(content.x - 1, content.y - 1, content.width + 2, content.height + 2), Obstacle);
            Paint(content, Ground);
            CollectMarkers(content, obstacles, targets, markers);
            GUI.BeginGroup(content);
            foreach (MapMarker marker in markers)
            {
                Rect area = marker.Area;
                area.position -= content.position;
                Color color = marker.Kind switch
                {
                    MapMarkerKind.Obstacle => Obstacle,
                    MapMarkerKind.Gold => Gold,
                    MapMarkerKind.AllyUnit or MapMarkerKind.AllyBuilding => Ally,
                    _ => Enemy
                };
                if (marker.Kind == MapMarkerKind.AllyBuilding || marker.Kind == MapMarkerKind.EnemyBuilding)
                {
                    Paint(area, new Color(color.r, color.g, color.b, 0.42f));
                    Outline(area, color);
                }
                else
                {
                    Paint(area, color);
                }
            }

            Rect frame = CameraFootprint(content, camera);
            if (fog != null)
            {
                GUI.DrawTexture(new Rect(0, 0, content.width, content.height), fog.PlayerMask);
                // Draw value snapshots after the fog; never inspect the hidden live building.
                foreach (BuildingMemory memory in fog.CaptureBuildings(Side.Player))
                {
                    if (fog.State(Side.Player, memory.position) == VisibilityState.Visible) continue;
                    Rect remembered = ProjectBounds(content, new Bounds(memory.position, memory.size));
                    remembered.position -= content.position;
                    Outline(remembered, new Color(0.65f, 0.39f, 0.39f, 0.85f));
                    // A hollow cross distinguishes last-seen knowledge from a live filled marker.
                    Paint(new Rect(remembered.center.x - 2, remembered.center.y, 4, 1), Color.gray);
                    Paint(new Rect(remembered.center.x, remembered.center.y - 2, 1, 4), Color.gray);
                }
            }
            frame.position -= content.position;
            Outline(frame, Color.white);
            GUI.EndGroup();
        }

        private static void DrawLegend(Rect panel)
        {
            float y = panel.yMax - 23;
            Legend(panel.x + 10, y, Obstacle, "Стіни");
            Legend(panel.x + 64, y, Gold, "Золото");
            Legend(panel.x + 128, y, Ally, "Свої");
            Legend(panel.x + 182, y, Enemy, "Вороги");
        }

        private static void Legend(float x, float y, Color color, string caption)
        {
            Paint(new Rect(x, y + 7, 7, 7), color);
            GUI.Label(new Rect(x + 9, y, 67, 22), caption);
        }

        private static void Outline(Rect area, Color color)
        {
            if (area.width <= 0f || area.height <= 0f)
            {
                return;
            }

            Paint(new Rect(area.xMin, area.yMin, area.width, 1), color);
            Paint(new Rect(area.xMin, area.yMax - 1, area.width, 1), color);
            Paint(new Rect(area.xMin, area.yMin, 1, area.height), color);
            Paint(new Rect(area.xMax - 1, area.yMin, 1, area.height), color);
        }

        private static void Paint(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
