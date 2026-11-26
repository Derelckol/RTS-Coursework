using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public sealed class MatchUnitAnimation : MonoBehaviour
    {
        private PrototypeUnit unit;
        private PrototypeControls controls;
        private readonly List<(Transform joint, float sign)> legs = new();
        private Transform leftArm, rightArm, rider, arrow;
        private Renderer[] bag, pick, mallet;
        private Renderer arrowRenderer;
        private Vector3 lastPosition, riderRest, arrowRest;
        private float lastTime, stride, lastStrike = float.NegativeInfinity;
        private UnitState strikeOrder;

        internal void Initialize(PrototypeUnit owner, PrototypeControls context)
        {
            unit = owner;
            controls = context;
            lastPosition = unit.transform.position;
            lastTime = controls.AnimationTime;
            var parts = new List<Transform>();
            foreach (Transform part in transform) parts.Add(part);

            bool mounted = unit.Kind == UnitKind.Cavalry;
            if (mounted)
            {
                rider = Joint(transform, "Rider pivot", Vector3.zero);
                riderRest = rider.localPosition;
            }
            Transform body = mounted ? rider : transform;
            float shoulder = mounted ? .80f : .23f;
            leftArm = Joint(body, "Left shoulder", new Vector3(-.25f,shoulder,0));
            rightArm = Joint(body, "Right shoulder", new Vector3(.25f,shoulder,0));

            var bags = new List<Renderer>();
            var picks = new List<Renderer>();
            var mallets = new List<Renderer>();
            foreach (var part in parts)
            {
                var point = part.localPosition;
                string name = part.name;
                if (name == "Horse leg" || name == "Boot" && !mounted)
                {
                    var pivot = Joint(transform, "Leg pivot", point + Vector3.up * .16f);
                    MovePart(part, pivot);
                    float sign = Mathf.Sign(point.x) * (name == "Horse leg" ? Mathf.Sign(point.z) : 1);
                    legs.Add((pivot,sign));
                    continue;
                }
                if (name.StartsWith("Satchel") || name == "Supply satchel") bags.Add(part.GetComponent<Renderer>());
                if (name.StartsWith("Pick")) picks.Add(part.GetComponent<Renderer>());
                if (name.StartsWith("Mallet")) mallets.Add(part.GetComponent<Renderer>());
                if (name == "Arrow")
                {
                    MovePart(part,rightArm);
                    arrow = part;
                    arrowRest = part.localPosition;
                    arrowRenderer = part.GetComponent<Renderer>();
                }
                else if (name.StartsWith("Shield") || name == "Broad shield" || name == "Sleeve" && point.x < 0)
                    MovePart(part,leftArm);
                else if (name == "Sword" || name == "Sleeve" || name.Contains("bow limb") ||
                    name.StartsWith("Bow") || name.StartsWith("Pick") || name.StartsWith("Mallet"))
                    MovePart(part,rightArm);
                else if (mounted && name is "Tunic" or "Face" or "Headwear" or "Boot")
                    MovePart(part,rider);
            }
            bag = bags.ToArray();
            pick = picks.ToArray();
            mallet = mallets.ToArray();
            UpdateEquipment(UnitState.Idle);
            if (arrowRenderer != null) arrowRenderer.forceRenderingOff = true;
            unit.CombatMotion += OnStrike;
        }

        private static Transform Joint(Transform parent, string name, Vector3 position)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent,false);
            pivot.localPosition = position;
            return pivot;
        }

        private static void MovePart(Transform part, Transform pivot)
        {
            // Both joints and components are in model space at setup.
            Vector3 point = part.localPosition;
            Quaternion rotation = part.localRotation;
            part.SetParent(pivot,false);
            part.localPosition = point - pivot.localPosition;
            part.localRotation = rotation;
        }

        private void OnStrike()
        {
            lastStrike = controls.AnimationTime;
            strikeOrder = controls.AnimationOrder(unit);
        }

        private void LateUpdate()
        {
            if (unit == null || controls.AnimationFrozen) return;
            float now = controls.AnimationTime;
            if (now <= lastTime) return;

            Vector3 displacement = unit.transform.position - lastPosition;
            displacement.y = 0;
            bool moving = displacement.sqrMagnitude > .000001f;
            var order = controls.AnimationOrder(unit);
            var focus = controls.AnimationFocus(unit, out bool working);
            if (order != strikeOrder) lastStrike = float.NegativeInfinity;
            Vector3 facing = moving ? displacement : focus.HasValue ? focus.Value - unit.transform.position : Vector3.zero;
            facing.y = 0;
            if (facing.sqrMagnitude > .000001f)
            {
                var direction = Quaternion.LookRotation(-facing);
                transform.rotation = Quaternion.RotateTowards(transform.rotation,direction,900 * (now-lastTime));
            }

            if (moving) stride += displacement.magnitude * (unit.Kind == UnitKind.Cavalry ? 5.5f : 10f);
            float step = moving ? Mathf.Sin(stride) : 0;
            foreach (var leg in legs) leg.joint.localRotation = Quaternion.Euler(step * leg.sign * 29,0,0);
            if (rider != null) rider.localPosition = riderRest + Vector3.up * (moving ? Mathf.Abs(step)*.075f : 0);

            float elapsed = now - lastStrike;
            float action = elapsed < .38f ? Mathf.Sin(Mathf.Clamp01(elapsed/.38f)*Mathf.PI) : 0;
            float work = working ? .45f + .55f * Mathf.Sin(now*14f) : 0;
            leftArm.localRotation = Quaternion.Euler(rider == null ? -step*22 : 0,0,0);
            rightArm.localRotation = Quaternion.Euler(rider == null ? step*22 : 0,0,0);
            if (!moving && (action > 0 || working))
            {
                float lift = Mathf.Max(action, work);
                rightArm.localRotation = Quaternion.Euler(-lift*(unit.Kind == UnitKind.Archer ? 68 : 105),0,0);
                if (unit.Kind == UnitKind.Archer) leftArm.localRotation = Quaternion.Euler(-lift*56,0,0);
            }
            if (arrowRenderer != null)
            {
                arrowRenderer.forceRenderingOff = elapsed >= .28f || moving;
                arrow.localPosition = arrowRest + Vector3.back * (elapsed < .28f ? Mathf.Max(0,elapsed-.13f)*4f : 0);
            }
            UpdateEquipment(order);
            lastPosition = unit.transform.position;
            lastTime = now;
        }

        private void UpdateEquipment(UnitState order)
        {
            SetShown(bag,unit.CarriedGold > 0);
            bool hammer = order == UnitState.Build || order == UnitState.Repair;
            SetShown(pick,!hammer);
            SetShown(mallet,hammer);
        }

        private static void SetShown(Renderer[] renderers, bool shown)
        {
            foreach (var part in renderers) part.forceRenderingOff = !shown;
        }

        private void OnDestroy()
        {
            if (unit != null) unit.CombatMotion -= OnStrike;
        }
    }
}

