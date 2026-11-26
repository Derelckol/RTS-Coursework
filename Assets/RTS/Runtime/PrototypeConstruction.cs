using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public sealed partial class PrototypeConstruction : MonoBehaviour
    {
        [Serializable]
        public sealed class Blueprint
        {
            [SerializeField] private BuildingKind kind;
            [SerializeField] private Vector3 size = new Vector3(3, 1.8f, 3);
            [SerializeField, Min(1f)] private float health = 150f;
            [SerializeField, Min(0)] private int cost = 40;
            [SerializeField, Min(0.01f)] private float seconds = 4f;

            public BuildingKind Kind => kind;
            public Vector3 Size => Vector3.Max(size, Vector3.one * 0.2f);
            public float Health => health;
            public int Cost => cost;
            public float Seconds => seconds;
        }

        [SerializeField] private Material buildingMaterial;
        [SerializeField] private Material opponentMaterial;
        [SerializeField] private Blueprint[] blueprints = Array.Empty<Blueprint>();
        [SerializeField, Min(0.1f)] private float workRange = 1.45f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private readonly List<Vector3> plannedRoute = new List<Vector3>();
        private RectInt bounds;
        private PrototypeEconomy economy;
        private PrototypeEconomy opponentEconomy;
        private UnitMovement movement;
        private UnitOrders orders;
        private Func<List<Bounds>> footprints;
        private Action<CombatTarget> register;
        private Blueprint pending;
        private PrototypeUnit builder;
        private GameObject preview;
        private Renderer previewRenderer;
        private MaterialPropertyBlock tint;
        private Vector3 previewPosition;
        private float nextPreview;
        private bool previewValid;

        public IReadOnlyList<Blueprint> Blueprints => blueprints;
        public bool IsPlacing => pending != null;

        public void Initialize(RectInt mapBounds, PrototypeEconomy resources, UnitMovement navigation,
            UnitOrders unitOrders, Func<List<Bounds>> collectFootprints, Action<CombatTarget> registerBuilding,
            PrototypeEconomy rivals = null)
        {
            bounds = mapBounds;
            economy = resources;
            opponentEconomy = rivals;
            movement = navigation;
            orders = unitOrders;
            footprints = collectFootprints;
            register = registerBuilding;
        }

        public void Begin(Blueprint blueprint, PrototypeUnit worker)
        {
            if (worker == null || !worker.IsAvailable || !worker.PlayerOwned || worker.Kind != UnitKind.Worker)
            {
                return;
            }

            builder = worker;
            pending = blueprint;
            nextPreview = 0f;
        }

        public void Cancel()
        {
            pending = null;
            builder = null;
            HidePreview();
        }

        public void HidePreview()
        {
            if (preview != null)
            {
                preview.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (preview != null && Application.isPlaying)
            {
                Destroy(preview);
            }
        }

        public void OnWorkerDefeated(PrototypeUnit worker)
        {
            if (builder == worker)
            {
                Cancel();
            }
        }

        public void ShowPreview(Vector3 groundPoint)
        {
            if (!IsPlacing)
            {
                return;
            }

            Bounds footprint = Footprint(pending, groundPoint);
            if (preview == null)
            {
                preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                preview.name = "Construction preview";
                preview.GetComponent<Collider>().enabled = false;
                previewRenderer = preview.GetComponent<Renderer>();
                previewRenderer.sharedMaterial = buildingMaterial;
                tint = new MaterialPropertyBlock();
            }

            if (previewPosition != footprint.center || Time.unscaledTime >= nextPreview)
            {
                previewPosition = footprint.center;
                nextPreview = Time.unscaledTime + 0.12f;
                previewValid = CanPlace(groundPoint);
            }

            preview.transform.position = footprint.center;
            preview.transform.localScale = footprint.size;
            tint.SetColor(BaseColor, previewValid ? new Color(0.1f, 0.85f, 0.25f) : new Color(0.9f, 0.18f, 0.12f));
            previewRenderer.SetPropertyBlock(tint);
            preview.SetActive(true);
        }

        public bool CanPlace(Vector3 groundPoint)
        {
            return IsPlacing && builder != null && builder.IsAvailable &&
                TryPlan(builder, Footprint(pending, groundPoint));
        }

        public CombatTarget Confirm(Vector3 groundPoint)
        {
            if (!IsPlacing || builder == null || !builder.IsAvailable)
            {
                return null;
            }

            CombatTarget target = TryBuild(pending, builder, groundPoint);
            if (target != null)
            {
                Cancel();
            }

            return target;
        }

        public CombatTarget TryBuild(Blueprint blueprint, PrototypeUnit worker, Vector3 point)
        {
            if (worker == null || !worker.IsAvailable || worker.Kind != UnitKind.Worker)
            {
                return null;
            }

            PrototypeEconomy resources = worker.PlayerOwned ? economy : opponentEconomy;
            Bounds footprint = Footprint(blueprint, point);
            if (resources == null || !TryPlan(worker, footprint))
            {
                if (worker.PlayerOwned)
                {
                    resources?.Notify("Cannot build here.");
                }
                return null;
            }

            if (!resources.TrySpend(blueprint.Cost))
            {
                return null;
            }

            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = blueprint.Kind.ToString();
            item.transform.position = footprint.center;
            item.transform.localScale = footprint.size;
            var renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterial = worker.PlayerOwned || opponentMaterial == null ? buildingMaterial : opponentMaterial;
            var foundationTint = new MaterialPropertyBlock();
            foundationTint.SetColor(BaseColor, new Color(0.65f, 0.45f, 0.25f));
            renderer.SetPropertyBlock(foundationTint);
            var site = new ConstructionSite(blueprint.Seconds, workRange, target =>
            {
                renderer.SetPropertyBlock(null);
                resources.RegisterBuilding(target);
            });
            var actor = item.AddComponent<CombatTarget>();
            actor.InitializeConstruction(blueprint.Kind, worker.Team, blueprint.Health, site);
            site.Attach(actor);
            Physics.SyncTransforms();
            register(actor);
            orders.StartBuild(worker, site, plannedRoute);
            return actor;
        }

        private static Bounds Footprint(Blueprint blueprint, Vector3 point)
        {
            Vector3 size = blueprint.Size;
            return new Bounds(new Vector3(Mathf.Floor(point.x) + 0.5f, size.y * 0.5f,
                Mathf.Floor(point.z) + 0.5f), size);
        }

        private bool TryPlan(PrototypeUnit worker, Bounds site)
        {
            MatchVisibility fog = GetComponent<MatchVisibility>();
            if (fog != null && worker.PlayerOwned)
            {
                for (int z = Mathf.FloorToInt(site.min.z); z < Mathf.CeilToInt(site.max.z); z++)
                for (int x = Mathf.FloorToInt(site.min.x); x < Mathf.CeilToInt(site.max.x); x++)
                    if (fog.State(worker.Team, new Vector3(x + 0.5f, 0, z + 0.5f)) != VisibilityState.Visible)
                        return false;
            }
            if (site.min.x < bounds.xMin || site.max.x > bounds.xMax ||
                site.min.z < bounds.yMin || site.max.z > bounds.yMax)
            {
                return false;
            }

            List<Bounds> obstacles = footprints();
            foreach (Bounds obstacle in obstacles)
            {
                if (site.min.x < obstacle.max.x && site.max.x > obstacle.min.x &&
                    site.min.z < obstacle.max.z && site.max.z > obstacle.min.z)
                {
                    return false;
                }
            }

            return movement.TryPlanConstruction(worker, site, obstacles, workRange, plannedRoute);
        }
    }
}
