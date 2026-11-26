using UnityEngine;

namespace Rts
{
    internal static class HudPresentation
    {
        internal static void Panel(Rect rect)
        {
            var previous = GUI.color;
            GUI.color = new Color(.09f, .125f, .155f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        internal static string Name(CombatTarget target) =>
            target.Unit == null ? Name(target.Building) : Name(target.Unit.Kind);

        internal static string Name(UnitKind kind) => kind switch
        {
            UnitKind.Worker => "Робітник",
            UnitKind.Infantry => "Піхотинець",
            UnitKind.Archer => "Стрілець",
            UnitKind.Cavalry => "Вершник",
            _ => "Юніт"
        };

        internal static string Name(BuildingKind kind) => kind switch
        {
            BuildingKind.TownHall => "Ратуша",
            BuildingKind.Barracks => "Казарма",
            BuildingKind.House => "Будинок",
            _ => "Споруда"
        };

        internal static string Order(UnitState state) => state switch
        {
            UnitState.Move => "Рух",
            UnitState.Attack => "Атака",
            UnitState.Repair => "Ремонт",
            UnitState.Gather => "Видобуток",
            UnitState.Deliver => "Доставка золота",
            UnitState.Build => "Будівництво",
            UnitState.Defend or UnitState.Respond => "Оборона",
            UnitState.ReturnToPost => "Повернення на позицію",
            UnitState.Breach or UnitState.ResumeBreach => "Пробивання проходу",
            _ => "Очікування"
        };

        internal static string Message(string message) => message switch
        {
            "Not enough gold." => "Недостатньо золота.",
            "Population limit reached." => "Досягнуто ліміту населення.",
            "Cannot build here." => "Тут не можна будувати.",
            _ => message
        };
    }
}
