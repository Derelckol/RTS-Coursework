using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rts
{
    public enum VisibilityState : byte { Unexplored, Explored, Visible }

    // Value-only knowledge: safe to serialize without retaining a hidden scene object.
    [System.Serializable]
    public struct BuildingMemory
    {
        public BuildingKind type;
        public Vector3 position;
        public Vector3 size;
    }

    public sealed partial class MatchVisibility : MonoBehaviour, ISideVisibility
    {
        [SerializeField, Min(1f)] private float unitSight = 12f;
        [SerializeField, Min(1f)] private float buildingSight = 12f;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.15f;
        [SerializeField] private Material fogTemplate;

        private readonly HashSet<int>[] knownMines = { new HashSet<int>(), new HashSet<int>() };
        private readonly List<BuildingMemory>[] buildings =
            { new List<BuildingMemory>(), new List<BuildingMemory>() };
        private readonly Dictionary<int, int>[] mineStock =
            { new Dictionary<int, int>(), new Dictionary<int, int>() };
        private VisibilityState[][] cells;
        private RectInt bounds;
        private IReadOnlyList<CombatTarget> actors;
        private IReadOnlyList<Collider> obstacles;
        private float nextRefresh;
        private Texture2D mask;
        private Color32[] pixels;
        private Mesh fogMesh;
        private Material fogMaterial;
        private readonly List<Renderer> hidden = new List<Renderer>();

        public RectInt Bounds => bounds;
        public Texture2D PlayerMask => mask;
        internal event System.Action Refreshed;

        public void Initialize(RectInt map, IReadOnlyList<CombatTarget> targets, IReadOnlyList<Collider> solids)
        {
            bounds = map;
            actors = targets;
            obstacles = solids;
            cells = new[]
            {
                new VisibilityState[map.width * map.height],
                new VisibilityState[map.width * map.height]
            };
            foreach (var set in knownMines) set.Clear();
            foreach (var set in buildings) set.Clear();
            foreach (var set in mineStock) set.Clear();
            mask = new Texture2D(map.width, map.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            pixels = new Color32[map.width * map.height];
            Refresh();
            if (Application.isPlaying) CreateFog();
        }

        public void Tick(float deltaTime)
        {
            nextRefresh -= deltaTime;
            if (nextRefresh <= 0)
            {
                nextRefresh = refreshInterval;
                Refresh();
            }
        }

        public VisibilityState State(Side side, Vector3 point)
        {
            var cell = GridNavigation.CellAt(point);
            if (cells == null || !bounds.Contains(cell)) return VisibilityState.Unexplored;
            return cells[(int)side][Index(cell.x, cell.y)];
        }

        private int Index(int x, int z) => (z - bounds.yMin) * bounds.width + x - bounds.xMin;

        public bool IsVisible(Side observer, CombatTarget target) =>
            target != null && (target.Side == observer || State(observer, target.transform.position) == VisibilityState.Visible);

        public bool IsKnown(Side side, GoldDeposit mine) =>
            mine != null && knownMines[(int)side].Contains(mine.GetInstanceID());
        public int KnownStock(Side side, GoldDeposit mine) =>
            mine != null && mineStock[(int)side].TryGetValue(mine.GetInstanceID(), out int stock) ? stock : 0;

        public bool IsVisible(Side side, GoldDeposit mine) =>
            mine != null && State(side, mine.transform.position) == VisibilityState.Visible;

        public bool TryKnownBase(Side observer, out Vector3 position)
        {
            foreach (BuildingMemory remembered in buildings[(int)observer])
            {
                if (remembered.type != BuildingKind.TownHall) continue;
                position = remembered.position;
                return true;
            }
            position = default;
            return false;
        }

        public BuildingMemory[] CaptureBuildings(Side side) => buildings[(int)side].ToArray();

        public void RestoreBuildings(Side side, BuildingMemory[] memories)
        {
            buildings[(int)side].Clear();
            if (memories == null) return;
            foreach (BuildingMemory memory in memories)
                if (bounds.Contains(GridNavigation.CellAt(memory.position))) buildings[(int)side].Add(memory);
        }

        public bool TryExplorationPoint(Side side, Vector3 origin, int offset, out Vector3 point)
        {
            // Deterministic wide grid explores both resource corridors and the far side.
            var candidates = new List<Vector3>();
            for (int z = bounds.yMin + 5; z < bounds.yMax; z += 9)
            for (int x = bounds.xMin + 5; x < bounds.xMax; x += 9)
            {
                var candidate = new Vector3(x + 0.5f, 0, z + 0.5f);
                if (State(side, candidate) == VisibilityState.Unexplored) candidates.Add(candidate);
            }
            candidates.Sort((a, b) => (a - origin).sqrMagnitude.CompareTo((b - origin).sqrMagnitude));
            if (candidates.Count == 0)
            {
                point = default;
                return false;
            }
            point = candidates[offset % candidates.Count];
            return true;
        }

        public void Refresh()
        {
            if (cells == null) return;
            foreach (VisibilityState[] grid in cells)
                for (int index = 0; index < grid.Length; index++)
                    if (grid[index] == VisibilityState.Visible) grid[index] = VisibilityState.Explored;

            foreach (CombatTarget actor in actors)
            {
                if (actor == null || !actor.IsAlive || actor.Unit == null && !actor.IsOperational) continue;
                Reveal(actor.Side, actor.transform.position, actor.Unit == null ? buildingSight : unitSight);
            }

            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                Side side = (Side)sideIndex;
                foreach (Collider body in obstacles)
                {
                    if (body != null && body.TryGetComponent(out GoldDeposit mine) && IsVisible(side, mine))
                    {
                        knownMines[sideIndex].Add(mine.GetInstanceID());
                        mineStock[sideIndex][mine.GetInstanceID()] = mine.Stock;
                    }
                }
                // Reconcile only what can be observed at remembered coordinates.
                for (int index = buildings[sideIndex].Count - 1; index >= 0; index--)
                {
                    // A fresh observation replaces old knowledge, including an empty site.
                    if (State(side, buildings[sideIndex][index].position) == VisibilityState.Visible)
                        buildings[sideIndex].RemoveAt(index);
                }
                foreach (CombatTarget actor in actors)
                    if (actor != null && actor.IsAlive && actor.Side != side &&
                        actor.Unit == null && IsVisible(side, actor))
                        buildings[sideIndex].Add(new BuildingMemory
                        {
                            type = actor.Building, position = actor.transform.position,
                            size = actor.Body.bounds.size
                        });
            }

            foreach (Renderer renderer in hidden)
                if (renderer != null) renderer.enabled = true;
            hidden.Clear();
            if (Application.isPlaying)
            {
                foreach (CombatTarget actor in actors)
                {
                    if (actor == null || actor.Side == Side.Player || IsVisible(Side.Player, actor)) continue;
                    foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>())
                    {
                        renderer.enabled = false;
                        hidden.Add(renderer);
                    }
                }
                foreach (Collider body in obstacles)
                {
                    if (body == null || body.GetComponent<CombatTarget>() != null ||
                        State(Side.Player, body.transform.position) != VisibilityState.Unexplored) continue;
                    foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>())
                    {
                        renderer.enabled = false;
                        hidden.Add(renderer);
                    }
                }
            }
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = new Color32(3, 7, 12, cells[0][index] == VisibilityState.Visible ? (byte)0 :
                    cells[0][index] == VisibilityState.Explored ? (byte)175 : (byte)255);
            mask.SetPixels32(pixels);
            mask.Apply(false);
            Refreshed?.Invoke();
        }

        private void Reveal(Side side, Vector3 center, float radius)
        {
            for (int z = Mathf.Max(bounds.yMin, Mathf.FloorToInt(center.z - radius));
                z < Mathf.Min(bounds.yMax, Mathf.CeilToInt(center.z + radius)); z++)
            for (int x = Mathf.Max(bounds.xMin, Mathf.FloorToInt(center.x - radius));
                x < Mathf.Min(bounds.xMax, Mathf.CeilToInt(center.x + radius)); x++)
                if ((new Vector2(x + 0.5f - center.x, z + 0.5f - center.z)).sqrMagnitude <= radius * radius)
                    cells[(int)side][Index(x, z)] = VisibilityState.Visible;
        }

        private void CreateFog()
        {
            var fog = new GameObject("Fog of war");
            fog.transform.SetParent(transform, false);
            fog.transform.localScale = Vector3.one;
            // Use world-space vertices, independent of the map plane scale.
            fog.transform.SetParent(null, true);
            fog.transform.position = Vector3.zero;
            fog.transform.rotation = Quaternion.identity;
            fog.transform.localScale = Vector3.one;
            fogMesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(bounds.xMin, 0.04f, bounds.yMin), new Vector3(bounds.xMin, 0.04f, bounds.yMax),
                    new Vector3(bounds.xMax, 0.04f, bounds.yMax), new Vector3(bounds.xMax, 0.04f, bounds.yMin)
                },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            fog.AddComponent<MeshFilter>().sharedMesh = fogMesh;
            fogMaterial = fogTemplate != null ? new Material(fogTemplate) :
                new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fogMaterial.SetFloat("_Surface", 1);
            fogMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fogMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            fogMaterial.SetFloat("_ZWrite", 0);
            fogMaterial.SetFloat("_Cull", (float)CullMode.Off);
            fogMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            fogMaterial.renderQueue = 3100;
            fogMaterial.SetTexture("_BaseMap", mask);
            fog.AddComponent<MeshRenderer>().sharedMaterial = fogMaterial;
        }

        private void OnDestroy()
        {
            if (fogMesh != null) Destroy(fogMesh);
            if (fogMaterial != null) Destroy(fogMaterial);
            if (mask != null) Destroy(mask);
        }
    }
}
