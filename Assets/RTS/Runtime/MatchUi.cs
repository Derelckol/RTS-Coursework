using UnityEngine;

namespace Rts
{
    internal static class MatchUi
    {
        private static readonly Color Ink = new Color32(234, 232, 218, 255);
        private static readonly Color Gold = new Color32(224, 185, 109, 255);
        private static readonly Color Panel = new Color32(22, 28, 31, 255);
        private static GUIStyle button, primary, inactive, field, heading, paragraph, row, selectedRow;

        private static Texture2D Surface(Color edge, Color fill)
        {
            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(new[] {edge,edge,edge,edge,fill,edge,edge,edge,edge});
            texture.Apply(false, true);
            return texture;
        }

        private static void State(GUIStyleState state, Color edge, Color fill, Color text)
        {
            state.background = Surface(edge,fill);
            state.textColor = text;
        }

        private static GUIStyle ButtonStyle(bool highlighted, bool disabled = false, bool multiline = false)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = highlighted ? FontStyle.Bold : FontStyle.Normal,
                alignment = multiline ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                wordWrap = multiline,
                clipping = TextClipping.Clip,
                padding = new RectOffset(10,10,2,2),
                border = new RectOffset(1,1,1,1)
            };
            Color edge = highlighted ? Gold : new Color32(100, 94, 79, 255);
            Color baseColor = highlighted ? new Color32(77, 60, 34, 255) : new Color32(42, 49, 50, 255);
            if (disabled)
            {
                State(style.normal,new Color32(57,61,59,255),new Color32(29,34,35,255),new Color32(157,161,156,255));
                return style;
            }
            State(style.normal,edge,baseColor,Ink);
            State(style.hover,Gold,new Color32(82,73,52,255),Color.white);
            State(style.active,Gold,new Color32(57,44,26,255),Gold);
            State(style.focused,Gold,baseColor,Ink);
            return style;
        }

        internal static bool Button(Rect rect, string text, bool highlighted = false, bool multiline = false)
        {
            bool enabled = GUI.enabled;
            if (!enabled)
            {
                inactive ??= ButtonStyle(false,true);
                // A disabled control is drawn without auto-dimming or accepting input.
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.enabled = true;
                    try { inactive.Draw(rect,new GUIContent(text),false,false,false,false); }
                    finally { GUI.enabled = enabled; }
                }
                return false;
            }
            if (multiline)
            {
                row ??= ButtonStyle(false,multiline:true);
                selectedRow ??= ButtonStyle(true,multiline:true);
                return GUI.Button(rect,text,highlighted ? selectedRow : row);
            }
            button ??= ButtonStyle(false);
            primary ??= ButtonStyle(true);
            return GUI.Button(rect,text,highlighted ? primary : button);
        }

        internal static string Field(Rect rect, string text, int limit)
        {
            if (field == null)
            {
                field = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = 15, alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(9,9,3,3), border = new RectOffset(1,1,1,1)
                };
                State(field.normal,new Color32(111,106,91,255),new Color32(14,18,20,255),Ink);
                State(field.hover,Gold,new Color32(19,23,24,255),Ink);
                State(field.focused,Gold,new Color32(14,18,20,255),Color.white);
                State(field.active,Gold,new Color32(14,18,20,255),Color.white);
            }
            return GUI.TextField(rect,text,limit,field);
        }

        internal static GUIStyle Paragraph
        {
            get
            {
                paragraph ??= new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14, wordWrap = true, alignment = TextAnchor.UpperLeft,
                    normal = { textColor = Ink }
                };
                return paragraph;
            }
        }

        internal static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect,Texture2D.whiteTexture);
            GUI.color = previous;
        }

        internal static void Window(Rect rect, string title)
        {
            heading ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Ink }
            };
            Fill(rect,Panel);
            Fill(new Rect(rect.x,rect.y,rect.width,2),Gold);
            Fill(new Rect(rect.x+20,rect.y+39,rect.width-40,1),new Color32(84,75,56,255));
            GUI.Label(new Rect(rect.x+8,rect.y+4,rect.width-16,34),title,heading);
        }

        internal static void Shade(Rect screen) => Fill(screen,new Color(.035f,.047f,.049f,.89f));
    }
}

