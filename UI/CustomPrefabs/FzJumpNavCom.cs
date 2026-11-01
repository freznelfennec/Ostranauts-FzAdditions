using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vectrosity;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    //Nav station module laid out like Sensors. Each page gives the keys under the screen their own actions,
    //shown along the bottom of the screen. The first key leaves the page, for MAIN unless the page says
    //otherwise, and the last is always JUMP.
    //COSelf here is the nav station, not the module's item.
    public class FzJumpNavCom : NavModBase
    {
        private enum Page { Main, Field, Energy, Plot, Target }

        private static readonly string FramePrefab = "GUIShip/GUIOrbitDraw/NavModSensorsMFD";
        private static readonly string FrameSprite = "GUIPanel256x256";
        private static readonly string InsetSprite = "GUIRectEmbossed";
        private static readonly Color OverlayColor = new Color(1f, 1f, 1f, 0.004f); //Edit mode's drag target, nearly invisible
        private static readonly Color BackgroundColor = new Color(0.251f, 0.306f, 0.380f);
        private static readonly Color InsetColor = new Color(0.286f, 0.314f, 0.341f);
        private static readonly Color TitleColor = new Color(0.827f, 0.827f, 0.827f);
        private static readonly Color ScreenTextColor = new Color(0.631f, 0.894f, 0.392f);
        private static readonly Color ActionErrorColor = new Color(1f, 0.188f, 0.188f);
        private static readonly string NormalColor = "#3CFF3C";
        private static readonly string WarnColor = "#FFD800";
        private static readonly string DangerColor = "#FF3030";
        private static readonly float TitleMinFontSize = 6f;
        private static readonly float TitleMaxFontSize = 10f;
        private static readonly float ScreenMinFontSize = 6f;
        private static readonly float ScreenMaxFontSize = 10f;
        private static readonly float ActionMinFontSize = 5f;
        private static readonly float ActionMaxFontSize = 8f;
        private static readonly float ActionHeight = 0.12f; //Of the screen
        private static readonly float ActionWidth = 0.95f; //Of each key's slot
        private static readonly int KeyCount = 6;
        private static readonly float FlashDuration = 0.6f; //Real seconds
        private static readonly float PlotMarkerSize = 6f; //Map canvas units
        private static readonly int MaxPlotMarkers = 64; //An uncapped series could otherwise ask for any number
        private static readonly Color PlotColor = new Color(0.25f, 0.88f, 1f, 0.9f);
        private static readonly AccessTools.FieldRef<GUIOrbitDraw, GameObject> OrbitPanel = AccessTools.FieldRefAccess<GUIOrbitDraw, GameObject>("goOrbitPanel");
        private static readonly float SettingRate = 0.5f; //Of the setting's range per real second, with the slider at its end
        private static readonly float DataInterval = 0.5f; //Real seconds between refreshes, reading the cells scans the ship
        private static readonly float MapInterval = 2f; //Real seconds between redraws, classifying the ship is slow

        private const string MainKey = "MAIN";
        private const string JumpKey = "JUMP";
        private const string FieldKey = "FIELD";
        private const string EnergyKey = "ENERGY";
        private const string PlotKey = "PLOT";
        private const string StrengthKey = "STRENGTH";
        private const string EmittersKey = "EMITTERS";
        private const string TargetKey = "TRG";
        private const string BackKey = "BACK";
        private const string CalcKey = "CALC";
        private const string ShowKey = "SHOW";
        private const string SingleKey = "SINGLE";
        private const string SeriesKey = "SERIES";
        private const string ExecuteKey = "EXECUTE";
        private const string TorchKey = "TORCH";
        private const string RcsKey = "RCS";
        private const string CancelKey = "CANCEL";
        private const int CalcKeyIndex = 1;

        private static GameObject prefabHolder;

        public static GameObject CreatePrefab()
        {
            return CreatePrefab("FzJumpNavCom", false);
        }

        public static GameObject CreateDamagedPrefab()
        {
            return CreatePrefab("FzJumpNavComDmg", true);
        }

        //LoadModules sizes a disabled module through its Awake, so the root has to be active. Under an
        //inactive holder it stays dormant here, and its copies wake up in the nav station.
        private static GameObject CreatePrefab(string name, bool damaged)
        {
            if (prefabHolder == null)
            {
                prefabHolder = new GameObject("FzNavModPrefabs");
                prefabHolder.SetActive(false);
                DontDestroyOnLoad(prefabHolder);
            }

            GameObject root = new GameObject(name, typeof(RectTransform), typeof(FzUIElement), typeof(FzJumpNavCom));
            root.layer = LayerMask.NameToLayer("UI");
            root.GetComponent<RectTransform>().SetParent(prefabHolder.transform, false);
            root.GetComponent<FzUIElement>().SetTransform(1f, 1f, 0f, 0f);

            GameObject container = CreateLayer(root, "Container", 0.25f, 0.4f, 0f, 0f, null, OverlayColor);
            container.AddComponent<CanvasGroup>();
            container.AddComponent<Ostranauts.ShipGUIs.NavStation.Draggable>(); //Named in full, the game has a global Draggable too

            CreateLayer(container, "bg", 1f, 1f, 0f, 0f, null, BackgroundColor);
            CreateLayer(container, "pnlScrews", 1f, 1f, 0f, 0f, SpriteUtil.FindSprite(FramePrefab, FrameSprite), Color.white);
            Label.Create(container, "lblTitle", "Jump Drive", 0.69f, 0.07f, 0.15f, 0.91f, TitleMinFontSize, TitleMaxFontSize).GetComponent<TMP_Text>().color = TitleColor;
            CreateLayer(container, "bgPanel", 0.96f, 0.82f, 0.02f, 0.09f, SpriteUtil.FindSprite(FramePrefab, InsetSprite), InsetColor);

            if (damaged) return root;

            FzStickySlider.Create(container, "SettingSlider", 0.88f, 0.075f, 0.06f, 0.785f, "-", "+");

            GameObject screen = CreateLayer(container, "Screen", 0.88f, 0.645f, 0.06f, 0.12f, null, Color.black);

            //Under the text, so the FIELD page can say when it has nothing to show
            FzFieldMap.Create(screen, "FieldMap", 1f, 1f - ActionHeight, 0f, ActionHeight);

            TMP_Text text = Label.Create(screen, "txtScreen", "", 0.92f, 0.82f, 0.04f, 0.15f, ScreenMinFontSize, ScreenMaxFontSize).GetComponent<TMP_Text>();
            text.color = ScreenTextColor;
            text.alignment = TextAlignmentOptions.TopLeft;

            float slot = 1f / KeyCount;
            for (int i = 0; i < KeyCount; i++)
            {
                TMP_Text action = Label.Create(screen, "txtAction" + i, "", slot * ActionWidth, ActionHeight, slot * i + slot * (1f - ActionWidth) / 2f, 0f, ActionMinFontSize, ActionMaxFontSize).GetComponent<TMP_Text>();
                action.color = ScreenTextColor;
            }

            FzSoftKeyRow.Create(container, "KeyRow", 0.88f, 0.065f, 0.06f, 0.022f, KeyCount);

            return root;
        }

        private static GameObject CreateLayer(GameObject parent, string name, float width, float height, float offsetX, float offsetY, Sprite sprite, Color color)
        {
            GameObject layer = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(FzUIElement));
            layer.layer = LayerMask.NameToLayer("UI");
            layer.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            layer.GetComponent<FzUIElement>().SetTransform(width, height, offsetX, offsetY);

            Image image = layer.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            if (sprite != null) image.type = Image.Type.Sliced;
            //Only the container takes the pointer, for dragging in edit mode
            image.raycastTarget = name == "Container";
            return layer;
        }

        private TMP_Text screenText;
        private TMP_Text[] actionTexts;
        private FzFieldMap fieldMap;
        private bool wired;
        private bool hasContour;
        private Page page = Page.Main;
        private float setting;
        private float nextDataTime;
        private float nextMapTime;
        private float flashEndTime;
        private bool showPlot;
        private bool seriesMode;
        private VectorLine plotLine;
        private readonly List<Vector2> plotPoints = new List<Vector2>();

        //Read together on an interval, so the slider can redraw the screen every frame without scanning the ship
        private int cellCount;
        private int poweredCells;
        private int activeTaps;
        private double available;
        private double capacity;
        private double chargeRate;
        private double chargeTime;
        private double minSetting;
        private double averageStrength;
        private double uncertainty;
        private double speed;
        private JumpDrive.JumpTarget target;
        private bool brakeOnTorch;
        private double fullMargin;
        private double chargeWait;
        private double seriesEnergy = -1.0;
        private double fillRate;
        private double seriesWait;
        private JumpSequence.SeriesState seriesState;
        private FieldContour.EmitterStatus emitterStatus;

        //A series recharges between jumps, so it isn't limited by what the cells hold at once
        private double MaxSetting => seriesMode ? float.MaxValue : Math.Max(minSetting, capacity);

        private bool IsJumping => JumpSequence.IsActive(COSelf?.ship) || JumpSequence.IsSeriesRunning(COSelf?.ship);

        //Runs again each time the nav station leaves edit mode
        protected override void Init()
        {
            screenText = UnityUtil.FindDescendant(transform, "txtScreen")?.GetComponent<TMP_Text>();
            if (screenText == null) return;

            fieldMap = UnityUtil.FindDescendant(transform, "FieldMap").GetComponent<FzFieldMap>();
            actionTexts = Enumerable.Range(0, KeyCount).Select(i => UnityUtil.FindDescendant(transform, "txtAction" + i).GetComponent<TMP_Text>()).ToArray();

            //The console writes its own copy of these back when it closes, so that is the one that lasts
            Dictionary<string, string> props = _guiOrbitDraw.GetPropMap();
            if (props.TryGetValue(Constants.NavComSettingProp, out string value)) float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out setting);
            showPlot = _guiOrbitDraw.GetPropMapData(Constants.NavComShowPlotProp, false);
            seriesMode = _guiOrbitDraw.GetPropMapData(Constants.NavComSeriesProp, false);

            if (!wired)
            {
                wired = true;
                UnityUtil.FindDescendant(transform, "KeyRow").GetComponent<FzSoftKeyRow>().OnPressed += PressKey;
                UnityUtil.FindDescendant(transform, "SettingSlider").GetComponent<FzStickySlider>().OnHeld += AdjustSetting;
            }

            ShowPage(page);
        }

        private string[] GetActions()
        {
            string[] actions = new string[KeyCount];
            actions[0] = MainKey;
            actions[KeyCount - 1] = IsJumping ? CancelKey : seriesMode ? ExecuteKey : JumpKey;

            if (page == Page.Main)
            {
                actions[1] = FieldKey;
                actions[2] = EnergyKey;
                actions[3] = PlotKey;
            }
            else if (page == Page.Field)
            {
                actions[1] = StrengthKey;
                actions[2] = EmittersKey;
            }
            else if (page == Page.Plot)
            {
                actions[1] = TargetKey;
                actions[3] = seriesMode ? SeriesKey : SingleKey;
                actions[4] = ShowKey;
            }
            else if (page == Page.Target)
            {
                actions[0] = BackKey;
                if (target.CanPlot) actions[1] = target.BrakesOnTorch ? TorchKey : RcsKey;
                actions[4] = CalcKey;
            }
            return actions;
        }

        private void PressKey(int key)
        {
            switch (GetActions()[key])
            {
                case MainKey: ShowPage(Page.Main); break;
                case FieldKey: ShowPage(Page.Field); break;
                case EnergyKey: ShowPage(Page.Energy); break;
                case PlotKey: ShowPage(Page.Plot); break;
                case StrengthKey: fieldMap.ShowStrength = !fieldMap.ShowStrength; nextMapTime = 0f; break;
                case EmittersKey: fieldMap.ShowEmitters = !fieldMap.ShowEmitters; nextMapTime = 0f; break;
                case TargetKey: ShowPage(Page.Target); break;
                case BackKey: ShowPage(Page.Plot); break;
                case CalcKey: SetSettingToTarget(); break;
                case ShowKey:
                    showPlot = !showPlot;
                    _guiOrbitDraw.SetPropMapData(Constants.NavComShowPlotProp, showPlot.ToString().ToLower());
                    break;
                case SingleKey:
                case SeriesKey:
                    SetSeriesMode(!seriesMode);
                    break;
                case TorchKey:
                case RcsKey:
                    brakeOnTorch = !target.BrakesOnTorch;
                    ReadData();
                    RenderScreen();
                    break;
                case CancelKey: JumpSequence.Cancel(COSelf?.ship); break;
                case JumpKey:
                    ReadData();
                    if (!JumpSequence.Start(COSelf?.ship, setting)) FlashKey(KeyCount - 1);
                    break;
                case ExecuteKey:
                    ReadData();
                    if (!JumpSequence.StartSeries(COSelf?.ship, setting)) FlashKey(KeyCount - 1);
                    break;
            }
        }

        private void ShowPage(Page shown)
        {
            page = shown;
            fieldMap.gameObject.SetActive(page == Page.Field);
            ClearFlash();
            RenderActions();

            nextDataTime = 0f;
            nextMapTime = 0f;
        }

        //Unscaled so it can be set while the game is paused, the further from centre the faster it moves
        private void AdjustSetting(float pull)
        {
            float step = Mathf.Sign(pull) * pull * pull * SettingRate * (float)(seriesMode ? Math.Max(setting, capacity) : MaxSetting - minSetting) * Time.unscaledDeltaTime;
            SetSetting(setting + step);
            RenderScreen();
        }

        //Flashes the key rather than settling for a setting that wouldn't reach
        private void SetSettingToTarget()
        {
            ReadData();
            double energy = TargetEnergy;
            if (!target.CanPlot || energy < minSetting || energy > MaxSetting)
            {
                FlashKey(CalcKeyIndex);
                return;
            }

            SetSetting((float)energy);
            ShowPage(Page.Plot);
        }

        private void FlashKey(int key)
        {
            ClearFlash();
            actionTexts[key].color = ActionErrorColor;
            flashEndTime = Time.unscaledTime + FlashDuration;
        }

        private void ClearFlash()
        {
            flashEndTime = 0f;
            foreach (TMP_Text action in actionTexts) action.color = ScreenTextColor;
        }

        private void RenderActions()
        {
            string[] actions = GetActions();
            for (int i = 0; i < KeyCount; i++)
            {
                string text = actions[i] ?? "";
                if (actionTexts[i].text != text) actionTexts[i].text = text;
            }
        }

        //Actions are read every frame, the jump key follows a sequence that ends on its own
        private void Update()
        {
            if (actionTexts == null) return;

            if (JumpSequence.TakeFieldFailure(COSelf?.ship))
            {
                ShowPage(Page.Field);
                FlashKey(KeyCount - 1);
            }

            if (JumpSequence.TakeSeriesFinished(COSelf?.ship)) SetSeriesMode(false);

            RenderActions();
            if (flashEndTime > 0f && Time.unscaledTime >= flashEndTime) ClearFlash();
        }

        private void SetSeriesMode(bool on)
        {
            seriesMode = on;
            _guiOrbitDraw.SetPropMapData(Constants.NavComSeriesProp, seriesMode.ToString().ToLower());
            ReadData();
            SetSetting(setting);
            RenderScreen();
        }

        //Every frame, since the map pans and zooms and the ship moves. After the map's own update.
        private void LateUpdate()
        {
            plotPoints.Clear();

            ShipSitu situ = COSelf?.ship?.objSS;
            JumpDrive.GetVelocity(COSelf?.ship, out double velocityX, out double velocityY);
            double magnitude = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
            if (showPlot && situ != null && _guiOrbitDraw != null && magnitude > 0.0)
            {
                _guiOrbitDraw.SolarToCanvas(situ.vPosx, situ.vPosy, out double startX, out double startY);
                double plotDistance = seriesMode ? JumpDistance + GetCoastDistance(PlotEnergy) : JumpDistance;
                Vector2 end = GetPlotPoint(situ, velocityX, velocityY, magnitude, plotDistance);
                plotPoints.Add(new Vector2((float)startX, (float)startY));
                plotPoints.Add(end);

                //Each arrival in a series, then the end of the plot however many jumps there are
                if (seriesMode)
                {
                    JumpDrive.JumpSeries series = Series;
                    int fullJumps = Math.Min(series.Jumps, MaxPlotMarkers);
                    for (int i = 1; i <= fullJumps; i++)
                    {
                        double distance = series.FullJumpDistance * i + GetCoastDistance(Math.Min(capacity * i, PlotEnergy));
                        if (distance < plotDistance) AddPlotMarker(GetPlotPoint(situ, velocityX, velocityY, magnitude, distance));
                    }
                }

                AddPlotMarker(end);
            }

            if (plotPoints.Count == 0 && plotLine == null) return;

            if (plotLine == null)
            {
                plotLine = NavIcon.SetupVectorLine("FzJumpPlot", PlotColor, OrbitPanel(_guiOrbitDraw), new List<Vector2>(plotPoints));
            }

            plotLine.points2 = new List<Vector2>(plotPoints);
            plotLine.matrix = Matrix4x4.Scale(Vector3.one);
            plotLine.Draw();
        }

        //In AU, how far the ship coasts while its cells recharge to spend this much of a series
        private double GetCoastDistance(double energy)
        {
            return fillRate > 0.0 ? speed * Math.Max(energy - available, 0.0) / fillRate : 0.0;
        }

        private Vector2 GetPlotPoint(ShipSitu situ, double velocityX, double velocityY, double speed, double distance)
        {
            double scale = distance / speed;
            _guiOrbitDraw.SolarToCanvas(situ.vPosx + velocityX * scale, situ.vPosy + velocityY * scale, out double x, out double y);
            return new Vector2((float)x, (float)y);
        }

        private void AddPlotMarker(Vector2 point)
        {
            plotPoints.Add(point + new Vector2(-PlotMarkerSize, -PlotMarkerSize));
            plotPoints.Add(point + new Vector2(PlotMarkerSize, PlotMarkerSize));
            plotPoints.Add(point + new Vector2(-PlotMarkerSize, PlotMarkerSize));
            plotPoints.Add(point + new Vector2(PlotMarkerSize, -PlotMarkerSize));
        }

        //Also runs when the module is taken off the console, so the line never outlives it
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (plotLine != null) VectorLine.Destroy(ref plotLine);
        }

        //Never below what the emitters need to raise the field
        private void SetSetting(float value)
        {
            float clamped = Mathf.Clamp(value, (float)minSetting, (float)MaxSetting);
            if (clamped == setting) return;
            setting = clamped;
            _guiOrbitDraw.SetPropMapData(Constants.NavComSettingProp, setting.ToString(CultureInfo.InvariantCulture));
        }

        protected override void UpdateUI()
        {
            if (screenText == null || COSelf == null) return;

            if (Time.unscaledTime >= nextDataTime)
            {
                nextDataTime = Time.unscaledTime + DataInterval;
                ReadData();
                SetSetting(setting);
                RenderScreen();
            }

            if (page == Page.Field && Time.unscaledTime >= nextMapTime)
            {
                nextMapTime = Time.unscaledTime + MapInterval;
                hasContour = fieldMap.Render(COSelf.ship);
                RenderScreen();
            }
        }

        private void ReadData()
        {
            Ship ship = COSelf.ship;
            List<CondOwner> installed = JumpCell.GetAllCells(ship);
            List<CondOwner> cells = installed.Where(JumpCell.IsOn).ToList();

            cellCount = installed.Count;
            poweredCells = cells.Count;
            available = cells.Sum(GetCharge);
            capacity = cells.Sum(JumpCell.GetCapacity);
            chargeRate = cells.Sum(JumpCell.GetCurrentRate);
            chargeTime = cells.Count > 0 ? cells.Max(JumpCell.GetChargeTime) : -1.0;
            activeTaps = ship != null ? ship.GetCOs(DataHandler.GetCondTrigger(Constants.TapActiveTrigger), bSubObjects: false, bAllowDocked: false, bAllowLocked: false).Count : 0;
            minSetting = FieldContour.GetTotalFieldEnergy(ship);
            emitterStatus = FieldContour.GetEmitterStatus(ship);
            averageStrength = JumpDrive.GetAverageStrength(ship);
            uncertainty = JumpDrive.GetUncertainty(ship);
            speed = JumpDrive.GetSpeed(ship);
            target = JumpDrive.GetTarget(ship, GUIOrbitDraw.CrossHairTarget, brakeOnTorch);
            fullMargin = JumpCell.GetFullMargin(cells);
            fillRate = JumpCell.GetFillRate(ship, cells);
            chargeWait = JumpCell.GetTimeUntilCharged(cells, setting - fullMargin);
            seriesEnergy = JumpSequence.GetSeriesEnergy(ship);
            seriesState = JumpSequence.GetSeriesState(ship, out seriesWait);
        }

        //Total kWh for a jump to the target, negative if it is out of reach
        private double TargetEnergy
        {
            get
            {
                if (seriesMode) return JumpDrive.GetSeriesEnergy(target, minSetting, capacity, speed, averageStrength, available, fillRate);

                double excess = JumpDrive.GetEnergyToReach(target, speed, averageStrength);
                return excess < 0.0 ? -1.0 : minSetting + excess;
            }
        }

        //What is left of a running series, otherwise the setting
        private double PlotEnergy => seriesEnergy >= 0.0 ? seriesEnergy : setting;

        private JumpDrive.JumpSeries Series => JumpDrive.GetSeries(PlotEnergy, minSetting, capacity, speed, averageStrength);

        private double JumpTime => seriesMode ? Series.JumpTime : JumpDrive.GetJumpTime(setting - minSetting, averageStrength);

        private double JumpDistance => seriesMode ? Series.Distance : JumpDrive.GetDistance(speed, JumpTime);

        //If all of the available energy went into one jump, so the setting has no part in it
        private double TotalJumpDistance => JumpDrive.GetDistance(speed, JumpDrive.GetJumpTime(available - minSetting, averageStrength));

        private int JumpsAvailable => setting > 0f ? (int)Math.Floor((available + fullMargin) / setting) : 0;

        private static double GetCharge(CondOwner cell)
        {
            return cell.GetCondAmount(Constants.CellChargeCond);
        }

        private void RenderScreen()
        {
            screenText.text = string.Join("\n", GetLines());
        }

        private IEnumerable<string> GetLines()
        {
            switch (page)
            {
                case Page.Field: return hasContour ? new string[0] : new[] { "NO FIELD CONTOUR" };
                case Page.Energy: return GetEnergyLines();
                case Page.Plot: return GetPlotLines();
                case Page.Target: return GetTargetLines();
                default: return GetMainLines();
            }
        }

        private IEnumerable<string> GetMainLines()
        {
            List<string> lines = new List<string>
            {
                Title(MainKey),
                $"EMITTERS: {FormatStatus(emitterStatus)}",
                $"CHARGE: {FormatPercent(available, capacity)}",
                $"RATE: {FzNumeric.FormatPower((float)chargeRate)}",
                $"FULL IN: {FormatTime()}",
                $"DISTANCE: {MathUtils.GetDistUnits(JumpDistance)}",
                SeriesLine
            };
            if (seriesMode) lines.Add(SeriesEtaLine);
            return lines;
        }

        private IEnumerable<string> GetEnergyLines()
        {
            return new[]
            {
                Title(EnergyKey),
                $"CELLS POWERED: {poweredCells}/{cellCount}",
                $"AVAILABLE: {FzNumeric.FormatEnergy((float)available)}",
                $"CHARGE: {FormatPercent(available, capacity)}",
                $"RATE: {FzNumeric.FormatPower((float)chargeRate)}",
                $"FULL IN: {FormatTime()}",
                $"ACTIVE TAPS: {activeTaps}"
            };
        }

        private IEnumerable<string> GetPlotLines()
        {
            List<string> lines = new List<string>
            {
                Title(seriesMode ? PlotKey + " SERIES" : PlotKey),
                $"SETTING: {FzNumeric.FormatEnergy((float)PlotEnergy)}",
                $"OF AVAILABLE: {FormatPercent(PlotEnergy, available)}",
                seriesMode ? $"JUMPS: {Series.Jumps}" : JumpsAvailable > 0 ? $"JUMPS: {JumpsAvailable}" : $"JUMPS: 0 READY IN {FzNumeric.FormatDuration((float)Math.Ceiling(chargeWait))}",
                $"JUMP TIME: {FzNumeric.FormatDuration((float)Math.Ceiling(JumpTime))} +/-{uncertainty:0.#}%",
                $"JUMP DISTANCE: {MathUtils.GetDistUnits(JumpDistance)}",
                $"TOTAL JUMP DISTANCE: {MathUtils.GetDistUnits(TotalJumpDistance)}"
            };
            if (seriesMode)
            {
                lines.Add(SeriesLine);
                lines.Add(SeriesEtaLine);
            }
            return lines;
        }

        private IEnumerable<string> GetTargetLines()
        {
            if (!target.CanPlot)
            {
                return new[]
                {
                    Title(seriesMode ? TargetKey + " SERIES" : TargetKey),
                    $"NAME: {target.Name}",
                    $"TYPE: {target.Type.ToString().ToUpper()}",
                    "CANNOT PLOT TO TARGET"
                };
            }

            double minSpeed = JumpDrive.GetMinSpeed(target, JumpDrive.GetJumpTime(available - minSetting, averageStrength));
            double energy = TargetEnergy;
            return new[]
            {
                Title(seriesMode ? TargetKey + " SERIES" : TargetKey),
                $"NAME: {target.Name}",
                $"TYPE: {target.Type.ToString().ToUpper()}",
                $"DIST: {MathUtils.GetDistUnits(target.Distance)}",
                $"BUFFER: {MathUtils.GetDistUnits(target.Buffer)}",
                $"BRAKING: {MathUtils.GetDistUnits(target.Braking)}",
                $"RVEL: {MathUtils.GetDistUnits(target.RelativeSpeed)}/s",
                DecelEtaLine,
                $"MIN PLOT VEL: {(minSpeed < 0.0 ? "--" : MathUtils.GetDistUnits(minSpeed) + "/s")}",
                $"MIN PLOT ENERGY: {(energy < 0.0 ? "--" : FzNumeric.FormatEnergy((float)energy))}"
            };
        }

        private static string Title(string name)
        {
            return $"<align=center>====[ {name} ]====</align>";
        }

        private string SeriesLine
        {
            get
            {
                switch (seriesState)
                {
                    case JumpSequence.SeriesState.Jumping: return "SERIES: JUMPING";
                    case JumpSequence.SeriesState.Thrusting: return "SERIES: HOLDING FOR THRUST";
                    case JumpSequence.SeriesState.Recharging: return $"SERIES: NEXT JUMP IN {FzNumeric.FormatDuration((float)Math.Ceiling(seriesWait))}";
                    default: return "SERIES: IDLE";
                }
            }
        }

        //Orange once a burn started now can't stop in time. The nav console's ETA, which isn't always showing, assumes
        //a steady speed, and a steady burn covers the same range in twice the time since it averages half the speed.
        private string DecelEtaLine
        {
            get
            {
                double time = target.BrakingTime;
                if (time < 0.0) return "DECEL ETA: --";

                string label = target.NavEta >= 0.0 && time > target.NavEta * 2.0 ? $"{MathUtils.ColorToColorTag(GUIOrbitDraw.clrOrange01)}DECEL ETA</color>" : "DECEL ETA";
                return $"{label}: {FzNumeric.FormatDuration((float)Math.Ceiling(time))}";
            }
        }

        //Time left is all recharging, the same time the plot's coasting is worked out from
        private string SeriesEtaLine
        {
            get
            {
                double recharge = Math.Max(PlotEnergy - available, 0.0);
                if (recharge > 0.0 && fillRate <= 0.0) return "SERIES ETA: --";
                return $"SERIES ETA: {FzNumeric.FormatDuration((float)Math.Ceiling(recharge > 0.0 ? recharge / fillRate : 0.0))}";
            }
        }

        private string FormatTime()
        {
            return FzNumeric.FormatDuration((float)Math.Ceiling(chargeTime));
        }

        private static string FormatPercent(double part, double whole)
        {
            return $"{(whole > 0.0 ? part / whole * 100.0 : 0.0):0.#}%";
        }

        private static string FormatStatus(FieldContour.EmitterStatus status)
        {
            switch (status)
            {
                case FieldContour.EmitterStatus.Normal: return $"<color={NormalColor}>NORMAL</color>";
                case FieldContour.EmitterStatus.Warn: return $"<color={WarnColor}>WARN</color>";
                default: return $"<color={DangerColor}>DANGER</color>";
            }
        }
    }
}
