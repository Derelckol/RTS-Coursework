using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rts
{
    internal sealed class DisplaySettings : MonoBehaviour
    {
        private const string Prefix = "Rts.Display.";
        private const float ConfirmationSeconds = 12f;
        private readonly List<Vector2Int> resolutions = new();
        private FullScreenMode selectedMode, previousMode;
        private Vector2Int selectedSize, previousSize;
        private float expires;
        private int closedFrame = -1;
        private bool previewing, applying, initialized;
        internal static DisplaySettings Instance { get; private set; }
        internal bool IsOpen { get; private set; }
        internal bool BlocksWorldInput => IsOpen || Time.frameCount == closedFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
            Instance = new GameObject("Display settings").AddComponent<DisplaySettings>();
            DontDestroyOnLoad(Instance.gameObject);
        }

        private IEnumerator Start()
        {
            yield return null;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var mode = PlayerPrefs.GetInt(Prefix + "Fullscreen",0) == 1
                ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var size = new Vector2Int(PlayerPrefs.GetInt(Prefix + "Width",1366),
                PlayerPrefs.GetInt(Prefix + "Height",768));
            yield return ApplyMode(mode,size);
#endif
            initialized = true;
        }

        internal void Open()
        {
            if (!initialized || applying) return;
            selectedMode = Screen.fullScreenMode == FullScreenMode.Windowed
                ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            selectedSize = new Vector2Int(Screen.width,Screen.height);
            UpdateResolutions();
            IsOpen = true;
        }

        private static RectInt WorkArea()
        {
            var display = Screen.mainWindowDisplayInfo;
            return display.workArea.width > 0 && display.workArea.height > 0
                ? display.workArea : new RectInt(0,0,Screen.currentResolution.width,Screen.currentResolution.height);
        }

        private static Vector2Int Fit(Vector2Int size, FullScreenMode mode)
        {
            var display = Screen.mainWindowDisplayInfo;
            RectInt work = WorkArea();
            // Leave room for the title bar and resize borders at higher desktop scales.
            int width = mode == FullScreenMode.Windowed ? work.width - 48 : display.width;
            int height = mode == FullScreenMode.Windowed ? work.height - 90 : display.height;
            if (width <= 0) width = Screen.currentResolution.width;
            if (height <= 0) height = Screen.currentResolution.height;
            size.x = Mathf.Max(1,size.x);
            size.y = Mathf.Max(1,size.y);
            float fit = Mathf.Min(1f,width / (float)size.x,height / (float)size.y);
            return new Vector2Int(Mathf.Max(1,Mathf.FloorToInt(size.x*fit)),Mathf.Max(1,Mathf.FloorToInt(size.y*fit)));
        }

        private void UpdateResolutions()
        {
            resolutions.Clear();
            foreach (var size in new[] {new Vector2Int(960,540),new Vector2Int(1024,768),new Vector2Int(1280,720),
                new Vector2Int(1366,768),new Vector2Int(1600,900),new Vector2Int(1920,1080),
                new Vector2Int(2560,1440),new Vector2Int(3840,2160),selectedSize})
            {
                var fitted = Fit(size,selectedMode);
                if (!resolutions.Contains(fitted)) resolutions.Add(fitted);
            }
            resolutions.Sort((a,b) => (a.x*a.y).CompareTo(b.x*b.y));
            selectedSize = Fit(selectedSize,selectedMode);
        }

        private IEnumerator ApplyMode(FullScreenMode mode, Vector2Int size, bool restore = false)
        {
            applying = true;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var fitted = restore ? size : Fit(size,mode);
            Screen.SetResolution(fitted.x,fitted.y,mode);
            yield return null;
            yield return null;
            if (mode == FullScreenMode.Windowed)
            {
                var display = Screen.mainWindowDisplayInfo;
                RectInt work = WorkArea();
                var centered = new Vector2Int(
                    work.x + Mathf.Max(0,(work.width-Screen.width-24)/2),
                    work.y + Mathf.Max(0,(work.height-Screen.height-55)/2));
                var move = Screen.MoveMainWindowTo(display,centered);
                if (move != null) yield return move;
            }
#else
            yield return null;
#endif
            applying = false;
        }

        private IEnumerator Preview()
        {
            previousMode = Screen.fullScreenMode;
            previousSize = new Vector2Int(Screen.width,Screen.height);
            yield return ApplyMode(selectedMode,selectedSize);
            expires = Time.realtimeSinceStartup + ConfirmationSeconds;
            previewing = true;
        }

        private void Revert()
        {
            if (!previewing || applying) return;
            previewing = false;
            selectedMode = previousMode;
            selectedSize = previousSize;
            UpdateResolutions();
            StartCoroutine(ApplyMode(previousMode,previousSize,restore: true));
        }

        private void Keep()
        {
            previewing = false;
            PlayerPrefs.SetInt(Prefix + "Fullscreen",selectedMode == FullScreenMode.Windowed ? 0 : 1);
            PlayerPrefs.SetInt(Prefix + "Width",Screen.width);
            PlayerPrefs.SetInt(Prefix + "Height",Screen.height);
            PlayerPrefs.Save();
            selectedSize = new Vector2Int(Screen.width,Screen.height);
            UpdateResolutions();
        }

        private void Update()
        {
            if (!IsOpen || applying) return;
            if (previewing && Time.realtimeSinceStartup >= expires) Revert();
            else if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                if (previewing) Revert();
                else Close();
            }
        }

        internal void Draw(HudLayout layout)
        {
            if (!IsOpen) return;
            var screen = layout.FullScreen;
            var panel = new Rect(screen.center.x-255,screen.center.y-145,510,290);
            MatchUi.Shade(screen);
            MatchUi.Window(panel,"Налаштування екрана");
            if (applying)
            {
                GUI.Label(new Rect(panel.x+25,panel.y+66,460,45),"Застосування налаштувань…",MatchUi.Paragraph);
                return;
            }
            if (previewing)
            {
                int seconds = Mathf.CeilToInt(Mathf.Max(0,expires-Time.realtimeSinceStartup));
                GUI.Label(new Rect(panel.x+25,panel.y+64,460,76),
                    $"Залишити цей режим? Попередні налаштування повернуться через {seconds} с.",MatchUi.Paragraph);
                if (MatchUi.Button(new Rect(panel.x+25,panel.y+171,216,42),"Підтвердити",highlighted:true)) Keep();
                if (MatchUi.Button(new Rect(panel.x+269,panel.y+171,216,42),"Повернути")) Revert();
                return;
            }
            GUI.Label(new Rect(panel.x+25,panel.y+55,460,24),"Режим екрана");
            if (MatchUi.Button(new Rect(panel.x+25,panel.y+83,216,40),"Віконний",
                    highlighted:selectedMode==FullScreenMode.Windowed))
            {
                selectedMode=FullScreenMode.Windowed;
                UpdateResolutions();
            }
            if (MatchUi.Button(new Rect(panel.x+269,panel.y+83,216,40),"Повноекранний",
                    highlighted:selectedMode!=FullScreenMode.Windowed))
            {
                selectedMode=FullScreenMode.FullScreenWindow;
                UpdateResolutions();
            }
            GUI.Label(new Rect(panel.x+25,panel.y+133,460,24),"Роздільність");
            if (MatchUi.Button(new Rect(panel.x+25,panel.y+158,62,38),"‹")) Step(-1);
            GUI.Label(new Rect(panel.x+103,panel.y+168,300,23),$"{selectedSize.x} × {selectedSize.y}",MatchUi.Paragraph);
            if (MatchUi.Button(new Rect(panel.x+423,panel.y+158,62,38),"›")) Step(1);
            if (MatchUi.Button(new Rect(panel.x+25,panel.y+222,216,40),"Застосувати",highlighted:true))
                StartCoroutine(Preview());
            if (MatchUi.Button(new Rect(panel.x+269,panel.y+222,216,40),"Назад")) Close();
        }

        private void Close()
        {
            IsOpen = false;
            closedFrame = Time.frameCount;
        }

        private void Step(int offset)
        {
            int i = resolutions.IndexOf(selectedSize);
            selectedSize = resolutions[(i+offset+resolutions.Count)%resolutions.Count];
        }
    }
}
