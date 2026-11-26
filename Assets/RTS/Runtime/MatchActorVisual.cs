using UnityEngine;

namespace Rts
{
    // Models are children of the gameplay object so fog and destruction cover every renderer.
    public sealed class MatchActorVisual : MonoBehaviour
    {
        private CombatTarget actor;
        private Renderer[] finished, buildingSite, badge;
        private bool operational, upgraded;

        internal static void Attach(CombatTarget actor, PrototypeControls controls)
        {
            string side = actor.Side == Side.Player ? "Blue" : "Red";
            string kind = actor.Unit != null ? actor.Unit.Kind.ToString() : actor.Building.ToString();
            var model = AddModel(actor.Body, side + kind, Vector3.one);
            if (model == null) return;
            if (actor.Unit != null)
            {
                model.AddComponent<MatchUnitAnimation>().Initialize(actor.Unit, controls);
                return;
            }

            var visual = actor.gameObject.AddComponent<MatchActorVisual>();
            visual.actor = actor;
            visual.finished = model.GetComponentsInChildren<Renderer>();
            Vector3 size = BoxSize(actor.Body);
            var site = AddModel(actor.Body, "Foundation", new Vector3(size.x / 2, size.y / 1.8f, size.z / 2));
            var upgrade = AddModel(actor.Body, "UpgradeBadge",
                new Vector3(size.x / 2, actor.Building == BuildingKind.House ? 1f : 1.4f, size.z / 2));
            visual.buildingSite = site != null ? site.GetComponentsInChildren<Renderer>() : null;
            visual.badge = upgrade != null ? upgrade.GetComponentsInChildren<Renderer>() : null;
            visual.Apply();
        }

        internal static void AttachLandmark(Collider body)
        {
            if (body.GetComponent<CombatTarget>() != null) return;
            bool gold = body.GetComponent<GoldDeposit>() != null;
            Vector3 size = BoxSize(body);
            bool alongZ = !gold && size.z > size.x;
            var model = AddModel(body, gold ? "GoldDeposit" : "Wall", gold
                ? new Vector3(size.x / 2, size.y / 1.3f, size.z / 2)
                
                : new Vector3(size.x / 5, size.y / 2, size.z / 3));
            if (alongZ && model != null) model.transform.localRotation = Quaternion.Euler(0, 90, 0);
        }

        private static Vector3 BoxSize(Collider body) =>
            body is BoxCollider box ? Vector3.Scale(box.size, body.transform.lossyScale) : body.bounds.size;

        private static GameObject AddModel(Collider body, string key, Vector3 size)
        {
            var prefab = Resources.Load<GameObject>("MatchModels/" + key);
            if (prefab == null) return null;
            var root = body.transform;
            var model = Instantiate(prefab, root, false);
            model.name = key + " model";
            model.transform.localPosition = body is BoxCollider box ? box.center :
                body is CapsuleCollider capsule ? capsule.center : Vector3.zero;
            Vector3 scale = root.lossyScale;
            model.transform.localScale = new Vector3(size.x / scale.x, size.y / scale.y, size.z / scale.z);
            // Fog toggles Renderer.enabled. Keep the prototype mesh permanently suppressed.
            if (root.TryGetComponent(out Renderer original)) original.forceRenderingOff = true;
            return model;
        }

        private void LateUpdate()
        {
            if (operational != actor.IsOperational || upgraded != actor.Upgraded) Apply();
        }

        private void Apply()
        {
            operational = actor.IsOperational;
            upgraded = actor.Upgraded;
            SetVisible(finished, operational);
            SetVisible(buildingSite, !operational);
            SetVisible(badge, operational && upgraded);
        }

        private static void SetVisible(Renderer[] parts, bool visible)
        {
            if (parts == null) return;
            foreach (var part in parts) part.forceRenderingOff = !visible;
        }
    }
}

