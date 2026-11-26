using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rts
{
    // All geometry is cosmetic and stays above the map collider and below the fog plane.
    internal sealed class MatchLandscape : MonoBehaviour
    {
        private sealed class Detail
        {
            public Collider Owner;
            public Bounds Footprint;
            public GameObject Object;
        }

        private readonly List<Detail> details = new();
        private readonly List<Mesh> meshes = new();
        private readonly List<Material> materials = new();
        private MatchVisibility fog;
        private Material earth, leaf, stone, bark;

        internal static MatchLandscape Create(Transform map, RectInt bounds, MatchVisibility visibility,
            IReadOnlyList<Collider> obstacles)
        {
            var root = new GameObject("Match landscape");
            root.transform.SetParent(map, false);
            root.transform.localScale = new Vector3(1 / map.lossyScale.x, 1 / map.lossyScale.y, 1 / map.lossyScale.z);
            var landscape = root.AddComponent<MatchLandscape>();
            landscape.fog = visibility;
            var ground = map.GetComponent<Renderer>().sharedMaterial;
            landscape.earth = landscape.Tint(ground, new Color(.43f,.36f,.24f));
            landscape.leaf = landscape.Tint(ground, new Color(.24f,.35f,.20f));
            landscape.stone = landscape.Tint(ground, new Color(.46f,.44f,.38f));
            landscape.bark = landscape.Tint(ground, new Color(.34f,.25f,.17f));
            landscape.BuildGround(bounds, ground);
            foreach (var obstacle in obstacles)
            {
                if (obstacle.GetComponent<GoldDeposit>() != null) landscape.AddApproach(obstacle);
                else if (obstacle.GetComponent<CombatTarget>() != null) landscape.AddApproach(obstacle);
                else landscape.AddCluster(obstacle);
            }
            visibility.Refreshed += landscape.RefreshDetails;
            landscape.RefreshDetails();
            return landscape;
        }

        private Material Tint(Material template, Color color)
        {
            var result = new Material(template) { color = color };
            materials.Add(result);
            return result;
        }

        private void BuildGround(RectInt bounds, Material ground)
        {
            var vertices = new List<Vector3>();
            const int stride = 4;
            for (int z = bounds.yMin; z < bounds.yMax; z += stride)
            for (int x = bounds.xMin; x < bounds.xMax; x += stride)
            {
                var a = GroundPoint(bounds,x,z);
                var b = GroundPoint(bounds,x,z+stride);
                var c = GroundPoint(bounds,x+stride,z+stride);
                var d = GroundPoint(bounds,x+stride,z);
                vertices.AddRange(new[]{a,b,c,a,c,d});
            }
            MeshObject("Meadow", transform, vertices, ground);
        }

        private static Vector3 GroundPoint(RectInt bounds,int x,int z)
        {
            int hash = Hash(x,z);
            float dx = x > bounds.xMin && x < bounds.xMax ? (hash % 7 - 3) * .18f : 0;
            float dz = z > bounds.yMin && z < bounds.yMax ? (hash / 7 % 7 - 3) * .18f : 0;
            return new Vector3(Mathf.Min(x,bounds.xMax)+dx,.012f,Mathf.Min(z,bounds.yMax)+dz);
        }

        private static int Hash(int x,int z) => unchecked((x * 73856093 ^ z * 19349663) & int.MaxValue);

        internal void AddApproach(Collider owner)
        {
            if (!(owner is BoxCollider box)) return;
            Vector3 size = Vector3.Scale(box.size,owner.transform.lossyScale);
            Vector3 center = owner.transform.TransformPoint(box.center);
            center.y = .022f;
            center.z -= size.z * .5f + .39f;
            float width = Mathf.Min(2.5f,size.x*.86f);
            var shape = new List<Vector3>();
            var rim = new[] { new Vector2(-.48f,-.60f),new Vector2(-.63f,.0f),new Vector2(-.44f,.56f),
                new Vector2(.38f,.58f),new Vector2(.60f,.13f),new Vector2(.46f,-.61f) };
            for (int i = 0; i < rim.Length; i++)
            {
                Vector2 a = rim[i], b = rim[(i+1)%rim.Length];
                shape.Add(center);
                shape.Add(center + new Vector3(a.x*width,0,a.y*1.6f));
                shape.Add(center + new Vector3(b.x*width,0,b.y*1.6f));
            }
            var obj = MeshObject("Worn approach",transform,shape,earth);
            details.Add(new Detail { Owner = owner, Footprint = obj.GetComponent<MeshRenderer>().bounds, Object = obj });
            obj.SetActive(false);
        }

        private void AddCluster(Collider obstacle)
        {
            Bounds b = obstacle.bounds;
            // Use only stone footprints that already occupy at least three cells in both axes.
            if (b.size.x < 3 || b.size.z < 3) return;
            var group = new GameObject("Obstacle verge");
            group.transform.SetParent(transform,false);
            float x = b.center.x + b.extents.x * .72f;
            float z = b.center.z - b.extents.z * .75f;
            var tree = new Vector3(x,b.max.y,z);
            var greens = new List<Vector3>();
            var rocks = new List<Vector3>();
            var trunk = new List<Vector3>();
            Pyramid(trunk,tree,.09f,.73f,5);
            Pyramid(greens,tree+Vector3.up*.44f,.32f,.75f,5);
            Pyramid(greens,tree+Vector3.up*.69f,.23f,.65f,5);
            Pyramid(greens,new Vector3(x-.54f,b.max.y,z+.21f),.25f,.37f,5);
            Pyramid(rocks,new Vector3(x-.25f,b.max.y,z+.42f),.19f,.24f,5);
            MeshObject("Pine and shrub",group.transform,greens,leaf);
            MeshObject("Rubble",group.transform,rocks,stone);
            MeshObject("Trunk",group.transform,trunk,bark);
            var footprint = new Bounds(new Vector3(x-.18f,b.max.y,z+.17f),new Vector3(1.4f,0,1.15f));
            details.Add(new Detail { Owner = obstacle, Footprint = footprint, Object = group });
            group.SetActive(false);
        }

        private static void Pyramid(List<Vector3> vertices,Vector3 center,float radius,float height,int sides)
        {
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i+1) * Mathf.PI * 2 / sides;
                vertices.Add(center + new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));
                vertices.Add(center + Vector3.up*height);
                vertices.Add(center + new Vector3(Mathf.Cos(b)*radius,0,Mathf.Sin(b)*radius));
            }
        }

        private GameObject MeshObject(string label,Transform parent,List<Vector3> vertices,Material material)
        {
            var item = new GameObject(label);
            item.transform.SetParent(parent,false);
            var mesh = new Mesh { name = label };
            var indices = new int[vertices.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices,0);
            mesh.RecalculateNormals();
            meshes.Add(mesh);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // Decorative objects never cast revealing silhouettes across a fog boundary.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return item;
        }

        internal void RefreshDetails()
        {
            if (!isActiveAndEnabled) return;
            for (int i = details.Count-1; i >= 0; i--)
            {
                var item = details[i];
                if (item.Owner == null || !item.Owner.gameObject.activeInHierarchy)
                {
                    item.Object.SetActive(false);
                    foreach (var filter in item.Object.GetComponentsInChildren<MeshFilter>(true))
                    {
                        meshes.Remove(filter.sharedMesh);
                        Destroy(filter.sharedMesh);
                    }
                    Destroy(item.Object);
                    details.RemoveAt(i);
                    continue;
                }
                bool visible = fog.State(Side.Player, item.Owner.bounds.center) == VisibilityState.Visible &&
                    EntirelyVisible(item.Footprint);
                if (item.Object.activeSelf != visible) item.Object.SetActive(visible);
            }
        }

        private bool EntirelyVisible(Bounds area)
        {
            for (int z = Mathf.FloorToInt(area.min.z); z <= Mathf.FloorToInt(area.max.z); z++)
            for (int x = Mathf.FloorToInt(area.min.x); x <= Mathf.FloorToInt(area.max.x); x++)
                if (fog.State(Side.Player,new Vector3(x+.5f,0,z+.5f)) != VisibilityState.Visible) return false;
            return true;
        }

        private void OnDestroy()
        {
            if (fog != null) fog.Refreshed -= RefreshDetails;
            foreach (var mesh in meshes) Destroy(mesh);
            foreach (var material in materials) Destroy(material);
        }
    }
}

