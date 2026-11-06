using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    //The panel for any material silo. The screen lists every material the silo can hold, read from its conditions when
    //the panel opens, so one panel serves every kind of silo. Up and down pick a material, and holding DUMP, EMPTY or
    //STOCKPILE works on it, slowly at first and faster the longer it's held.
    public class FzSiloGUI : GUIData
    {
        public const string PrefabName = "FzSiloGUI";
        private const string SelectedProp = "FzSiloSelected"; //On the silo, so it opens on the material last picked

        public static readonly Color CaptionColor = new Color(0.625f, 0.625f, 0.625f); //The time/zoom module's QUICK ZOOM caption

        //Hold rates in units a second: it starts at BaseRate and doubles every DoubleTime seconds held, up to MaxRate
        private const double BaseRate = 5.0;
        private const double DoubleTime = 0.5;
        private const double MaxRate = 50000.0;
        private const double SnapUnits = 0.5; //Less than this left over is taken too, so a hold can finish a material

        //Materials one page of the screen shows. Every material this mod has fits on one page.
        public const int PageSize = 11;
        private const float SelectCaptionY = 0.70f, SelectCaptionPagedY = 0.605f;

        public static GameObject CreatePrefab()
        {
            GameObject root = Controls.Panel.CreateContainer(PrefabName, 0.40f, 0.45f, 0.30f, 0.28f);
            root.AddComponent<Animator>();
            root.AddComponent<FzSiloGUI>();

            GameObject mainPanel = Controls.Panel.CreatePanel("MainPanel", 0.72f, 1f, 0f, 0f, root);
            Controls.Panel.AddTitle(mainPanel, "MATERIAL SILO", 0.08f).name = "SiloTitle"; //Set to the silo's own name when it opens
            GameObject screen = FzMaterialScreen.CreateRect(mainPanel, "Screen", 0.92f, 0.86f, 0.04f, 0.03f);
            Controls.Panel.CreateScreen(screen);
            FzMaterialScreen.CreateRect(screen, "Rows", 0.94f, 0.94f, 0.03f, 0.03f);

            GameObject controls = Controls.Panel.CreatePanel("ControlPanel", 0.265f, 1f, 0.735f, 0f, root);
            Controls.Panel.AddTitle(controls, "CONTROL", 0.08f);
            //Keys like the time/zoom module's, with a caption under each group as it has. The page keys show only when
            //the silo's materials need more than one page, and the SELECT caption moves up under UP and DOWN without them.
            FzHoldBtn.Create(controls, "UpBtn", "UP", 0.40f, 0.085f, 0.07f, 0.76f);
            FzHoldBtn.Create(controls, "DownBtn", "DOWN", 0.40f, 0.085f, 0.53f, 0.76f);
            FzHoldBtn.Create(controls, "PgUpBtn", "PGUP", 0.40f, 0.085f, 0.07f, 0.665f);
            FzHoldBtn.Create(controls, "PgDnBtn", "PGDN", 0.40f, 0.085f, 0.53f, 0.665f);
            Caption(controls, "SelectCaption", "SELECT", SelectCaptionPagedY);
            FzHoldBtn.Create(controls, "DumpBtn", "DUMP", 0.76f, 0.085f, 0.12f, 0.47f);
            FzHoldBtn.Create(controls, "EmptyBtn", "EMPTY", 0.76f, 0.085f, 0.12f, 0.36f);
            FzHoldBtn.Create(controls, "StockpileBtn", "STOCKPILE", 0.76f, 0.085f, 0.12f, 0.25f);
            Caption(controls, "HoldCaption", "HOLD TO RUN", 0.19f);

            return root;
        }

        public static TMP_Text Caption(GameObject panel, string name, string text, float y, float width = 0.9f, float x = 0.05f, float height = 0.045f)
        {
            TMP_Text caption = Controls.Label.Create(panel, name, text, width, height, x, y, 8f, 8f).GetComponent<TMP_Text>();
            caption.color = CaptionColor;
            return caption;
        }

        private enum SiloAction { None, Dump, Empty, Stockpile }

        private FzMaterialScreen screen;
        private FzHoldBtn dumpBtn, emptyBtn, stockpileBtn;
        private string failure; //Why the running action moved nothing, shown until it's let go

        public override void Init(CondOwner coSelf, Dictionary<string, string> dict, string strCOKey)
        {
            base.Init(coSelf, dict, strCOKey);

            TMP_Text title = UnityUtil.FindDescendant<TMP_Text>(transform, "SiloTitle");
            if (title != null && COSelf != null) title.text = (COSelf.strNameFriendly ?? "MATERIAL SILO").ToUpperInvariant();

            GameObject rows = UnityUtil.FindDescendant(transform, "Rows").gameObject;
            screen = new FzMaterialScreen(rows, COSelf != null ? Silo.Accepted(COSelf) : new List<string>(), PageSize, selectable: true);

            UnityUtil.FindDescendant<FzHoldBtn>(transform, "UpBtn").Pressed += () => Select(screen.Selected - 1);
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "DownBtn").Pressed += () => Select(screen.Selected + 1);
            FzHoldBtn pgUp = UnityUtil.FindDescendant<FzHoldBtn>(transform, "PgUpBtn");
            FzHoldBtn pgDn = UnityUtil.FindDescendant<FzHoldBtn>(transform, "PgDnBtn");
            pgUp.Pressed += () => { screen.TurnPage(-1); Selected(); };
            pgDn.Pressed += () => { screen.TurnPage(1); Selected(); };
            pgUp.gameObject.SetActive(screen.Paged);
            pgDn.gameObject.SetActive(screen.Paged);
            FzUIElement selectCaption = UnityUtil.FindDescendant<FzUIElement>(transform, "SelectCaption");
            if (selectCaption != null) selectCaption.OffsetY = screen.Paged ? SelectCaptionPagedY : SelectCaptionY;

            dumpBtn = UnityUtil.FindDescendant<FzHoldBtn>(transform, "DumpBtn");
            emptyBtn = UnityUtil.FindDescendant<FzHoldBtn>(transform, "EmptyBtn");
            stockpileBtn = UnityUtil.FindDescendant<FzHoldBtn>(transform, "StockpileBtn");
            foreach (FzHoldBtn btn in new[] { dumpBtn, emptyBtn, stockpileBtn }) btn.Released += () => failure = null;

            string saved = dictPropMap != null && dictPropMap.TryGetValue(SelectedProp, out string value) ? value : null;
            Select(Math.Max(0, screen.IndexOf(saved)));
            Refresh();
        }

        private void Select(int index)
        {
            screen.Select(index);
            Selected();
        }

        private void Selected()
        {
            failure = null;
            if (screen.SelectedMaterial != null) SetPropMapData(SelectedProp, screen.SelectedMaterial);
        }

        private void Update()
        {
            if (COSelf == null || screen == null) return;
            RunHeld();
            Refresh();
        }

        //The first of the action buttons being held, if any
        private SiloAction HeldAction(out FzHoldBtn btn)
        {
            btn = null;
            if (dumpBtn != null && dumpBtn.Held) { btn = dumpBtn; return SiloAction.Dump; }
            if (emptyBtn != null && emptyBtn.Held) { btn = emptyBtn; return SiloAction.Empty; }
            if (stockpileBtn != null && stockpileBtn.Held) { btn = stockpileBtn; return SiloAction.Stockpile; }
            return SiloAction.None;
        }

        private static double RateFor(float heldTime) => Math.Min(MaxRate, BaseRate * Math.Pow(2.0, heldTime / DoubleTime));

        private void RunHeld()
        {
            SiloAction action = HeldAction(out FzHoldBtn btn);
            string material = screen.SelectedMaterial;
            if (action == SiloAction.None || material == null) return;
            double want = RateFor(btn.HeldTime) * Time.unscaledDeltaTime;
            double have = Silo.Amount(COSelf, material);

            switch (action)
            {
                case SiloAction.Dump:
                    if (have <= 0) { failure = "NOTHING TO DUMP"; return; }
                    if (have - want < SnapUnits) want = have;
                    Silo.Remove(COSelf, material, want);
                    break;
                case SiloAction.Empty:
                    if (have <= 0) { failure = "NOTHING TO EMPTY"; return; }
                    if (have - want < SnapUnits) want = have;
                    if (Silo.MoveOut(COSelf, material, want) <= 0) failure = "NO ROOM IN OTHER SILOS";
                    break;
                case SiloAction.Stockpile:
                    double free = Silo.Free(COSelf);
                    if (free <= 0) { failure = "SILO FULL"; return; }
                    if (free - want < SnapUnits) want = free;
                    if (Silo.MoveIn(COSelf, material, want) <= 0) failure = "NONE IN OTHER SILOS";
                    break;
            }
        }

        private void Refresh()
        {
            screen.Refresh(Silo.Capacity(COSelf), Silo.Total(COSelf), m => Silo.Amount(COSelf, m), Status());
        }

        private string Status()
        {
            if (screen.Count == 0) return "HOLDS NO KNOWN MATERIAL";
            SiloAction action = HeldAction(out FzHoldBtn btn);
            if (action == SiloAction.None) return "HOLD DUMP, EMPTY OR STOCKPILE";
            if (failure != null) return failure;
            string verb = action == SiloAction.Dump ? "DUMPING" : action == SiloAction.Empty ? "EMPTYING" : "STOCKPILING";
            return $"{verb} {screen.SelectedName}  {FzMaterialScreen.Units(RateFor(btn.HeldTime))} U/S";
        }
    }
}
