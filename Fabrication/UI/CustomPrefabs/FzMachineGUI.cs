using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    //What the recycler's and fabricator's panels share: POWER with its draw, SHIP RESOURCES mirroring what the ship's
    //silos and reserve canisters hold, and SETUP for the working area's size with a map of it. Each panel builds these
    //where it wants them, and the rest of itself. Settings live in the machine's prop map through WorkArea.
    public abstract class FzMachineGUI : GUIData
    {
        protected const int ResourcePageSize = FzSiloGUI.PageSize; //Paged like a silo's, with the reserves on a page of their own
        protected const float ScanInterval = 0.25f; //Real seconds between scans of the ship and the area
        private const int MachineColumns = 3, MachineRows = 2;
        private const float CellFill = 0.84f; //Of a cell's pitch, leaving a gap between cells
        private static readonly Color MapMachineColor = new Color(0.45f, 0.50f, 0.45f);
        private static readonly Color MapAreaColor = new Color(0.30f, 0.62f, 0.22f);
        private static readonly Color MapBlockedColor = new Color(0.75f, 0.18f, 0.14f);
        private static readonly Color MapEmptyColor = new Color(0.07f, 0.11f, 0.07f);

        //POWER: its lamp, its draw, and its rocker
        protected static void CreatePowerPanel(GameObject root, float width, float height, float x, float y)
        {
            GameObject power = Controls.Panel.CreatePanel("PowerPanel", width, height, x, y, root);
            Controls.Panel.AddTitle(power, "POWER", 0.11f);
            FzLamp.Create(power, "PwrLamp", 0.84f, 0.15f, 0.08f, 0.66f, LampColor.Green, "POWER", stretch: true);
            FzNumeric.Create(power, "DrawDisplay", 0.84f, 0.15f, 0.08f, 0.38f, LampColor.Yellow, null, "0.0", "DRAW");
            FzRocker.Create(power, "PwrRocker", 0.4f, 0.27f, 0.3f, 0.05f, showIO: true);
        }

        protected static void CreateResourcesPanel(GameObject root, float width, float height, float x, float y)
        {
            GameObject resources = Controls.Panel.CreatePanel("ResourcesPanel", width, height, x, y, root);
            Controls.Panel.AddTitle(resources, "SHIP RESOURCES", 0.085f);
            CreateScreenArea(resources, "Resources");
            PageKeys(resources, "Res");
        }

        //SETUP: a map of the area beside a row each for its width and depth
        protected static void CreateSetupPanel(GameObject root, float width, float height, float x, float y)
        {
            GameObject setup = Controls.Panel.CreatePanel("SetupPanel", width, height, x, y, root);
            Controls.Panel.AddTitle(setup, "SETUP", 0.11f);
            GameObject mapScreen = FzMaterialScreen.CreateRect(setup, "MapScreen", 0.38f, 0.8f, 0.04f, 0.05f);
            Controls.Panel.CreateScreen(mapScreen);
            //Its cells are laid out when the panel opens, for the machine's own largest area (BuildMap)
            FzMaterialScreen.CreateRect(mapScreen, "Map", 0.9f, 0.9f, 0.05f, 0.05f);
            SizeRow(setup, "Width", "WIDTH", 0.55f);
            SizeRow(setup, "Depth", "DEPTH", 0.2f);
        }

        //A black screen filling most of a panel, above its row of page keys, with an area inside for what it shows
        protected static GameObject CreateScreenArea(GameObject panel, string name)
        {
            GameObject screen = FzMaterialScreen.CreateRect(panel, name + "Screen", 0.92f, 0.74f, 0.04f, 0.15f);
            Controls.Panel.CreateScreen(screen);
            return FzMaterialScreen.CreateRect(screen, name, 0.94f, 0.94f, 0.03f, 0.03f);
        }

        protected static void PageKeys(GameObject panel, string prefix)
        {
            FzHoldBtn.Create(panel, prefix + "PgUpBtn", "PGUP", 0.28f, 0.105f, 0.16f, 0.025f);
            FzHoldBtn.Create(panel, prefix + "PgDnBtn", "PGDN", 0.28f, 0.105f, 0.56f, 0.025f);
        }

        //A size setting, right of SETUP's map: a minus key, its value on a numeric display, a plus key, and a caption
        //under them
        private static void SizeRow(GameObject panel, string name, string caption, float y)
        {
            FzHoldBtn.Create(panel, name + "LessBtn", "-", 0.13f, 0.11f, 0.45f, y + 0.015f);
            FzNumeric.Create(panel, name + "Display", 0.24f, 0.14f, 0.59f, y, LampColor.Yellow, null, "0");
            FzHoldBtn.Create(panel, name + "MoreBtn", "+", 0.13f, 0.11f, 0.84f, y + 0.015f);
            FzSiloGUI.Caption(panel, name + "Caption", caption, y - 0.08f, 0.52f, 0.45f, 0.07f);
        }

        protected static string Dim(string text) => $"<color=#{ColorUtility.ToHtmlStringRGB(FzMaterialScreen.DimTextColor)}>{text}</color>";

        //Without the state words every loose item has, which say nothing here. Damaged stays, it's worth less.
        private static readonly System.Text.RegularExpressions.Regex StateWords =
            new System.Text.RegularExpressions.Regex(@"\b(?:Loose|Off)\b(?:,\s*)?|,\s*\b(?:Loose|Off)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex EmptyBrackets = new System.Text.RegularExpressions.Regex(@"\s*\(\s*\)");

        protected static string ItemName(CondOwner item) => CleanName(item.strNameFriendly ?? item.strCODef ?? "");

        protected static string CleanName(string name)
        {
            name = EmptyBrackets.Replace(StateWords.Replace(name ?? "", ""), "");
            return name.Trim().ToUpperInvariant();
        }

        private FzLamp pwrLamp;
        private FzNumeric drawDisplay, widthDisplay, depthDisplay;
        private FzMaterialScreen resources;
        private List<Reserves.Reserve> shipReserves = new List<Reserves.Reserve>();
        private Image[,] mapCells;
        private int mapColumns, mapRows;
        private int lastWidth = -1, lastDepth = -1;
        protected float nextScan;

        public override void Init(CondOwner coSelf, Dictionary<string, string> dict, string strCOKey)
        {
            base.Init(coSelf, dict, strCOKey);

            FzRocker pwrRocker = UnityUtil.FindDescendant<FzRocker>(transform, "PwrRocker");
            pwrRocker.SetOnWithoutNotify(!COSelf.HasCond("IsOverrideOff"));
            pwrRocker.OnChanged += SetPower;
            pwrLamp = UnityUtil.FindDescendant<FzLamp>(transform, "PwrLamp");
            drawDisplay = UnityUtil.FindDescendant<FzNumeric>(transform, "DrawDisplay");
            drawDisplay.Formatter = FzNumeric.FormatPower;

            //Every material any working silo on the ship can hold, and every reserve it has canisters for
            var materials = new HashSet<string>(Silo.GetWorking(COSelf.ship).SelectMany(Silo.Accepted));
            shipReserves = Reserves.All.Where(r => Reserves.Canisters(COSelf.ship, r).Count > 0).ToList();
            resources = new FzMaterialScreen(UnityUtil.FindDescendant(transform, "Resources").gameObject,
                FabData.Materials.Keys.Where(materials.Contains), ResourcePageSize, selectable: false, capacityLabel: "SILOS", reserves: shipReserves);
            //Shown even when everything fits on one page, as the machine panels always have them
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "ResPgUpBtn").Pressed += () => resources.TurnPage(-1);
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "ResPgDnBtn").Pressed += () => resources.TurnPage(1);

            BuildMap();

            widthDisplay = UnityUtil.FindDescendant<FzNumeric>(transform, "WidthDisplay");
            depthDisplay = UnityUtil.FindDescendant<FzNumeric>(transform, "DepthDisplay");
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "WidthLessBtn").Pressed += () => Resize(-1, 0);
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "WidthMoreBtn").Pressed += () => Resize(1, 0);
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "DepthLessBtn").Pressed += () => Resize(0, -1);
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "DepthMoreBtn").Pressed += () => Resize(0, 1);

            InitPanel();

            //The area shows while the panel is open, even with the power off, so it can be set up
            FzAreaProjector.Previewing = COSelf;

            nextScan = 0f;
            Update();
        }

        //The panel's own controls, found once the shared ones are
        protected abstract void InitPanel();

        //kW the machine draws now
        protected abstract double PowerDraw();

        //Every frame, after the shared readouts
        protected virtual void UpdateFrame() { }

        //Every scan, after the resources and the map
        protected abstract void UpdateScan();

        protected void OnDestroy()
        {
            if (COSelf != null && FzAreaProjector.Previewing == COSelf) FzAreaProjector.Previewing = null;
        }

        protected void Update()
        {
            if (COSelf == null || resources == null) return;
            pwrLamp.On = COSelf.HasCond("IsPowered");
            drawDisplay.Value = (float)PowerDraw();
            UpdateSetup();
            UpdateFrame();

            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + ScanInterval;
            UpdateResources();
            UpdateSetup(rescan: true);
            UpdateScan();
        }

        private void UpdateResources()
        {
            List<CondOwner> silos = Silo.GetWorking(COSelf.ship);
            double capacity = silos.Sum(Silo.Capacity), total = silos.Sum(Silo.Total);
            string status = silos.Count == 0 ? "NO SILOS ON SHIP" : $"{silos.Count} SILO{(silos.Count == 1 ? "" : "S")}";
            //Each reserve's canisters are found once a refresh, the screen asks for each reserve once
            var reserveLevels = shipReserves.ToDictionary(r => r, r =>
            {
                List<CondOwner> canisters = Reserves.Canisters(COSelf.ship, r);
                return (canisters.Sum(c => Reserves.Amount(c, r)), canisters.Sum(c => Reserves.Capacity(c, r)));
            });
            resources.Refresh(capacity, total, m => silos.Sum(s => Silo.Amount(s, m)), status,
                r => reserveLevels.TryGetValue(r, out var level) ? level : (0.0, 0.0));
        }

        //SETUP's map, sized for this machine's largest area: its columns, at least the machine's own three, and its
        //rows over the machine's two. The cells are kept square by a grid in the map at the grid's own shape,
        //fitted inside it, so a tier with a long narrow area gets a tall narrow map.
        private void BuildMap()
        {
            mapColumns = Math.Max(WorkArea.MaxWidth(COSelf), MachineColumns);
            mapRows = WorkArea.MaxDepth(COSelf) + MachineRows;
            GameObject map = UnityUtil.FindDescendant(transform, "Map").gameObject;
            Transform old = map.transform.Find("Grid");
            if (old != null) Destroy(old.gameObject);

            GameObject grid = FzMaterialScreen.CreateRect(map, "Grid", 1f, 1f, 0f, 0f);
            AspectRatioFitter fitter = grid.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)mapColumns / mapRows;

            float gap = (1f - CellFill) / 2f;
            mapCells = new Image[mapColumns, mapRows];
            for (int row = 0; row < mapRows; row++)
            {
                for (int col = 0; col < mapColumns; col++)
                {
                    GameObject cell = FzMaterialScreen.CreateRect(grid, $"Cell{col}_{row}", CellFill / mapColumns, CellFill / mapRows, (col + gap) / mapColumns, 1f - (row + 1f - gap) / mapRows);
                    Image image = cell.AddComponent<Image>();
                    image.color = MapEmptyColor;
                    image.raycastTarget = false;
                    mapCells[col, row] = image;
                }
            }
            lastWidth = lastDepth = -1;
        }

        private void Resize(int width, int depth)
        {
            if (width != 0) WorkArea.SetWidth(COSelf, WorkArea.GetWidth(COSelf) + width);
            if (depth != 0) WorkArea.SetDepth(COSelf, WorkArea.GetDepth(COSelf) + depth);
            nextScan = 0f; //What's in the area changes with it
        }

        //The numbers, and the map, the way the machine sits with its panel side down: its three columns across the
        //bottom two rows, the area above them. Tiles it can't work on, walls and anything behind them, are red. Walls
        //can change without the size changing, so a scan redraws it too.
        private void UpdateSetup(bool rescan = false)
        {
            int width = WorkArea.GetWidth(COSelf), depth = WorkArea.GetDepth(COSelf);
            if (!rescan && width == lastWidth && depth == lastDepth) return;
            lastWidth = width;
            lastDepth = depth;
            widthDisplay.Value = width;
            depthDisplay.Value = depth;
            bool[,] open = WorkArea.OpenTiles(COSelf);

            //The machine's centre column, with room either side for the widest area, which reaches one further left
            //when even
            int centre = mapColumns / 2;
            int firstArea = centre + WorkArea.FirstColumn(width);
            int baseRow = mapRows - MachineRows - 1; //The area's first row, against the machine
            for (int row = 0; row < mapRows; row++)
            {
                for (int col = 0; col < mapColumns; col++)
                {
                    bool machine = row >= mapRows - MachineRows && Math.Abs(col - centre) <= 1;
                    int areaCol = col - firstArea, areaRow = baseRow - row;
                    bool inArea = areaRow >= 0 && areaRow < depth && areaCol >= 0 && areaCol < width;
                    Color colour = machine ? MapMachineColor
                        : !inArea ? MapEmptyColor
                        : areaCol < open.GetLength(0) && areaRow < open.GetLength(1) && open[areaCol, areaRow] ? MapAreaColor
                        : MapBlockedColor;
                    mapCells[col, row].color = colour;
                }
            }
        }

        private void SetPower(bool on)
        {
            if (COSelf == null) return;
            COSelf.ZeroCondAmount("IsOverrideOff");
            COSelf.ZeroCondAmount("IsOverrideOn");
            COSelf.AddCondAmount(on ? "IsOverrideOn" : "IsOverrideOff", 1.0);
        }
    }
}
