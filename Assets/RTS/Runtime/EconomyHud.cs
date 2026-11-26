using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    internal sealed class EconomyHud
    {
        private readonly PrototypeEconomy economy;
        private readonly PrototypeConstruction construction;

        public EconomyHud(PrototypeEconomy economy, PrototypeConstruction construction)
        {
            this.economy = economy;
            this.construction = construction;
        }

        public bool Covers(HudLayout layout, Vector2 logicalPoint) =>
            layout.Resources.Contains(logicalPoint) || layout.Actions(construction != null).Contains(logicalPoint);

        public void Draw(HudLayout layout, CombatTarget building, GoldDeposit source,
            IReadOnlyList<PrototypeUnit> selected, UnitOrders orders)
        {
            Rect resources = layout.Resources;
            HudPresentation.Panel(resources);
            GUI.Box(resources, $"Золото {economy.Gold}    Населення {economy.Used} / {economy.Maximum}");
            if (economy.Reserved > 0)
            {
                GUI.Label(new Rect(resources.x + 12, resources.y + 23, resources.width - 24, 20),
                    $"У черзі: {economy.Reserved}");
            }

            Rect panel = layout.Actions(construction != null);
            HudPresentation.Panel(panel);
            GUI.Box(panel, GUIContent.none);
            GUI.BeginGroup(panel);
            if (building != null && building.IsAlive)
            {
                GUI.Label(new Rect(10, 4, 350, 22),
                    $"{HudPresentation.Name(building)}    {Mathf.CeilToInt(building.Health)} / {Mathf.CeilToInt(building.MaxHealth)} здоров’я");
                if (!building.IsOperational)
                {
                    string state = building.Site.Builder == null ? "призупинено" : "робітника призначено";
                    GUI.Label(new Rect(10, 31, 350, 22), $"Будівництво {building.Site.Progress:P0} · {state}");
                }
                int row = 0;
                foreach (PrototypeEconomy.Product product in economy.Products)
                {
                    if (!economy.CanProduce(building, product))
                    {
                        continue;
                    }

                    if (MatchUi.Button(new Rect(10, 30 + 27 * row++, 350, 25),
                            $"Навчити: {HudPresentation.Name(product.Kind)} · {product.Cost} золота · {product.Seconds:g} с"))
                    {
                        economy.Enqueue(building, product);
                    }
                }

                GUI.Label(new Rect(10, 112, 350, 22), economy.QueueSummary(building, localized: true));
                if (construction != null && building.Side == economy.Owner && building.IsOperational)
                {
                    var upgrade = new Rect(10, 139, 350, 25);
                    if (building.Upgraded) GUI.Label(upgrade, "Покращено");
                    else if (building.UpgradeRemaining > 0)
                        GUI.Label(upgrade, $"Покращення · {building.UpgradeRemaining:F1} с");
                    else if (MatchUi.Button(upgrade, $"Покращити · {economy.UpgradeCost} золота · {economy.UpgradeSeconds:g} с"))
                        economy.TryUpgrade(building);
                }
            }
            else if (source != null)
            {
                GUI.Label(new Rect(10, 4, 350, 22), $"Родовище золота · залишок {source.Stock}");
            }
            else if (selected.Count == 1)
            {
                PrototypeUnit unit = selected[0];
                var actor = unit.GetComponent<CombatTarget>();
                GUI.Label(new Rect(10, 4, 350, 22),
                    $"{HudPresentation.Name(unit.Kind)}    {Mathf.CeilToInt(actor.Health)} / {Mathf.CeilToInt(actor.MaxHealth)} здоров’я");
                GUI.Label(new Rect(10, 31, 350, 22), $"Наказ: {HudPresentation.Order(orders.StateOf(unit))}");
                string movement = $"Швидкість руху: {unit.MoveSpeed:G} од./с";
                string cargo = unit.Kind == UnitKind.Worker ? $"    Несе золота: {unit.CarriedGold}" : "";
                GUI.Label(new Rect(10, 58, 350, 22), movement + cargo);
                string attack = unit.CanAttack
                    ? $"Шкода за удар: {unit.Damage:G}    Дальність атаки: {unit.AttackRange:G} од."
                    : "Атака недоступна";
                GUI.Label(new Rect(10, 81, 350, 22), attack);
                if (unit.Kind == UnitKind.Worker)
                {
                    DrawBlueprints(unit);
                }
            }
            else if (selected.Count > 1)
            {
                GUI.Label(new Rect(10, 4, 350, 22), $"Виділено юнітів: {selected.Count}");
            }

            if (!string.IsNullOrEmpty(economy.Message))
            {
                GUI.Label(new Rect(10, panel.height - 29, 350, 22), HudPresentation.Message(economy.Message));
            }
            GUI.EndGroup();
        }

        private void DrawBlueprints(PrototypeUnit worker)
        {
            if (construction == null)
            {
                return;
            }

            int row = 0;
            foreach (PrototypeConstruction.Blueprint blueprint in construction.Blueprints)
            {
                string name = HudPresentation.Name(blueprint.Kind);
                if (MatchUi.Button(new Rect(10, 106 + row++ * 25, 350, 23),
                        $"Збудувати: {name} · {blueprint.Cost} золота · {blueprint.Seconds:g} с"))
                {
                    construction.Begin(blueprint, worker);
                }
            }

            if (construction.IsPlacing)
            {
                GUI.Label(new Rect(10, 182, 350, 24), "Обери місце · Esc / ПКМ — скасувати");
            }
        }
    }
}
