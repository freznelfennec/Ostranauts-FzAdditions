using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    //The fabricator's panel: SHIP RESOURCES, BLUEPRINTS to choose what to build, and COST, what one takes against what
    //the ship has and how many that makes, across the top. POWER, SETUP and OPERATION under them, OPERATION with a
    //status screen, FABRICATE and FILL, COUNT and its keys, and AUTO under its cover. POWER, SHIP RESOURCES and SETUP
    //are shared with the recycler (FzMachineGUI). Settings live in the fabricator's prop map through Fabricator.
    public class FzFabricatorGUI : FzMachineGUI
    {
        public const string PrefabName = "FzFabricatorGUI";

        private const int BlueprintLines = 11; //Entries on a page, under the breadcrumbs
        private const string CategoryColour = "#7FC8FF"; //Subcategories, so they stand apart from blueprints
        private const float KeyRepeatDelay = 0.4f, KeyRepeatInterval = 0.08f; //Real seconds, for a held arrow key
        private const int CostLines = 14;
        private const int StatusLines = 7;
        private const float CountRepeatDelay = 0.4f; //Real seconds a COUNT key is held before it repeats
        private const float CountRepeatRate = 6f; //Steps a second as it starts repeating, doubling each second after
        private const string ShortColour = "#FF5A4A"; //Materials the ship hasn't enough of

        //Rows of the container, as fractions of its height, as the recycler's
        private const float TopY = 0.44f, TopHeight = 0.56f, BottomHeight = 0.43f;

        public static GameObject CreatePrefab()
        {
            //Wider than the recycler's, for its third readout. POWER and SETUP keep the recycler's shapes: their widths
            //are the recycler's scaled by its container's width over this one's.
            GameObject root = Controls.Panel.CreateContainer(PrefabName, 0.74f, 0.66f, 0.13f, 0.17f);
            root.AddComponent<Animator>();
            root.AddComponent<FzFabricatorGUI>();

            CreateResourcesPanel(root, 0.31f, TopHeight, 0f, TopY);

            GameObject blueprints = Controls.Panel.CreatePanel("BlueprintPanel", 0.37f, TopHeight, 0.315f, TopY, root);
            Controls.Panel.AddTitle(blueprints, "BLUEPRINTS", 0.085f);
            //The breadcrumbs on the top line, a page of the category's entries, and a status line under them
            GameObject list = CreateScreenArea(blueprints, "BlueprintLines");
            float lineH = 1f / (BlueprintLines + 2);
            FzMaterialScreen.MakeText(list, "BlueprintCrumbs", "", 0.98f, lineH * 0.9f, 0.01f, 1f - lineH, TextAlignmentOptions.Left);
            for (int i = 0; i < BlueprintLines; i++)
            {
                FzMaterialScreen.MakeText(list, "Blueprint" + i, "", 0.98f, lineH * 0.9f, 0.01f, 1f - lineH * (i + 2), TextAlignmentOptions.Left);
            }
            FzMaterialScreen.MakeText(list, "BlueprintStatus", "", 0.7f, lineH * 0.9f, 0.01f, 0f, TextAlignmentOptions.Left).color = FzMaterialScreen.DimTextColor;
            FzMaterialScreen.MakeText(list, "BlueprintPage", "", 0.28f, lineH * 0.9f, 0.71f, 0f, TextAlignmentOptions.Right);
            string[] keys = { "UP", "DOWN", "PGUP", "PGDN", "BACK", "OPEN" };
            for (int i = 0; i < keys.Length; i++)
            {
                FzHoldBtn.Create(blueprints, "Bp" + keys[i] + "Btn", keys[i], 0.145f, 0.105f, 0.03f + i * 0.157f, 0.025f);
            }

            //COST has no keys, so its screen runs the panel's height
            GameObject cost = Controls.Panel.CreatePanel("CostPanel", 0.31f, TopHeight, 0.69f, TopY, root);
            Controls.Panel.AddTitle(cost, "COST", 0.085f);
            GameObject costScreen = FzMaterialScreen.CreateRect(cost, "CostScreen", 0.92f, 0.86f, 0.04f, 0.03f);
            Controls.Panel.CreateScreen(costScreen);
            GameObject costLines = FzMaterialScreen.CreateRect(costScreen, "CostLines", 0.94f, 0.94f, 0.03f, 0.03f);
            float costH = 1f / CostLines;
            for (int i = 0; i < CostLines; i++)
            {
                float y = 1f - costH * (i + 1);
                FzMaterialScreen.MakeText(costLines, "CostName" + i, "", 0.98f, costH * 0.9f, 0.01f, y, TextAlignmentOptions.Left);
                FzMaterialScreen.MakeText(costLines, "CostNeed" + i, "", 0.2f, costH * 0.9f, 0.52f, y, TextAlignmentOptions.Right);
                FzMaterialScreen.MakeText(costLines, "CostHave" + i, "", 0.26f, costH * 0.9f, 0.73f, y, TextAlignmentOptions.Right);
            }

            CreatePowerPanel(root, 0.1424f, BottomHeight, 0f, 0f);
            CreateSetupPanel(root, 0.3184f, BottomHeight, 0.1466f, 0f);

            //A status screen, FABRICATE and FILL beside it, COUNT and AUTO under them. Sized for the recycler's
            //proportions at this panel's size, for a 16:9 screen.
            GameObject operation = Controls.Panel.CreatePanel("OperationPanel", 0.5308f, BottomHeight, 0.4692f, 0f, root);
            Controls.Panel.AddTitle(operation, "OPERATION", 0.11f);
            FzRecyclerGUI.CreateStatusScreen(operation, 0.46f, StatusLines);
            LampBtn.Create(operation, "FabricateBtn", "FABRICATE", 0.215f, 0.26f, 0.51f, 0.6f);
            LampBtn.Create(operation, "FillBtn", "FILL ITEMS", 0.215f, 0.26f, 0.745f, 0.6f);
            FzHoldBtn.Create(operation, "CountLessBtn", "-", 0.075f, 0.13f, 0.51f, 0.33f);
            FzNumeric.Create(operation, "CountDisplay", 0.14f, 0.16f, 0.595f, 0.315f, LampColor.Yellow, null, "0");
            FzHoldBtn.Create(operation, "CountMoreBtn", "+", 0.075f, 0.13f, 0.745f, 0.33f);
            FzSiloGUI.Caption(operation, "CountCaption", "COUNT", 0.235f, 0.31f, 0.51f, 0.06f);
            FzCoveredToggle.Create(operation, "AutoSwitch", 0.07f, 0.41f, 0.875f, 0.08f);
            FzSiloGUI.Caption(operation, "AutoCaption", "AUTO", 0.015f, 0.15f, 0.835f, 0.06f);

            return root;
        }

        private TMP_Text[] blueprintLines;
        private TMP_Text blueprintCrumbs, blueprintStatus, blueprintPage;
        private TMP_Text[] costNames, costNeeds, costHaves;
        private TMP_Text[] statusLabels, statusValues;
        private LampBtn fabricateBtn, fillBtn;
        private FzNumeric countDisplay;
        private FzHoldBtn countLess, countMore;
        private float countHeld; //Steps owed to a held COUNT key, as they come in fractions
        //A line of the blueprint screen: a subcategory to open, or a recipe to choose
        private struct Entry
        {
            public FabCategory Category;
            public JsonFabRecipe Recipe;
        }

        private FabCategory category; //The one being shown
        private readonly List<Entry> entries = new List<Entry>();
        private int selected = -1;
        private FzHoldBtn bpUp, bpDown, bpBack, bpOpen;
        private float nextRepeat;

        protected override void InitPanel()
        {
            blueprintLines = Enumerable.Range(0, BlueprintLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "Blueprint" + i)).ToArray();
            blueprintCrumbs = UnityUtil.FindDescendant<TMP_Text>(transform, "BlueprintCrumbs");
            blueprintStatus = UnityUtil.FindDescendant<TMP_Text>(transform, "BlueprintStatus");
            blueprintPage = UnityUtil.FindDescendant<TMP_Text>(transform, "BlueprintPage");
            bpUp = UnityUtil.FindDescendant<FzHoldBtn>(transform, "BpUPBtn");
            bpDown = UnityUtil.FindDescendant<FzHoldBtn>(transform, "BpDOWNBtn");
            bpBack = UnityUtil.FindDescendant<FzHoldBtn>(transform, "BpBACKBtn");
            bpOpen = UnityUtil.FindDescendant<FzHoldBtn>(transform, "BpOPENBtn");
            bpUp.Pressed += () => { nextRepeat = Time.unscaledTime + KeyRepeatDelay; MoveSelection(-1); };
            bpDown.Pressed += () => { nextRepeat = Time.unscaledTime + KeyRepeatDelay; MoveSelection(1); };
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "BpPGUPBtn").Pressed += () => MoveSelection(-BlueprintLines);
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "BpPGDNBtn").Pressed += () => MoveSelection(BlueprintLines);
            bpBack.Pressed += Back;
            bpOpen.Pressed += Open;

            costNames = Enumerable.Range(0, CostLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "CostName" + i)).ToArray();
            costNeeds = Enumerable.Range(0, CostLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "CostNeed" + i)).ToArray();
            costHaves = Enumerable.Range(0, CostLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "CostHave" + i)).ToArray();
            statusLabels = Enumerable.Range(0, StatusLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "StatusLabel" + i)).ToArray();
            statusValues = Enumerable.Range(0, StatusLines).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "StatusValue" + i)).ToArray();

            //Set the states first, the On setters raise OnChanged. AUTO keeps FABRICATE lit and locked on.
            fabricateBtn = UnityUtil.FindDescendant<LampBtn>(transform, "FabricateBtn");
            fabricateBtn.SetOnWithoutNotify(Fabricator.IsFabricateOn(COSelf));
            fabricateBtn.OnChanged += SetFabricate;
            fillBtn = UnityUtil.FindDescendant<LampBtn>(transform, "FillBtn");
            fillBtn.SetOnWithoutNotify(Fabricator.IsFillOn(COSelf));
            fillBtn.OnChanged += on => Fabricator.SetFillOn(COSelf, on);

            countDisplay = UnityUtil.FindDescendant<FzNumeric>(transform, "CountDisplay");
            countLess = UnityUtil.FindDescendant<FzHoldBtn>(transform, "CountLessBtn");
            countMore = UnityUtil.FindDescendant<FzHoldBtn>(transform, "CountMoreBtn");
            countLess.Pressed += () => { countHeld = 0f; StepCount(-1); };
            countMore.Pressed += () => { countHeld = 0f; StepCount(1); };

            FzCoveredToggle autoSwitch = UnityUtil.FindDescendant<FzCoveredToggle>(transform, "AutoSwitch");
            autoSwitch.SetOnWithoutNotify(Fabricator.IsAuto(COSelf));
            autoSwitch.SetClosedWithoutNotify(Fabricator.IsAutoCovered(COSelf));
            autoSwitch.OnChanged += on => { Fabricator.SetAuto(COSelf, on); UpdateLamps(); nextScan = 0f; };
            autoSwitch.OnCoverChanged += closed => Fabricator.SetAutoCovered(COSelf, closed);

            OpenAtChosen();
            UpdateLamps();
        }

        protected override double PowerDraw() => Fabricator.PowerDraw(COSelf);

        protected override void UpdateFrame()
        {
            UpdateLamps(); //FABRICATE turns itself off when its count runs out
            RepeatCount();
            countDisplay.Value = Fabricator.GetCount(COSelf);
            ReadKeys();
        }

        protected override void UpdateScan()
        {
            ShowBlueprints();
            ShowCost();
            ShowStatus();
        }

        //Opens on the chosen recipe's category with it selected, or the top with its first entry
        private void OpenAtChosen()
        {
            JsonFabRecipe chosen = Fabricator.Selected(COSelf);
            FabCategory at = chosen != null ? FabData.Categories.Find(chosen.strCategory) : FabData.Categories;
            while (!at.IsRoot && !Visible(at)) at = at.Parent;
            ShowCategory(at, chosen != null ? (Func<Entry, bool>)(e => e.Recipe == chosen) : null);
        }

        private static bool Visible(FabCategory node) => node.AnyKnown(Blueprints.IsKnown);

        //Makes a category the one shown and selects an entry in it: the one matching, or the first
        private void ShowCategory(FabCategory node, Func<Entry, bool> select)
        {
            category = node;
            LoadEntries();
            int found = select != null ? entries.FindIndex(e => select(e)) : -1;
            selected = found >= 0 ? found : entries.Count > 0 ? 0 : -1;
            ChooseSelected();
            ShowBlueprints();
            ShowCost();
        }

        //The category's subcategories with known blueprints in them, then its known recipes. Blueprints can be learned
        //while the panel is open, so each scan builds this again, keeping the selection.
        private void LoadEntries()
        {
            Entry? was = selected >= 0 && selected < entries.Count ? entries[selected] : (Entry?)null;
            entries.Clear();
            foreach (FabCategory child in category.Children.Where(Visible)) entries.Add(new Entry { Category = child });
            foreach (JsonFabRecipe recipe in category.Recipes.Where(r => Blueprints.IsKnown(r.strBlueprint))) entries.Add(new Entry { Recipe = recipe });
            if (was == null) return;
            int found = entries.FindIndex(e => e.Category == was.Value.Category && e.Recipe == was.Value.Recipe);
            selected = found >= 0 ? found : Math.Min(selected, entries.Count - 1);
        }

        //A highlighted recipe is the one chosen to build; a highlighted subcategory leaves the choice as it was
        private void ChooseSelected()
        {
            if (selected >= 0 && selected < entries.Count && entries[selected].Recipe != null) Fabricator.Select(COSelf, entries[selected].Recipe);
        }

        //Up and down a line or a page, wrapping round at either end
        private void MoveSelection(int by)
        {
            int count = entries.Count;
            if (count == 0) return;
            if (Math.Abs(by) > 1)
            {
                //A page at a time, landing on its first line, and round to the other end past either
                int pages = (count + BlueprintLines - 1) / BlueprintLines;
                int page = Math.Max(0, selected) / BlueprintLines + Math.Sign(by);
                page = (page % pages + pages) % pages;
                selected = page * BlueprintLines;
            }
            else
            {
                selected = ((selected + by) % count + count) % count;
            }
            ChooseSelected();
            ShowBlueprints();
            ShowCost();
        }

        //Into the highlighted subcategory
        private void Open()
        {
            if (selected < 0 || selected >= entries.Count || entries[selected].Category == null) return;
            ShowCategory(entries[selected].Category, null);
        }

        //Up a level, with the category just left highlighted
        private void Back()
        {
            if (category == null || category.IsRoot) return;
            FabCategory left = category;
            ShowCategory(category.Parent, e => e.Category == left);
        }

        //The arrow keys work the screen while the panel is open, each holding down its key on the panel, with its
        //pressed look and sounds, for as long as it's held: up and down move, right opens, left goes back. Shift with
        //an arrow is the game's own panel switching, so it's left alone. UP and DOWN repeat while held, by key or by
        //mouse.
        private void ReadKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                bool shift = keyboard.shiftKey.isPressed;
                Bind(keyboard.upArrowKey, bpUp, shift);
                Bind(keyboard.downArrowKey, bpDown, shift);
                Bind(keyboard.leftArrowKey, bpBack, shift);
                Bind(keyboard.rightArrowKey, bpOpen, shift);
            }
            FzHoldBtn held = bpUp.Held ? bpUp : bpDown.Held ? bpDown : null;
            if (held != null && held.HeldTime >= KeyRepeatDelay && Time.unscaledTime >= nextRepeat)
            {
                nextRepeat = Time.unscaledTime + KeyRepeatInterval;
                MoveSelection(held == bpUp ? -1 : 1);
            }
        }

        private static void Bind(KeyControl key, FzHoldBtn button, bool shift)
        {
            if (key.wasPressedThisFrame && !shift) button.KeyDown();
            else if (!key.isPressed) button.KeyUp();
        }
        private void ShowBlueprints()
        {
            if (category == null) return;
            LoadEntries();
            int count = entries.Count;
            int pages = Math.Max(1, (count + BlueprintLines - 1) / BlueprintLines);
            int page = selected >= 0 ? selected / BlueprintLines : 0;
            for (int i = 0; i < BlueprintLines; i++)
            {
                int index = page * BlueprintLines + i;
                string text = "";
                if (count == 0 && i == 0) text = Dim(" NO BLUEPRINTS KNOWN");
                else if (index < count)
                {
                    Entry entry = entries[index];
                    //Subcategories in their own colour with a marker and how many blueprints they hold, blueprints plain
                    string name = entry.Category != null
                        ? $"<color={CategoryColour}>> {CategoryLabel(entry.Category)}</color>"
                        : RecipeName(entry.Recipe);
                    text = index == selected ? $"<b>[{name}]</b>" : $" {name}";
                    if (entry.Category != null) text += Dim($" ({entry.Category.Blueprints.Count(Blueprints.IsKnown)})");
                }
                if (blueprintLines[i].text != text) blueprintLines[i].text = text;
            }
            string crumbs = Crumbs();
            if (blueprintCrumbs.text != crumbs) blueprintCrumbs.text = crumbs;
            int subs = entries.Count(e => e.Category != null), known = count - subs;
            string status = count == 0 ? "" : $"{subs} CAT{(subs == 1 ? "" : "S")}  {known} BP{(known == 1 ? "" : "S")}";
            if (blueprintStatus.text != status) blueprintStatus.text = status;
            string pageText = pages > 1 ? $"PG {page + 1}/{pages}" : "";
            if (blueprintPage.text != pageText) blueprintPage.text = pageText;
        }

        //The way down to the category shown, the levels above dimmed. Deep ones keep the top and the last two.
        private string Crumbs()
        {
            List<string> parts = category.Trail().Select(n => n.IsRoot ? "ALL" : CategoryLabel(n)).ToList();
            if (parts.Count > 4) parts = new List<string> { parts[0], "..." }.Concat(parts.Skip(parts.Count - 2)).ToList();
            string above = string.Join(" / ", parts.Take(parts.Count - 1));
            return parts.Count > 1 ? Dim(above + " / ") + parts[parts.Count - 1] : parts[0];
        }

        //A category's own name, split where its words run together, such as FusionParts
        private static readonly System.Text.RegularExpressions.Regex WordBreak = new System.Text.RegularExpressions.Regex(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])");

        private static string CategoryLabel(FabCategory node) => WordBreak.Replace(node.Name, " ").Replace("_", " ").ToUpperInvariant();
        private static string RecipeName(JsonFabRecipe recipe) => CleanName(recipe.strNameFriendly ?? recipe.strName);


        //What one of the chosen recipe takes, each material against what the ship's silos hold, then how many of it
        //that makes, and the time, energy and floor space each takes
        private void ShowCost()
        {
            var lines = new List<(string Name, string Need, string Have)>();
            JsonFabRecipe recipe = Fabricator.Selected(COSelf);
            if (recipe == null)
            {
                lines.Add((Dim("NO BLUEPRINT SELECTED"), "", ""));
            }
            else
            {
                lines.Add((RecipeName(recipe), "", ""));
                lines.Add((Dim("MATERIAL"), Dim("NEED"), Dim("HAVE")));
                foreach (var material in recipe.mapMaterials.Where(m => m.Value > 0).OrderBy(m => FabData.Materials.Keys.ToList().IndexOf(m.Key)))
                {
                    FabData.Materials.TryGetValue(material.Key, out JsonFabMaterial def);
                    double have = Silo.ShipAmount(COSelf.ship, material.Key);
                    string haveText = FzMaterialScreen.Units(have);
                    if (have + 1e-6 < material.Value) haveText = $"<color={ShortColour}>{haveText}</color>";
                    lines.Add(($" <color={def?.strColor ?? "#FFFFFF"}>{(def?.strNameFriendly ?? material.Key).ToUpperInvariant()}</color>", material.Value.ToString(), haveText));
                }
                //With FILL ITEMS on, a canister also takes its fill from the ship's reserves, in kg. It's filled with
                //what there is, so too little is red but doesn't stop it.
                if (Fabricator.IsFillOn(COSelf))
                {
                    foreach (var fill in Reserves.DefCapacity(recipe.strCOFabricate))
                    {
                        Reserves.Reserve reserve = Reserves.Get(fill.Key);
                        double have = Reserves.ShipAmount(COSelf.ship, reserve);
                        string haveText = Reserves.Kg(have) + "kg";
                        if (have + 1e-6 < fill.Value) haveText = $"<color={ShortColour}>{haveText}</color>";
                        lines.Add(($" <color={reserve.Color}>{reserve.Name}</color>", Reserves.Kg(fill.Value) + "kg", haveText));
                    }
                }
                lines.Add(("", "", ""));
                int canBuild = Fabricator.CanBuild(COSelf.ship, recipe);
                lines.Add((Dim("CAN BUILD"), "", canBuild > 0 ? canBuild.ToString() : $"<color={ShortColour}>0</color>"));
                lines.Add((Dim("TIME EACH"), "", FzNumeric.FormatDuration((float)Math.Ceiling(Fabricator.RunTime(recipe) / WorkArea.Speed(COSelf)))));
                lines.Add((Dim("ENERGY EACH"), "", FzNumeric.FormatEnergy((float)Fabricator.Energy(recipe))));
                Vector2Int size = Fabricator.Size(recipe.strCOFabricate);
                lines.Add((Dim("SIZE"), "", Fabricator.Stacks(recipe.strCOFabricate) ? $"{size.x}x{size.y} STACKS" : $"{size.x}x{size.y}"));
            }
            for (int i = 0; i < CostLines; i++)
            {
                var line = i < lines.Count ? lines[i] : ("", "", "");
                if (costNames[i].text != line.Item1) costNames[i].text = line.Item1;
                if (costNeeds[i].text != line.Item2) costNeeds[i].text = line.Item2;
                if (costHaves[i].text != line.Item3) costHaves[i].text = line.Item3;
            }
        }

        //What it's doing, what it's building and how far through, and how long the rest of the count will take
        private void ShowStatus()
        {
            bool hasRun = Fabricator.HasRun(COSelf);
            JsonFabRecipe running = hasRun ? Fabricator.RunRecipe(COSelf) : null;
            bool working = COSelf.HasCond(Fabricator.FabricatingCond);
            bool trying = Fabricator.IsFabricateOn(COSelf) || Fabricator.IsAuto(COSelf);
            string blocked = Fabricator.IsWorking(COSelf) && trying && !working ? Fabricator.Blocked(COSelf) : null;
            string state = !COSelf.HasCond("IsPowered") ? "NO POWER"
                : working ? "FABRICATING"
                : blocked != null ? blocked
                : trying ? "WAITING"
                : "IDLE";

            //Run times are in standard seconds, which a faster fabricator gets through sooner
            double speed = WorkArea.Speed(COSelf);
            double runTime = Fabricator.RunTime(running);
            double done = hasRun ? Fabricator.Progress(COSelf) : 0.0;
            double left = Math.Max(0.0, runTime - done) / speed;
            JsonFabRecipe batch = running ?? Fabricator.Selected(COSelf);
            int count = Fabricator.GetCount(COSelf);
            bool counting = Fabricator.IsFabricateOn(COSelf) && !Fabricator.IsAuto(COSelf) && batch != null && count > 0;
            double batchLeft = counting ? left + Fabricator.RunTime(batch) / speed * Math.Max(0, count - (hasRun ? 1 : 0)) : 0.0;

            FzRecyclerGUI.SetStatus(statusLabels, statusValues, 0, "STATUS", state);
            FzRecyclerGUI.SetStatus(statusLabels, statusValues, 1, "ITEM", running != null ? "" : "NONE");
            FzRecyclerGUI.SetStatus(statusLabels, statusValues, 2, running != null ? " " + RecipeName(running) : "", "");
            FzRecyclerGUI.SetStatus(statusLabels, statusValues, 3, "TIME LEFT", running != null ? FzNumeric.FormatDuration((float)Math.Ceiling(left)) : "-");
            FzRecyclerGUI.SetStatus(statusLabels, statusValues, 4, "PROGRESS", running != null && runTime > 0.0 ? $"{Math.Floor(done / runTime * 100.0)}%" : "-");
            FzRecyclerGUI.SetStatus(statusLabels, statusValues, 5, Fabricator.IsAuto(COSelf) ? "COUNT" : "COUNT ETA",
                Fabricator.IsAuto(COSelf) ? "AUTO" : counting ? FzNumeric.FormatDuration((float)Math.Ceiling(batchLeft)) : "-");
            FzRecyclerGUI.SetStatus(statusLabels, statusValues, 6, "BEAM FORMER", FzRecyclerGUI.FormerText(COSelf));
        }

        //AUTO builds whatever the count, so while it's on FABRICATE shows lit and can't be switched off. Its own
        //setting is kept, and shows again once AUTO is off.
        private void UpdateLamps()
        {
            if (fabricateBtn == null || COSelf == null) return;
            bool auto = Fabricator.IsAuto(COSelf);
            fabricateBtn.Enabled = !auto;
            fabricateBtn.SetOnWithoutNotify(auto || Fabricator.IsFabricateOn(COSelf));
            fillBtn.SetOnWithoutNotify(Fabricator.IsFillOn(COSelf));
        }

        //Switched on with nothing left to count, it builds one
        private void SetFabricate(bool on)
        {
            if (on && Fabricator.GetCount(COSelf) <= 0) Fabricator.SetCount(COSelf, 1);
            Fabricator.SetFabricateOn(COSelf, on);
            nextScan = 0f;
        }

        private void StepCount(int by)
        {
            Fabricator.SetCount(COSelf, Fabricator.GetCount(COSelf) + by);
            countDisplay.Value = Fabricator.GetCount(COSelf);
        }

        //A held COUNT key repeats after a moment, faster the longer it's held
        private void RepeatCount()
        {
            FzHoldBtn held = countMore.Held ? countMore : countLess.Held ? countLess : null;
            if (held == null || held.HeldTime < CountRepeatDelay)
            {
                if (held == null) countHeld = 0f;
                return;
            }
            countHeld += Time.unscaledDeltaTime * CountRepeatRate * Mathf.Pow(2f, held.HeldTime - CountRepeatDelay);
            int steps = Mathf.FloorToInt(countHeld);
            if (steps <= 0) return;
            countHeld -= steps;
            StepCount(held == countMore ? steps : -steps);
        }
    }
}
