using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    //The recycler's panel: SHIP RESOURCES and WORKING AREA, listing what's in the area and what it breaks down into,
    //across the top, and POWER, SETUP and OPERATION with the RECYCLE button under them. POWER, SHIP RESOURCES and SETUP
    //are shared with the fabricator (FzMachineGUI). Settings live in the recycler's prop map through Recycler.
    public class FzRecyclerGUI : FzMachineGUI
    {
        public const string PrefabName = "FzRecyclerGUI";

        private const int AreaLines = 12;
        private const int StatusLines = 8;

        //Rows of the container, as fractions of its height
        private const float TopY = 0.44f, TopHeight = 0.56f, BottomHeight = 0.43f;

        public static GameObject CreatePrefab()
        {
            //Two rows: the two readouts across the top, and the controls under them
            GameObject root = Controls.Panel.CreateContainer(PrefabName, 0.62f, 0.66f, 0.19f, 0.17f);
            root.AddComponent<Animator>();
            root.AddComponent<FzRecyclerGUI>();

            CreateResourcesPanel(root, 0.4975f, TopHeight, 0f, TopY);

            GameObject area = Controls.Panel.CreatePanel("AreaPanel", 0.4975f, TopHeight, 0.5025f, TopY, root);
            Controls.Panel.AddTitle(area, "WORKING AREA", 0.085f);
            GameObject lines = CreateScreenArea(area, "AreaLines");
            float lineH = 1f / (AreaLines + 1);
            for (int i = 0; i < AreaLines; i++)
            {
                //Item lines have their count in a column of its own at the right, yield lines leave it empty
                FzMaterialScreen.MakeText(lines, "Line" + i, "", 0.84f, lineH * 0.9f, 0.01f, 1f - lineH * (i + 1), TextAlignmentOptions.Left);
                FzMaterialScreen.MakeText(lines, "Count" + i, "", 0.14f, lineH * 0.9f, 0.85f, 1f - lineH * (i + 1), TextAlignmentOptions.Right);
            }
            FzMaterialScreen.MakeText(lines, "AreaStatus", "", 0.78f, lineH * 0.9f, 0.01f, 0f, TextAlignmentOptions.Left).color = FzMaterialScreen.DimTextColor;
            FzMaterialScreen.MakeText(lines, "AreaPage", "", 0.2f, lineH * 0.9f, 0.79f, 0f, TextAlignmentOptions.Right);
            PageKeys(area, "Area");

            CreatePowerPanel(root, 0.17f, BottomHeight, 0f, 0f);
            CreateSetupPanel(root, 0.38f, BottomHeight, 0.175f, 0f);

            //A status screen, with RECYCLE over AUTO and DUMP EXCESS beside it
            GameObject operation = Controls.Panel.CreatePanel("OperationPanel", 0.44f, BottomHeight, 0.56f, 0f, root);
            Controls.Panel.AddTitle(operation, "OPERATION", 0.11f);
            CreateStatusScreen(operation, 0.56f, StatusLines);
            LampBtn.Create(operation, "RunBtn", "RECYCLE", 0.3f, 0.3f, 0.635f, 0.52f);
            //AUTO and DUMP EXCESS side by side under it, sized to keep the torch drive switch's proportions at this
            //panel's size, for a 16:9 screen
            FzCoveredToggle.Create(operation, "AutoSwitch", 0.1f, 0.41f, 0.655f, 0.08f);
            FzSiloGUI.Caption(operation, "AutoCaption", "AUTO", 0.015f, 0.18f, 0.615f, 0.06f);
            FzCoveredToggle.Create(operation, "DumpSwitch", 0.1f, 0.41f, 0.815f, 0.08f);
            FzSiloGUI.Caption(operation, "DumpCaption", "DUMP EXCESS", 0.015f, 0.2f, 0.765f, 0.06f);

            return root;
        }

        //A status screen at the left of an OPERATION panel, a label and a value on each line
        internal static void CreateStatusScreen(GameObject operation, float width, int lineCount)
        {
            GameObject statusScreen = FzMaterialScreen.CreateRect(operation, "StatusScreen", width, 0.8f, 0.03f, 0.05f);
            Controls.Panel.CreateScreen(statusScreen);
            GameObject status = FzMaterialScreen.CreateRect(statusScreen, "Status", 0.92f, 0.9f, 0.04f, 0.05f);
            float statusH = 1f / lineCount;
            for (int i = 0; i < lineCount; i++)
            {
                float y = 1f - statusH * (i + 1);
                FzMaterialScreen.MakeText(status, "StatusLabel" + i, "", 0.98f, statusH * 0.9f, 0.01f, y, TextAlignmentOptions.Left);
                FzMaterialScreen.MakeText(status, "StatusValue" + i, "", 0.6f, statusH * 0.9f, 0.39f, y, TextAlignmentOptions.Right);
            }
        }

        private TMP_Text[] areaLines, areaCounts;
        private TMP_Text areaStatus, areaPage;
        private LampBtn runBtn;
        private TMP_Text[] statusLabels, statusValues;
        private int queueCount; //Recyclable items in the area, counting stacks whole
        private double queueSeconds; //Game seconds to recycle them all
        private double queueEnergy; //kWh to recycle them all
        private int areaPageIndex;
        private readonly List<(string Text, string Count)> areaText = new List<(string Text, string Count)>();
        private string areaSummary = "";

        protected override void InitPanel()
        {
            areaLines = Enumerable.Range(0, AreaLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "Line" + i)).ToArray();
            areaCounts = Enumerable.Range(0, AreaLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "Count" + i)).ToArray();
            areaStatus = UnityUtil.FindDescendant<TMP_Text>(transform, "AreaStatus");
            areaPage = UnityUtil.FindDescendant<TMP_Text>(transform, "AreaPage");
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "AreaPgUpBtn").Pressed += () => TurnAreaPage(-1);
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "AreaPgDnBtn").Pressed += () => TurnAreaPage(1);

            //Set the state first, the On setter raises OnChanged. AUTO keeps it lit and locked on; see UpdateRunLamp.
            runBtn = UnityUtil.FindDescendant<LampBtn>(transform, "RunBtn");
            runBtn.SetOnWithoutNotify(Recycler.IsRecycleOn(COSelf));
            runBtn.OnChanged += on => Recycler.SetRecycleOn(COSelf, on);
            UpdateRunLamp();

            statusLabels = Enumerable.Range(0, StatusLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "StatusLabel" + i)).ToArray();
            statusValues = Enumerable.Range(0, StatusLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "StatusValue" + i)).ToArray();

            FzCoveredToggle autoSwitch = UnityUtil.FindDescendant<FzCoveredToggle>(transform, "AutoSwitch");
            autoSwitch.SetOnWithoutNotify(Recycler.IsAuto(COSelf));
            autoSwitch.SetClosedWithoutNotify(Recycler.IsAutoCovered(COSelf));
            autoSwitch.OnChanged += on => { Recycler.SetAuto(COSelf, on); UpdateRunLamp(); nextScan = 0f; };
            autoSwitch.OnCoverChanged += closed => Recycler.SetAutoCovered(COSelf, closed);

            FzCoveredToggle dumpSwitch = UnityUtil.FindDescendant<FzCoveredToggle>(transform, "DumpSwitch");
            dumpSwitch.SetOnWithoutNotify(Recycler.IsDumpExcess(COSelf));
            dumpSwitch.SetClosedWithoutNotify(Recycler.IsDumpCovered(COSelf));
            dumpSwitch.OnChanged += on => { Recycler.SetDumpExcess(COSelf, on); nextScan = 0f; };
            dumpSwitch.OnCoverChanged += closed => Recycler.SetDumpCovered(COSelf, closed);
        }

        protected override double PowerDraw() => Recycler.PowerDraw(COSelf);

        //RECYCLE turns itself off when it's been through the area
        protected override void UpdateFrame() => UpdateRunLamp();

        protected override void UpdateScan()
        {
            ScanArea();
            ShowArea();
            ShowStatus();
        }

        //AUTO recycles whatever turns up, so while it's on RECYCLE shows lit and can't be switched off. Its own
        //setting is kept, and shows again once AUTO is off.
        private void UpdateRunLamp()
        {
            if (runBtn == null || COSelf == null) return;
            bool auto = Recycler.IsAuto(COSelf);
            runBtn.Enabled = !auto;
            runBtn.SetOnWithoutNotify(auto || Recycler.IsRecycleOn(COSelf));
        }

        //What it's doing, what it's working on and how long that has left, and how long the whole area will take
        private void ShowStatus()
        {
            //Run times are in standard seconds, which a faster recycler gets through sooner
            double speed = WorkArea.Speed(COSelf);
            CondOwner current = Recycler.CurrentItem(COSelf);
            double left = current != null ? Math.Max(0.0, Recycler.RunTime(current) - Recycler.Progress(COSelf)) / speed : 0.0;
            double eta = Math.Max(0.0, queueSeconds - (current != null ? Recycler.Progress(COSelf) : 0.0)) / speed;
            string blocked = Recycler.IsWorking(COSelf) ? Recycler.Blocked(COSelf) : null;
            string state = !COSelf.HasCond("IsPowered") ? "NO POWER"
                : blocked != null ? blocked
                : current != null ? "RECYCLING"
                : Recycler.IsRecycleOn(COSelf) || Recycler.IsAuto(COSelf) ? "WAITING"
                : "IDLE";

            SetStatus(statusLabels, statusValues, 0, "STATUS", state);
            SetStatus(statusLabels, statusValues, 1, "ITEM", current != null ? "" : "NONE");
            SetStatus(statusLabels, statusValues, 2, current != null ? " " + ItemName(current) : "", "");
            SetStatus(statusLabels, statusValues, 3, "TIME LEFT", current != null ? FzNumeric.FormatDuration((float)Math.Ceiling(left)) : "-");
            SetStatus(statusLabels, statusValues, 4, "QUEUE", $"{queueCount} ITEM{(queueCount == 1 ? "" : "S")}");
            SetStatus(statusLabels, statusValues, 5, "AREA ETA", queueCount > 0 ? FzNumeric.FormatDuration((float)Math.Ceiling(eta)) : "-");
            SetStatus(statusLabels, statusValues, 6, "AREA ENERGY", queueCount > 0 ? FzNumeric.FormatEnergy((float)queueEnergy) : "-");
            SetStatus(statusLabels, statusValues, 7, "BEAM FORMER", FormerText(COSelf));
        }

        //The machine's beam former's life left, as a share of new, or NONE
        internal static string FormerText(CondOwner machine)
        {
            CondOwner former = BeamFormer.Installed(machine);
            double life = BeamFormer.Life(former), max = BeamFormer.LifeMax(former);
            if (former == null || life <= 0.0) return "<color=#FF5A4A>NONE</color>";
            return max > 0.0 ? $"{Math.Ceiling(life / max * 100.0)}%" : FzMaterialScreen.Units(life) + " U";
        }

        //A label starting with a space is a value of its own, shown bright; others are dimmed
        internal static void SetStatus(TMP_Text[] labels, TMP_Text[] values, int line, string label, string value)
        {
            if (line >= labels.Length) return;
            string shown = label.StartsWith(" ") ? label : Dim(label);
            if (labels[line].text != shown) labels[line].text = shown;
            if (values[line].text != value) values[line].text = value;
        }

        //What everything in the area breaks down into, by full name, then the items themselves with their counts, and
        //under any container what's inside it, nested. A container is recycled with its contents, so its yield, time
        //and energy include theirs. Reserves an item holds, such as gas in a canister, go to the ship's canisters and
        //are listed under the materials. A yield the ship has no room for is red, or yellow when the excess will be
        //dumped. Items with no recipe are listed dimmed, they stay where they are.
        private void ScanArea()
        {
            areaText.Clear();
            var materials = new Dictionary<string, double>();
            var reserves = new Dictionary<string, double>();
            var items = new List<(string Text, string Count)>();
            queueCount = 0;
            queueSeconds = 0.0;
            queueEnergy = 0.0;

            List<CondOwner> inArea = Recycler.ItemsInArea(COSelf);
            foreach (CondOwner item in inArea.Where(i => Recycler.IsRecyclable(i) && !i.HasCond(FabricatorProcess.UnfinishedCond)))
            {
                foreach (var part in Recycler.Breakdown(item)) Add(materials, part.Key, part.Value);
                foreach (var held in Recycler.ReservesIn(item)) Add(reserves, held.Key, held.Value);
                queueCount += Math.Max(1, item.StackCount);
                queueSeconds += Recycler.RunTime(item);
                queueEnergy += Recycler.Energy(item);
            }
            int count = ListItems(items, inArea, 0, true);

            HashSet<string> noRoom = Recycler.NoRoom(COSelf.ship, materials, reserves);
            string overColour = Recycler.IsDumpExcess(COSelf) ? DumpedColour : NoRoomColour;
            string Amount(string key, string text) => noRoom.Contains(key) ? $"<color={overColour}>{text}</color>" : text;

            areaText.Add((Dim("YIELD"), ""));
            var shown = materials.Where(m => Math.Floor(m.Value + 1e-6) >= 1).OrderBy(m => FabData.Materials.Keys.ToList().IndexOf(m.Key)).ToList();
            foreach (var material in shown)
            {
                string colour = FabData.Materials.TryGetValue(material.Key, out JsonFabMaterial def) ? def.strColor : "#FFFFFF";
                areaText.Add(($" <color={colour}>{def?.strNameFriendly.ToUpperInvariant() ?? material.Key}</color>", Amount(material.Key, FzMaterialScreen.Units(material.Value))));
            }
            foreach (Reserves.Reserve reserve in Reserves.All.Where(r => reserves.ContainsKey(r.Key)))
            {
                areaText.Add(($" <color={reserve.Color}>{reserve.Name}</color>", Amount(reserve.Key, Reserves.Kg(reserves[reserve.Key]) + "kg")));
            }
            if (shown.Count == 0 && reserves.Count == 0) areaText.Add((Dim(" NOTHING"), ""));

            areaText.Add(("", ""));
            areaText.Add((Dim("ITEMS"), ""));
            areaText.AddRange(items);
            if (items.Count == 0) areaText.Add((Dim(" AREA EMPTY"), ""));

            areaSummary = count == 0 ? "AREA EMPTY" : $"{count} ITEM{(count == 1 ? "" : "S")}  {FzMaterialScreen.Units(shown.Sum(m => m.Value))} U";
        }

        private static void Add(Dictionary<string, double> totals, string key, double amount) => totals[key] = (totals.TryGetValue(key, out double had) ? had : 0) + amount;

        //Yield amounts the ship can't hold: red when they'd stop the recycler, yellow when they'll be dumped
        private const string NoRoomColour = "#FF5A4A";
        private const string DumpedColour = "#FFD24A";
        private const int IndentSpaces = 2;

        //Adds lines for some items at a depth of nesting, alike items together, and under each container what's in
        //it. Something that can't be recycled is dimmed, and so is everything inside it, which stays with it.
        //Returns how many things it listed, counting stacks whole and contents too.
        private int ListItems(List<(string Text, string Count)> lines, IEnumerable<CondOwner> things, int depth, bool recycled)
        {
            int listed = 0;
            string indent = new string(' ', 1 + depth * IndentSpaces);
            //Containers with something in them are listed one by one, so their contents stay under them
            var groups = things.GroupBy(t => Recycler.Contents(t).Count > 0 ? t.strID : t.strCODef).OrderBy(g => ItemName(g.First()));
            foreach (var group in groups)
            {
                CondOwner first = group.First();
                int units = group.Sum(t => Math.Max(1, t.StackCount));
                listed += units;
                bool recyclable = recycled && Recycler.IsRecyclable(first);
                string name = indent.Substring(1) + (depth > 0 ? "> " : "") + ItemName(first);
                lines.Add(recyclable ? (name, $"x{units}") : (Dim(name + (Recycler.IsRecyclable(first) ? "" : " (NO RECIPE)")), Dim($"x{units}")));
                List<CondOwner> inside = Recycler.Contents(first);
                if (inside.Count > 0) listed += ListItems(lines, inside, depth + 1, recyclable);
            }
            return listed;
        }

        private int AreaPageCount => Math.Max(1, (areaText.Count + AreaLines - 1) / AreaLines);

        private void TurnAreaPage(int pages)
        {
            int count = AreaPageCount;
            areaPageIndex = ((areaPageIndex + pages) % count + count) % count;
            ShowArea();
        }

        private void ShowArea()
        {
            int pages = AreaPageCount;
            if (areaPageIndex >= pages) areaPageIndex = pages - 1;
            int first = areaPageIndex * AreaLines;
            for (int i = 0; i < AreaLines; i++)
            {
                (string Text, string Count) line = first + i < areaText.Count ? areaText[first + i] : ("", "");
                if (areaLines[i].text != line.Text) areaLines[i].text = line.Text;
                if (areaCounts[i].text != line.Count) areaCounts[i].text = line.Count;
            }
            if (areaStatus.text != areaSummary) areaStatus.text = areaSummary;
            bool paged = pages > 1;
            areaPage.gameObject.SetActive(paged);
            if (paged) areaPage.text = $"PG {areaPageIndex + 1}/{pages}";
        }
    }
}
