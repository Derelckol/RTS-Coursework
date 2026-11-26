using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Rts
{
    public sealed class SaveWindow
    {
        private sealed class Entry { public string path, error; public MatchSave data; }
        private readonly List<Entry> entries = new List<Entry>();
        private PrototypeControls controls;
        private bool saving;
        private string name = "Мій матч", message, confirmation, pendingPath;
        private int selected = -1;
        private Vector2 scroll, statusScroll;
        public bool IsOpen { get; private set; }
        public void Open(bool save, PrototypeControls current = null)
        {
            controls = current; saving = save; IsOpen = true;
            SaveLoader.ClearMessage();
            confirmation = null; message = null; selected = -1;
            scroll = statusScroll = Vector2.zero; Refresh();
        }
        public void Close() { if (!SaveLoader.Busy) { IsOpen = false; confirmation = null; } }
        private void Refresh()
        {
            entries.Clear();
            try
            {
                foreach (string path in SaveStore.Files())
                {
                    var entry = new Entry { path = path };
                    try { entry.data = SaveStore.Read(path); }
                    catch (Exception error) { entry.error = error.Message; }
                    entries.Add(entry);
                }
            }
            catch (Exception error) { message = "Не вдалося прочитати каталог: " + error.Message; }
        }
        public void Draw(HudLayout layout)
        {
            if (!IsOpen) return;
            float width = Mathf.Min(760, layout.FullScreen.width - 24), height = Mathf.Min(530, layout.FullScreen.height - 24);
            var panel = new Rect((layout.FullScreen.width - width) / 2, (layout.FullScreen.height - height) / 2, width, height);
            MatchUi.Shade(layout.FullScreen);
            MatchUi.Window(panel, saving ? "Зберегти гру" : "Завантажити гру");
            bool previous = GUI.enabled;
            GUI.enabled = previous && !SaveLoader.Busy;
            try
            {
                if (confirmation != null)
                {
                    GUI.Label(new Rect(panel.x + 25, panel.y + 60, width - 50, 70), confirmation, MatchUi.Paragraph);
                    if (MatchUi.Button(new Rect(panel.x + 25, panel.y + 145, width / 2 - 35, 42), "Підтвердити"))
                    {
                        try
                        {
                            if (saving) SaveStore.Write(controls.CaptureSave(), name, pendingPath);
                            else SaveStore.Delete(pendingPath);
                            message = saving ? "Гру збережено." : "Збереження видалено.";
                            confirmation = null; selected = -1; Refresh();
                        }
                        catch (Exception error) { message = "Операцію не виконано: " + error.Message; confirmation = null; }
                    }
                    if (MatchUi.Button(new Rect(panel.center.x + 10, panel.y + 145, width / 2 - 35, 42), "Скасувати")) confirmation = null;
                }
                else
                {
                    float top = panel.y + 49;
                    if (saving)
                    {
                        GUI.Label(new Rect(panel.x + 20, top, 65, 30), "Назва:");
                        name = MatchUi.Field(new Rect(panel.x + 85, top, width - 105, 30), name, 100);
                        top += 43;
                    }
                    var list = new Rect(panel.x + 20, top, width - 40, height - (top - panel.y) - 175);
                    scroll = GUI.BeginScrollView(list, scroll, new Rect(0, 0, list.width - 22, Mathf.Max(list.height, entries.Count * 72)));
                    if (entries.Count == 0) GUI.Label(new Rect(8, 12, list.width - 30, 30), "Збережень поки немає.");
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        string caption = e.data == null ? "Пошкоджене збереження\n" + Path.GetFileName(e.path) :
                            $"{e.data.name}\n{Date(e.data.utc)}   |   {MatchSession.Profile(e.data.difficulty).Title}   |   {PrototypeMatch.FormatTime(e.data.time)}";
                        if (MatchUi.Button(new Rect(0, i * 72, list.width - 24, 67), caption,
                            highlighted: selected == i, multiline: true))
                        {
                            selected = i;
                            message = e.error;
                            statusScroll = Vector2.zero;
                            if (saving && e.data != null) name = e.data.name;
                        }
                    }
                    GUI.EndScrollView();
                    float y = panel.yMax - 159;
                    if (saving)
                    {
                        if (MatchUi.Button(new Rect(panel.x + 20, y, width - 40, 35), "Зберегти", highlighted: true))
                        {
                            try
                            {
                                var existing = selected >= 0 && selected < entries.Count && entries[selected].data != null &&
                                    string.Equals(entries[selected].data.name, name.Trim(), StringComparison.OrdinalIgnoreCase)
                                    ? entries[selected] : entries.Find(e => e.data != null && string.Equals(e.data.name, name.Trim(), StringComparison.OrdinalIgnoreCase));
                                if (existing != null) { pendingPath = existing.path; confirmation = $"Перезаписати «{existing.data.name}» ({Date(existing.data.utc)})? Попередній стан буде замінено."; }
                                else { SaveStore.Write(controls.CaptureSave(), name); message = "Гру збережено."; Refresh(); }
                            }
                            catch (Exception error) { message = "Не вдалося зберегти: " + error.Message; }
                        }
                    }
                    else
                    {
                        GUI.enabled = previous && selected >= 0 && selected < entries.Count && entries[selected].data != null && !SaveLoader.Busy;
                        if (MatchUi.Button(new Rect(panel.x + 20, y, (width - 50) / 2, 35), "Завантажити", highlighted: true))
                        {
                            try { SaveLoader.Load(SaveStore.Read(entries[selected].path)); }
                            catch (Exception error) { message = "Не вдалося завантажити: " + error.Message; }
                        }
                        GUI.enabled = previous && selected >= 0 && selected < entries.Count && !SaveLoader.Busy;
                        if (MatchUi.Button(new Rect(panel.center.x + 5, y, (width - 50) / 2, 35), "Видалити"))
                        { pendingPath = entries[selected].path; confirmation = "Видалити вибране збереження?"; }
                    }
                    GUI.enabled = previous && !SaveLoader.Busy;
                    if (MatchUi.Button(new Rect(panel.x + 20, panel.yMax - 43, width - 40, 30), "Назад")) Close();
                }
                string status = SaveLoader.Busy ? "Відновлення матчу…" : SaveLoader.Message ?? message ?? "";
                if (!string.IsNullOrEmpty(status))
                {
                    var area = new Rect(panel.x + 20, panel.yMax - 116, width - 40, 66);
                    var content = new GUIContent(status);
                    var style = MatchUi.Paragraph;
                    float textWidth = area.width - 24;
                    float textHeight = style.CalcHeight(content,textWidth);
                    statusScroll = GUI.BeginScrollView(area,statusScroll,new Rect(0,0,textWidth,Mathf.Max(textHeight,area.height)));
                    GUI.Label(new Rect(0,0,textWidth,textHeight),content,style);
                    GUI.EndScrollView();
                }
            }
            finally { GUI.enabled = previous; }
        }
        private static string Date(string utc) => DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var value)
            ? value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "Невідома дата";
    }
    public sealed partial class PrototypeControls
    {
        internal void OpenSaveWindow(bool saving)
        {
            interfaceHud.SetPaused(true);
            saveWindow ??= new SaveWindow();
            saveWindow.Open(saving, this);
        }
    }
}
