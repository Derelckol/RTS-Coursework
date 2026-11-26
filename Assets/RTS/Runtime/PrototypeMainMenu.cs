using UnityEngine;

namespace Rts
{
    public sealed class PrototypeMainMenu : MonoBehaviour
    {
        private static readonly DifficultyLevel[] Levels =
            { DifficultyLevel.Easy, DifficultyLevel.Medium, DifficultyLevel.Hard };
        private readonly DifficultyProfile[] profiles = new DifficultyProfile[Levels.Length];
        private bool choosingDifficulty;
        private bool leaving;
        private DifficultyLevel selected;
        private readonly SaveWindow saveWindow = new SaveWindow();

        private void Awake()
        {
            for (int index = 0; index < Levels.Length; index++)
            {
                profiles[index] = MatchSession.Profile(Levels[index]);
            }
        }

        private void OnGUI()
        {
            var layout = new HudLayout(Screen.width, Screen.height, true);
            Matrix4x4 previousMatrix = GUI.matrix;
            bool previousEnabled = GUI.enabled;
            GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(layout.Scale, layout.Scale, 1f));
            GUI.enabled = previousEnabled && !leaving;
            try
            {
                if (DisplaySettings.Instance?.IsOpen == true) { DisplaySettings.Instance.Draw(layout); return; }
                if (saveWindow.IsOpen) { saveWindow.Draw(layout); return; }
                Rect screen = layout.FullScreen;
                float height = choosingDifficulty ? 400f : 346f;
                float width = Mathf.Min(540f, screen.width - 32f);
                var panel = new Rect((screen.width - width) * 0.5f, (screen.height - height) * 0.5f, width, height);
                MatchUi.Shade(screen);
                MatchUi.Window(panel, choosingDifficulty ? "Складність" : "Стратегія в реальному часі");
                if (choosingDifficulty)
                {
                    DrawChoices(panel);
                }
                else
                {
                    DrawHome(panel);
                }
            }
            finally
            {
                GUI.enabled = previousEnabled;
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawHome(Rect panel)
        {
            if (MatchUi.Button(new Rect(panel.x + 48, panel.y + 85, panel.width - 96, 42), "Нова гра", highlighted: true))
            {
                OpenDifficulty();
            }

            if (MatchUi.Button(new Rect(panel.x + 48, panel.y + 142, panel.width - 96, 42), "Завантажити гру"))
                saveWindow.Open(false);
            if (MatchUi.Button(new Rect(panel.x + 48, panel.y + 199, panel.width - 96, 42), "Налаштування екрана"))
                DisplaySettings.Instance?.Open();
            if (MatchUi.Button(new Rect(panel.x + 48, panel.y + 256, panel.width - 96, 42), "Вийти"))
            {
                Application.Quit();
            }
        }

        private void OpenDifficulty()
        {
            choosingDifficulty = true;
            selected = DifficultyLevel.Easy;
        }

        private void DrawChoices(Rect panel)
        {
            for (int index = 0; index < Levels.Length; index++)
            {
                float y = panel.y + 76 + index * 83;
                if (MatchUi.Button(new Rect(panel.x + 30, y, panel.width - 60, 32),
                    profiles[index].Title, highlighted: Levels[index] == selected))
                    selected = Levels[index];
                GUI.Label(new Rect(panel.x + 34, y + 34, panel.width - 68, 42), profiles[index].Description, MatchUi.Paragraph);
            }

            float half = (panel.width - 72) * 0.5f;
            if (MatchUi.Button(new Rect(panel.x + 30, panel.yMax - 61, half, 38), "Назад"))
            {
                choosingDifficulty = false;
            }
            if (MatchUi.Button(new Rect(panel.x + 42 + half, panel.yMax - 61, half, 38), "Почати", highlighted: true))
            {
                LaunchSelected();
            }
        }

        private void LaunchSelected()
        {
            leaving = true;
            MatchSession.StartNewMatch(selected);
        }
    }
}
