using UnityEngine;

namespace Rts
{
    public sealed partial class GoldDeposit : MonoBehaviour
    {
        [SerializeField, Min(0)] private int initialStock = 180;
        public int Stock { get; private set; }
        public Collider Body { get; private set; }

        private void Awake() => Initialize();

        public void Initialize()
        {
            Body = GetComponent<Collider>();
            Stock = initialStock;
        }

        public int Take(int amount)
        {
            int taken = Mathf.Clamp(amount, 0, Stock);
            Stock -= taken;
            return taken;
        }
    }
}
