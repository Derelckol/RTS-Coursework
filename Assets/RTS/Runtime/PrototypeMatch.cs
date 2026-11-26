using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public enum MatchOutcome { Ongoing, Victory, Defeat, Draw }

    public sealed partial class PrototypeMatch : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float defenseDetectionRange = 7.5f;
        [SerializeField, Min(0.1f)] private float defenseLeash = 10f;
        [SerializeField, Min(0.1f)] private float defenseScanInterval = 0.45f;
        [SerializeField, Min(1f)] private float attackAlertCooldown = 20f;
        [SerializeField, Min(0.1f)] private float baseAlertRange = 9f;
        private bool restarting;
        private bool returningToMenu;
        private bool confirmingReturn;
        private DifficultyProfile difficulty;
        private int startFrame = -1;
        private float elapsed;
        private float nextAttackAlert;
        private float attackAlertUntil;
        public MatchOutcome Outcome { get; private set; }
        public bool HasEnded => Outcome != MatchOutcome.Ongoing;
        public float ElapsedSeconds => elapsed;
        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" :
                $"{total / 60}:{total % 60:00}";
        }
        public bool HasStarted { get; private set; }
        public DifficultyProfile Difficulty => difficulty ??= MatchSession.Profile(MatchSession.Selected);
        public bool BlocksSimulation => !HasStarted || HasEnded || returningToMenu;
        public bool BlocksWorldInput => BlocksSimulation || Time.frameCount == startFrame;
        public float DefenseDetectionRange => Mathf.Min(defenseDetectionRange, defenseLeash);
        public float DefenseLeash => defenseLeash;
        public float DefenseScanInterval => Mathf.Max(0.1f, defenseScanInterval);
        public float FirstBaseAttackAt { get; private set; } = -1f;
        private bool ShowAlert => elapsed < attackAlertUntil;

        public void Tick(float deltaTime) => elapsed += deltaTime;

        public void StartMatch()
        {
            if (!HasStarted && !HasEnded)
            {
                HasStarted = true;
                startFrame = Time.frameCount;
            }
        }

        public void RegisterBaseHit(CombatTarget victim, IReadOnlyList<CombatTarget> actors)
        {
            if (elapsed < nextAttackAlert)
            {
                return;
            }

            if (victim.Building == BuildingKind.None)
            {
                bool atBase = false;
                foreach (CombatTarget actor in actors)
                {
                    if (actor != null && actor.IsAlive && actor.Side == Side.Player &&
                        actor.Building != BuildingKind.None &&
                        actor.DistanceFrom(victim.transform.position, victim.Unit.Radius) <= baseAlertRange)
                    {
                        atBase = true;
                        break;
                    }
                }

                if (!atBase)
                {
                    return;
                }
            }

            if (FirstBaseAttackAt < 0f)
            {
                FirstBaseAttackAt = elapsed;
            }
            nextAttackAlert = elapsed + attackAlertCooldown;
            attackAlertUntil = elapsed + 5f;
        }

        private static Rect Alert(HudLayout layout) =>
            new Rect((layout.FullScreen.width - 246f) * 0.5f, 63f, 246f, 30f);

        public bool CoversGuidance(HudLayout layout, Vector2 point) =>
            (ShowAlert && Alert(layout).Contains(point)) ||
            new Rect(layout.FullScreen.width * 0.5f - 56f, 56, 112, 25).Contains(point);

        public void DrawGuidance(HudLayout layout)
        {
            if (ShowAlert)
            {
                GUI.Box(Alert(layout), "На вашу базу напали!");
            }
        }

        public void DrawClock(HudLayout layout) =>
            GUI.Box(new Rect(layout.FullScreen.width * 0.5f - 56f, 56, 112f, 25f), FormatTime(elapsed));

        public void DrawIntro(HudLayout layout)
        {
            Rect screen = layout.FullScreen;
            float width = Mathf.Min(570f, screen.width - 24f);
            var panel = new Rect((screen.width - width) * 0.5f, (screen.height - 236f) * 0.5f, width, 236f);
            DrawOverlay(screen, panel);
            MatchUi.Window(panel, "Навчальний матч");
            var instruction = MatchUi.Paragraph;
            GUI.Label(new Rect(panel.x + 20, panel.y + 48, width - 40, 38),
                "Золото: вибери робітника й клацни правою кнопкою по родовищу.", instruction);
            GUI.Label(new Rect(panel.x + 20, panel.y + 85, width - 40, 38),
                "Військо: збудуй казарму, вибери її й замов юнітів.", instruction);
            GUI.Label(new Rect(panel.x + 20, panel.y + 122, width - 40, 38),
                "Перемога: знищ усі завершені ворожі ратуші.", instruction);
            if (MatchUi.Button(new Rect(panel.x + 20, panel.y + 175, width - 40, 38), "Почати гру", highlighted: true))
            {
                StartMatch();
            }
        }

        public bool CancelMenuConfirmation()
        {
            if (!confirmingReturn)
            {
                return false;
            }

            confirmingReturn = false;
            return true;
        }

        public void DrawPause(HudLayout layout, System.Action<bool> setPaused)
        {
            Rect screen = layout.FullScreen;
            var panel = new Rect((screen.width - 364) * 0.5f, (screen.height - 382) * 0.5f, 364, 382);
            DrawOverlay(screen, panel);
            MatchUi.Window(panel, confirmingReturn ? "Повернутися в головне меню?" : "Пауза");
            if (confirmingReturn)
            {
                GUI.Label(new Rect(panel.x + 26, panel.y + 46, 312, 26), "Незбережений прогрес буде втрачено.");
                if (MatchUi.Button(new Rect(panel.x + 26, panel.y + 92, 312, 40), "Вийти в головне меню"))
                {
                    ConfirmMenuReturn();
                }
                if (MatchUi.Button(new Rect(panel.x + 26, panel.y + 144, 312, 40), "Залишитися в матчі"))
                {
                    CancelMenuConfirmation();
                }
                return;
            }

            GUI.Label(new Rect(panel.x + 26, panel.y + 43, 312, 26), $"Складність: {Difficulty.Title}");
            if (MatchUi.Button(new Rect(panel.x + 26, panel.y + 83, 312, 40), "Продовжити", highlighted: true))
            {
                setPaused(false);
            }
            if (MatchUi.Button(new Rect(panel.x + 26, panel.y + 135, 312, 40), "Зберегти гру"))
                GetComponent<PrototypeControls>().OpenSaveWindow(true);
            if (MatchUi.Button(new Rect(panel.x + 26, panel.y + 187, 312, 40), "Завантажити гру"))
                GetComponent<PrototypeControls>().OpenSaveWindow(false);
            if (MatchUi.Button(new Rect(panel.x + 26, panel.y + 239, 312, 40), "Налаштування екрана"))
                DisplaySettings.Instance?.Open();
            if (MatchUi.Button(new Rect(panel.x + 26, panel.y + 291, 312, 40), "Головне меню"))
            {
                OpenMenuConfirmation();
            }
        }

        private void OpenMenuConfirmation() => confirmingReturn = true;

        private void ConfirmMenuReturn()
        {
            if (confirmingReturn)
            {
                GoToMenu();
            }
        }

        private void GoToMenu()
        {
            if (returningToMenu || restarting)
            {
                return;
            }
            returningToMenu = true;
            MatchSession.ReturnToMenu();
        }

        public void Evaluate(IReadOnlyList<CombatTarget> actors)
        {
            if (HasEnded)
            {
                return;
            }

            bool player = false;
            bool opponent = false;
            foreach (CombatTarget actor in actors)
            {
                if (!actor.IsAlive || !actor.IsOperational || actor.Building != BuildingKind.TownHall)
                {
                    continue;
                }

                player |= actor.Side == Side.Player;
                opponent |= actor.Side == Side.Opponent;
            }

            if (!player || !opponent)
            {
                Outcome = player ? MatchOutcome.Victory : opponent ? MatchOutcome.Defeat : MatchOutcome.Draw;
            }
        }

        public void DrawResult(HudLayout layout)
        {
            if (!HasEnded)
            {
                return;
            }

            Rect screen = layout.FullScreen;
            var panel = new Rect((screen.width - 292) * 0.5f, (screen.height - 188) * 0.5f, 292, 188);
            DrawOverlay(layout.FullScreen, panel);
            string result = Outcome switch
            {
                MatchOutcome.Victory => "Перемога",
                MatchOutcome.Defeat => "Поразка",
                _ => "Нічия"
            };
            MatchUi.Window(panel, result);
            if (MatchUi.Button(new Rect(panel.x + 34, panel.y + 55, panel.width - 68, 37), "Зіграти ще раз", highlighted: true))
            {
                Restart();
            }
            if (MatchUi.Button(new Rect(panel.x + 34, panel.y + 104, panel.width - 68, 37), "Головне меню"))
            {
                GoToMenu();
            }
        }

        private static void DrawOverlay(Rect screen, Rect panel)
        {
            MatchUi.Shade(screen);
        }

        public void Restart()
        {
            if (restarting || returningToMenu)
            {
                return;
            }

            restarting = true;
            MatchSession.ReplayMatch();
        }
    }
}
