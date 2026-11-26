using System;
using UnityEngine;

namespace Rts
{
    public sealed partial class ConstructionSite
    {
        private readonly float duration;
        private readonly Action<CombatTarget> onComplete;
        private float elapsed;

        public CombatTarget Target { get; private set; }
        public PrototypeUnit Builder { get; set; }
        public float WorkRange { get; }
        public bool Completed { get; private set; }
        public float Progress => Mathf.Clamp01(elapsed / duration);

        public ConstructionSite(float duration, float workRange, Action<CombatTarget> onComplete)
        {
            this.duration = Mathf.Max(0.01f, duration);
            this.onComplete = onComplete;
            WorkRange = workRange;
        }

        public void Attach(CombatTarget target) => Target = target;

        public void Advance(float deltaTime)
        {
            if (Completed || !Target.IsAlive)
            {
                return;
            }

            elapsed = Mathf.Min(duration, elapsed + deltaTime);
            if (elapsed >= duration)
            {
                Completed = true;
                Builder = null;
                onComplete(Target);
            }
        }
    }
}
