using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    //The green material readout drawn on a screen: a capacity bar, then a line for each material with its name, a bar
    //in its colour and its units, then a status line. Materials that don't fit on one page are paged, with a page
    //marker in the corner. It can show a selected material in bold and brackets, and selection wraps around.
    //
    //It's built into an area of an open panel, as the materials it lists depend on what the panel is for.
    public class FzMaterialScreen
    {
        public static readonly Color TextColor = new Color(0.631f, 0.894f, 0.392f);
        public static readonly Color DimTextColor = new Color(0.38f, 0.54f, 0.24f);
        public static readonly Color TrackColor = new Color(0.07f, 0.11f, 0.07f);
        public const float TextMinFontSize = 4f;
        public const float TextMaxFontSize = 9f;

        //Columns, as fractions of the area's width
        private const float NameX = 0.02f, NameW = 0.33f;
        private const float BarX = 0.36f, BarW = 0.40f;
        private const float NumX = 0.77f, NumW = 0.21f;

        //A line's content: a material, a ship reserve with its own fill, or the heading above the reserves
        private class Entry
        {
            public string Material;
            public Reserves.Reserve Reserve;
            public bool Header;
            public string Name;
            public Color Color;
        }

        //A line of the screen, showing whichever material is in its place on the current page
        private class Row
        {
            public string Name;
            public TMP_Text NameText;
            public GameObject Track;
            public RectTransform Fill;
            public Image FillImage;
            public TMP_Text NumText;
            public string LastName, LastNum;
            public float LastFill = -1f;
            public Entry Shown;
            public bool Visible = true;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<Row> rows = new List<Row>();
        private readonly Row capacityRow;
        private readonly TMP_Text statusText, pageText;
        private readonly int pageSize;
        private readonly bool selectable;
        private string lastStatus, lastPage;
        private int page;

        public int Count => entries.Count;
        public int Selected { get; private set; }
        public string SelectedMaterial => entries.Count > 0 ? entries[Selected].Material : null;
        public string SelectedName => entries.Count > 0 ? entries[Selected].Name : null;
        public int PageCount => Math.Max(1, (entries.Count + pageSize - 1) / pageSize);
        public int Page => selectable ? Selected / pageSize : page;
        public bool Paged => PageCount > 1;

        //Reserves, if given, are listed after the materials under their own heading. Each reserve's bar shows how full
        //it is against its own capacity, not the materials' capacity bar. Only read-only screens should show them.
        public FzMaterialScreen(GameObject area, IEnumerable<string> materials, int pageSize, bool selectable, string capacityLabel = "CAPACITY", IEnumerable<Reserves.Reserve> reserves = null)
        {
            this.pageSize = Math.Max(1, pageSize);
            this.selectable = selectable;
            foreach (Transform child in area.transform) UnityEngine.Object.Destroy(child.gameObject);

            foreach (string material in materials)
            {
                if (!FabData.Materials.TryGetValue(material, out JsonFabMaterial def)) continue;
                entries.Add(new Entry { Material = material, Name = def.strNameFriendly.ToUpperInvariant(), Color = SpriteUtil.ColorFromHex(def.strColor) });
            }
            var reserveList = reserves?.ToList() ?? new List<Reserves.Reserve>();
            if (reserveList.Count > 0)
            {
                //The reserves start a page of their own, unless they all fit on the materials' last page
                int room = (this.pageSize - entries.Count % this.pageSize) % this.pageSize;
                if (entries.Count > 0 && room < reserveList.Count + 1)
                {
                    while (entries.Count % this.pageSize != 0) entries.Add(new Entry { Header = true, Name = "" });
                }
                entries.Add(new Entry { Header = true, Name = "RESERVES" });
                foreach (Reserves.Reserve reserve in reserveList)
                {
                    entries.Add(new Entry { Reserve = reserve, Name = reserve.Name, Color = SpriteUtil.ColorFromHex(reserve.Color) });
                }
            }

            //The capacity bar, a gap, a line for each material on a page, and the status line, top to bottom
            int lines = Math.Min(entries.Count, this.pageSize);
            float rowH = 1f / (lines + 3.2f);
            float y = 1f - rowH;

            capacityRow = CreateRow(area, "Capacity", capacityLabel, TextColor, y, rowH);
            y -= rowH * 1.4f;
            for (int i = 0; i < lines; i++)
            {
                rows.Add(CreateRow(area, "Line" + i, "", Color.white, y, rowH));
                y -= rowH;
            }

            statusText = MakeText(area, "Status", "", Paged ? 0.78f : 0.96f, rowH * 0.9f, NameX, 0f, TextAlignmentOptions.Left);
            statusText.color = DimTextColor;
            pageText = MakeText(area, "Page", "", 0.18f, rowH * 0.9f, 0.80f, 0f, TextAlignmentOptions.Right);
            pageText.gameObject.SetActive(Paged);
        }

        public int IndexOf(string material) => entries.FindIndex(e => e.Material == material);

        //Wraps around, so up from the first material goes to the last and down from the last to the first
        public void Select(int index)
        {
            if (entries.Count == 0) return;
            Selected = ((index % entries.Count) + entries.Count) % entries.Count;
        }

        //Keeps the selection's place on the page, wrapping between the first and last pages. The last page may be
        //short, so a place past its end selects its last material. Without selection it only turns the page.
        public void TurnPage(int pages)
        {
            if (!Paged) return;
            int next = ((Page + pages) % PageCount + PageCount) % PageCount;
            if (selectable) Select(Math.Min(next * pageSize + Selected % pageSize, entries.Count - 1));
            else page = next;
        }

        //reserve gives a reserve's kg and capacity in kg, needed only when the screen lists reserves
        public void Refresh(double capacity, double total, Func<string, double> amount, string status, Func<Reserves.Reserve, (double Kg, double Capacity)> reserve = null)
        {
            SetRow(capacityRow, capacityRow.Name, capacity > 0 ? (float)(total / capacity) : 0f, $"{Units(total)}/{Units(capacity)}");

            int first = Page * pageSize;
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                int index = first + i;
                Entry entry = index < entries.Count ? entries[index] : null;
                ShowEntry(row, entry);
                if (entry == null) continue;
                if (entry.Header)
                {
                    SetRow(row, $"<color=#{ColorUtility.ToHtmlStringRGB(DimTextColor)}>{entry.Name}</color>", 0f, "");
                    continue;
                }
                if (entry.Reserve != null)
                {
                    var (kg, kgCapacity) = reserve != null ? reserve(entry.Reserve) : (0.0, 0.0);
                    SetRow(row, $" {entry.Name}", kgCapacity > 0 ? (float)(kg / kgCapacity) : 0f, Reserves.Kg(kg) + "kg");
                    continue;
                }
                double units = amount(entry.Material);
                bool isSelected = selectable && index == Selected;
                string name = isSelected ? $"<b>[{entry.Name}]</b>" : $" {entry.Name}";
                string num = isSelected ? $"<b>{Units(units)}</b>" : Units(units);
                SetRow(row, name, capacity > 0 ? (float)(units / capacity) : 0f, num);
            }

            if (status != lastStatus) statusText.text = lastStatus = status;
            string marker = $"PG {Page + 1}/{PageCount}";
            if (pageText.gameObject.activeSelf && marker != lastPage) pageText.text = lastPage = marker;
        }

        private static Row CreateRow(GameObject parent, string name, string label, Color barColor, float y, float height)
        {
            var row = new Row { Name = label };
            row.NameText = MakeText(parent, name + "Name", label, NameW, height * 0.9f, NameX, y, TextAlignmentOptions.Left);
            row.NumText = MakeText(parent, name + "Num", "0", NumW, height * 0.9f, NumX, y, TextAlignmentOptions.Right);

            row.Track = CreateRect(parent, name + "Track", BarW, height * 0.5f, BarX, y + height * 0.22f);
            Image trackImage = row.Track.AddComponent<Image>();
            trackImage.color = TrackColor;
            trackImage.raycastTarget = false;

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fill.layer = parent.layer;
            row.Fill = fill.GetComponent<RectTransform>();
            row.Fill.SetParent(row.Track.GetComponent<RectTransform>(), false);
            row.Fill.anchorMin = Vector2.zero;
            row.Fill.anchorMax = new Vector2(0f, 1f);
            row.Fill.offsetMin = row.Fill.offsetMax = Vector2.zero;
            row.FillImage = fill.GetComponent<Image>();
            row.FillImage.color = barColor;
            row.FillImage.raycastTarget = false;
            return row;
        }

        //Puts a material in a line, or blanks the line past the end of a short last page
        private static void ShowEntry(Row row, Entry entry)
        {
            bool visible = entry != null;
            if (visible != row.Visible)
            {
                row.Visible = visible;
                row.NameText.gameObject.SetActive(visible);
                row.NumText.gameObject.SetActive(visible);
            }
            bool bar = visible && !entry.Header;
            if (row.Track.activeSelf != bar) row.Track.SetActive(bar);
            if (entry != null && entry != row.Shown)
            {
                row.Shown = entry;
                row.FillImage.color = entry.Color;
                row.LastFill = -1f;
            }
        }

        private static void SetRow(Row row, string name, float fill, string num)
        {
            if (name != row.LastName) row.NameText.text = row.LastName = name;
            if (num != row.LastNum) row.NumText.text = row.LastNum = num;
            fill = Mathf.Clamp01(fill);
            if (!Mathf.Approximately(fill, row.LastFill))
            {
                row.LastFill = fill;
                row.Fill.anchorMax = new Vector2(fill, 1f);
            }
        }

        public static TMP_Text MakeText(GameObject parent, string name, string text, float width, float height, float x, float y, TextAlignmentOptions alignment)
        {
            TMP_Text label = Label.Create(parent, name, text, width, height, x, y, TextMinFontSize, TextMaxFontSize).GetComponent<TMP_Text>();
            label.color = TextColor;
            label.alignment = alignment;
            label.richText = true;
            return label;
        }

        public static GameObject CreateRect(GameObject parent, string name, float width, float height, float x, float y)
        {
            GameObject rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(FzUIElement));
            rect.layer = LayerMask.NameToLayer("UI");
            rect.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            rect.GetComponent<FzUIElement>().SetTransform(width, height, x, y);
            return rect;
        }

        //Whole units, rounded down, so a readout never shows more than there is
        public static string Units(double amount) => Math.Floor(Math.Max(0.0, amount) + 1e-6).ToString("N0", CultureInfo.InvariantCulture);
    }
}
