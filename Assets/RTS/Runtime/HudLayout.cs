using UnityEngine;

namespace Rts
{
    public readonly struct HudLayout
    {
        private readonly int pixelHeight;
        private readonly float width;
        private readonly float height;

        public float Scale { get; }
        public Rect FullScreen => new Rect(0, 0, width, height);
        public Rect Resources => new Rect((width - 340) * 0.5f, 8, 340, 46);
        public Rect Menu => new Rect(width - 122, 8, 114, 34);
        public Rect PausePanel => new Rect((width - 292) * 0.5f, (height - 146) * 0.5f, 292, 146);

        public HudLayout(int pixelWidth, int pixelHeight, bool scalable)
        {
            this.pixelHeight = pixelHeight;
            Scale = scalable
                ? Mathf.Clamp(Mathf.Min(pixelWidth / 1366f, pixelHeight / 768f), 0.01f, 1.5f)
                : 1f;
            width = pixelWidth / Scale;
            height = pixelHeight / Scale;
        }

        public Rect Actions(bool construction)
        {
            float panelHeight = construction ? 244 : 166;
            return new Rect(8, height - panelHeight - 8, 374, panelHeight);
        }

        public Rect MinimapPanel(RectInt bounds)
        {
            const float panelWidth = 260;
            float panelHeight = MapHeight(bounds) + 50;
            return new Rect(width - panelWidth - 8, height - panelHeight - 8, panelWidth, panelHeight);
        }

        public Rect MinimapContent(RectInt bounds)
        {
            Rect panel = MinimapPanel(bounds);
            return new Rect(panel.x + 8, panel.y + 23, 244, MapHeight(bounds));
        }

        public Vector2 FromScreen(Vector2 screenPoint)
        {
            return new Vector2(screenPoint.x / Scale, (pixelHeight - screenPoint.y) / Scale);
        }

        private static float MapHeight(RectInt bounds) => 244f * bounds.height / bounds.width;
    }
}
