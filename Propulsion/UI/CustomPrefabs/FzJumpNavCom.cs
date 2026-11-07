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
    //otherwise, and the last is JUMP. ERROR and CONFIRM take over the keys until they are answered, with CONFIRM
    //away from JUMP's key so that pressing JUMP twice can't click through it.
    //COSelf here is the nav station, not the module's item.
    public class FzJumpNavCom : NavModBase
    {
        private enum Page { Main, Field, Energy, Plot, Target, Mode, Error, Confirm }

        private static readonly string FramePrefab = "GUIShip/GUIOrbitDraw/NavModSensorsMFD";
        private static readonly string FrameSprite = "GUIPanel256x256";
        private static readonly string InsetSprite = "GUIRectEmbossed";
        private static readonly Color OverlayColor = new Color(1f, 1f, 1f, 0.004f); //Edit mode's drag target, nearly invisible
        private static readonly Color BackgroundColor = new Color(0.251f, 0.306f, 0.380f);
        private static readonly Color InsetColor = new Color(0.286f, 0.314f, 0.341f);
        private static readonly Color TitleColor = new Color(0.827f, 0.827f, 0.827f);
        private static readonly Color ScreenTextColor = new Color(0.631f, 0.894f, 0.392f);
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
        private static readonly float PlotMarkerSize = 6f; //Map canvas units
        private static readonly int PlotBurnPoints = 8; //Segments a correction burn's curve is drawn with
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
        private const string TargetKey = "TARGET";
        private const string BackKey = "BACK";
        private const string CalcKey = "CALC";
        private const string ShowKey = "SHOW";
        private const string ModeKey = "MODE";
        private const string JumpTypeKey = "TYPE";
        private const string ThrustKey = "THRUST";
        private const string ExecuteKey = "EXECUTE";
        private const string CancelKey = "CANCEL";
        private const string DismissKey = "DISMISS";
        private const string ConfirmKey = "CONFIRM";
        private const string MatchKey = "MATCH";
        private const string StopKey = "STOP";
        private const string MatchAllKey = "ALL"; //On the match confirmation, switches to the mode it names
        private const string MatchCrossKey = "CROSS";

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
        private Page returnPage = Page.Main; //Where ERROR and CONFIRM go once they are answered
        private List<string> messageLines = new List<string>();
        private Action confirmed;
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
        private bool thrustOnTorch;
        private SeriesPlan plan; //Null outside series mode or without a target to plan to
        private double correctionLeft; //In AU per second, what a running correction still has to change
        private string calcTarget; //The target a series' setting was last worked out for with CALC, null once it isn't followed
        private double matchLeft = -1.0; //In AU per second, the velocity a running match still has to take out, negative without one
        private string matchName = ""; //The running match's target
        private bool matchCross; //A match takes out only the cross-velocity
        private bool matchConfirm; //The confirmation up is a match's, which can switch between all and cross

        //Kept with the console's settings, so a setting still follows its target after a load
        private void SetFollow(string name)
        {
            if (calcTarget == name) return;
            calcTarget = name;
            _guiOrbitDraw.SetPropMapData(Constants.NavComFollowProp, name ?? "");
        }
        private double fullMargin;
        private double chargeWait;
        private double seriesEnergy = -1.0;
        private double fillRate;
        private double burnPerKWh;
        private double seriesWait;
        private JumpSequence.SeriesState seriesState;
        private FieldContour.EmitterStatus emitterStatus;

        //A series recharges between jumps, so it can go past what the cells hold at once. With no cells on to measure
        //it by it is left alone, a brief loss of power shouldn't pull a long series' setting down.
        private double MaxSetting
        {
            get
            {
                if (!seriesMode) return Math.Max(minSetting, capacity);
                return capacity > 0.0 ? Math.Max(minSetting, capacity * Constants.MaxSeriesJumps) : float.MaxValue;
            }
        }

        private bool IsJumping => JumpSequence.IsActive(COSelf?.ship) || JumpSequence.IsSeriesRunning(COSelf?.ship);

        //Runs again each time the nav station leaves edit mode
        protected override void Init()
        {
            screenText = UnityUtil.FindDescendant<TMP_Text>(transform, "txtScreen");
            if (screenText == null) return;

            fieldMap = UnityUtil.FindDescendant<FzFieldMap>(transform, "FieldMap");
            actionTexts = Enumerable.Range(0, KeyCount).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "txtAction" + i)).ToArray();

            //The console writes its own copy of these back when it closes, so that is the one that lasts
            Dictionary<string, string> props = _guiOrbitDraw.GetPropMap();
            if (props.TryGetValue(Constants.NavComSettingProp, out string value)) float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out setting);
            showPlot = _guiOrbitDraw.GetPropMapData(Constants.NavComShowPlotProp, false);
            seriesMode = _guiOrbitDraw.GetPropMapData(Constants.NavComSeriesProp, false);
            calcTarget = props.TryGetValue(Constants.NavComFollowProp, out string follow) && follow.Length > 0 ? follow : null;
            thrustOnTorch = props.ContainsKey(Constants.NavComThrustProp)
                ? _guiOrbitDraw.GetPropMapData(Constants.NavComThrustProp, false)
                : _guiOrbitDraw.GetPropMapData(Constants.NavComLegacyTorchProp, false);
            matchCross = _guiOrbitDraw.GetPropMapData(Constants.NavComMatchCrossProp, false);

            if (!wired)
            {
                wired = true;
                UnityUtil.FindDescendant<FzSoftKeyRow>(transform, "KeyRow").OnPressed += PressKey;
                UnityUtil.FindDescendant<FzStickySlider>(transform, "SettingSlider").OnHeld += AdjustSetting;
            }

            ShowPage(page);
        }

        private string[] GetActions()
        {
            string[] actions = new string[KeyCount];

            //A running jump can always be stopped, so ERROR leaves its key alone
            if (page == Page.Error)
            {
                actions[0] = DismissKey;
                if (IsJumping) actions[KeyCount - 1] = CancelKey;
                return actions;
            }

            if (page == Page.Confirm)
            {
                actions[0] = ConfirmKey;
                if (matchConfirm) actions[2] = matchCross ? MatchAllKey : MatchCrossKey;
                actions[KeyCount - 1] = CancelKey;
                return actions;
            }

            actions[0] = MainKey;
            actions[KeyCount - 1] = IsJumping ? CancelKey : seriesMode ? ExecuteKey : JumpKey;

            if (page == Page.Main)
            {
                actions[1] = FieldKey;
                actions[2] = EnergyKey;
                actions[3] = PlotKey;
                actions[4] = ModeKey;
            }
            else if (page == Page.Field)
            {
                actions[1] = StrengthKey;
                actions[2] = EmittersKey;
            }
            else if (page == Page.Plot)
            {
                actions[1] = TargetKey;
                actions[4] = ShowKey;
            }
            else if (page == Page.Target)
            {
                actions[0] = BackKey;
                actions[3] = VelocityMatch.IsMatching(COSelf?.ship) ? StopKey : MatchKey;
                actions[4] = CalcKey;
            }
            else if (page == Page.Mode)
            {
                actions[1] = JumpTypeKey;
                actions[2] = ThrustKey;
            }
            return actions;
        }

        private void PressKey(int key)
        {
            if (page == Page.Confirm)
            {
                if (key == 0) confirmed?.Invoke();
                else if (key == KeyCount - 1) ShowPage(returnPage);
                else if (GetActions()[key] != null)
                {
                    matchCross = !matchCross;
                    _guiOrbitDraw.SetPropMapData(Constants.NavComMatchCrossProp, matchCross.ToString().ToLower());
                    RequestMatch();
                }
                return;
            }

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
                case ModeKey: ShowPage(Page.Mode); break;
                case DismissKey: ShowPage(returnPage); break;
                case ShowKey:
                    showPlot = !showPlot;
                    _guiOrbitDraw.SetPropMapData(Constants.NavComShowPlotProp, showPlot.ToString().ToLower());
                    break;
                case JumpTypeKey:
                    if (JumpSequence.IsSeriesRunning(COSelf?.ship)) ShowError("CANCEL THE SERIES TO CHANGE JUMP TYPE");
                    else SetSeriesMode(!seriesMode);
                    break;
                case MatchKey: RequestMatch(); break;
                case StopKey: VelocityMatch.Stop(COSelf?.ship); break;
                case ThrustKey:
                    if (JumpSequence.IsSeriesRunning(COSelf?.ship)) ShowError("CANCEL THE SERIES TO CHANGE THRUST");
                    else if (VelocityMatch.IsMatching(COSelf?.ship)) ShowError("STOP THE MATCH TO CHANGE THRUST");
                    else
                    {
                        thrustOnTorch = !thrustOnTorch;
                        _guiOrbitDraw.SetPropMapData(Constants.NavComThrustProp, thrustOnTorch.ToString().ToLower());
                        ReadData();
                        FollowTarget();
                        RenderScreen();
                    }
                    break;
                case CancelKey: JumpSequence.Cancel(COSelf?.ship); break;
                case JumpKey:
                case ExecuteKey:
                    RequestJump();
                    break;
            }
        }

        //A jump that can't start says why, and one that would cost something asks first
        private void RequestJump()
        {
            ReadData();
            FollowTarget();
            Ship ship = COSelf?.ship;
            double energy = setting;

            JumpSequence.JumpFailure failure = CheckStart(ship, energy);
            if (failure != JumpSequence.JumpFailure.None)
            {
                ShowFailure(failure);
                return;
            }

            List<string> warnings = seriesMode ? JumpSequence.GetSeriesWarnings(ship, energy, GUIOrbitDraw.CrossHairTarget, thrustOnTorch) : JumpSequence.GetJumpWarnings(ship, energy);
            if (warnings.Count == 0) StartJump();
            else ShowConfirm(warnings);
        }

        private JumpSequence.JumpFailure CheckStart(Ship ship, double energy)
        {
            return seriesMode ? JumpSequence.CheckSeries(ship, energy) : JumpSequence.CheckJump(ship, energy);
        }

        //A series with a target takes over the ship's thrust, so a course being flown is dropped
        private void StartJump()
        {
            bool started = seriesMode
                ? JumpSequence.StartSeries(COSelf?.ship, setting, COSelf, GUIOrbitDraw.CrossHairTarget, thrustOnTorch)
                : JumpSequence.Start(COSelf?.ship, setting);
            if (!started) ShowFailure(JumpSequence.JumpFailure.NoContour);
            else if (JumpSequence.IsSteering(COSelf?.ship)) _guiOrbitDraw.SetPropMapData("chkEngage", false.ToString());
        }

        private void ShowFailure(JumpSequence.JumpFailure failure)
        {
            ShowError(GetFailureText(failure));

            //A field that has failed is the next thing to look at
            if (failure == JumpSequence.JumpFailure.FieldStrength) returnPage = Page.Field;
        }

        private static string GetFailureText(JumpSequence.JumpFailure failure)
        {
            switch (failure)
            {
                case JumpSequence.JumpFailure.Thrusting: return "JUMP BLOCKED: SHIP IS THRUSTING";
                case JumpSequence.JumpFailure.HeldInPlace: return "JUMP BLOCKED: HELD BY A STATION OR ORBIT";
                case JumpSequence.JumpFailure.FieldStrength: return "JUMP FAILED: AN EMITTER HAS NO FIELD STRENGTH";
                case JumpSequence.JumpFailure.SettingTooLow: return "JUMP BLOCKED: SETTING IS BELOW THE FIELD'S NEED";
                case JumpSequence.JumpFailure.NotCharged: return "JUMP BLOCKED: NOT ENOUGH CHARGE";
                case JumpSequence.JumpFailure.CellsTooSmall: return "JUMP BLOCKED: SMES CHARGE INSUFFICIENT FOR FIELD ENERGY";
                case JumpSequence.JumpFailure.LandingBlocked: return "SERIES STOPPED: JUMP WOULD LAND INSIDE A BODY";
                case JumpSequence.JumpFailure.NoContour: return "JUMP BLOCKED: NO FIELD CONTOUR";
                case JumpSequence.JumpFailure.NoTarget: return "SERIES BLOCKED: NO TARGET TO PLOT";
                case JumpSequence.JumpFailure.TargetLost: return "SERIES STOPPED: TARGET LOST";
                case JumpSequence.JumpFailure.CorrectionFailed: return "SERIES STOPPED: CORRECTION FAILED, OUT OF RCS PROPELLANT";
                case JumpSequence.JumpFailure.MatchTargetLost: return "MATCH STOPPED: TARGET LOST";
                case JumpSequence.JumpFailure.MatchFailed: return "MATCH STOPPED: OUT OF RCS PROPELLANT";
                case JumpSequence.JumpFailure.Matching: return "JUMP BLOCKED: VELOCITY MATCH RUNNING";
                default: return "JUMP BLOCKED";
            }
        }

        private void ShowError(string message)
        {
            messageLines = new List<string> { message };
            ShowMessagePage(Page.Error);
        }

        private void ShowConfirm(List<string> warnings)
        {
            ShowConfirm(warnings, RequestStart);
        }

        private void ShowConfirm(List<string> lines, Action start)
        {
            messageLines = lines;
            matchConfirm = false;
            confirmed = () =>
            {
                ShowPage(returnPage);
                start();
            };
            ShowMessagePage(Page.Confirm);
        }

        //Says what the match would take before it starts, or why it can't
        private void RequestMatch()
        {
            ReadData();
            if (!GetMatchEstimate(out JumpDrive.AimTarget aim, out JumpCorrection.MatchEstimate estimate)) return;

            string propellant = estimate.Torch
                ? (estimate.ReactantA < 0.0 ? "REACTANT: --" : $"REACTANT: {estimate.ReactantA:0.##}kg D2O, {estimate.ReactantB:0.##}kg He-3")
                : $"PROPELLANT: {estimate.RcsFuel:0.##}kg RCS";
            string kill = matchCross
                ? $"KILL: CROSS-VELOCITY, KEEPS {MathUtils.GetDistUnits(Math.Abs(estimate.Closing))}/s {(estimate.Closing >= 0.0 ? "CLOSING" : "OPENING")}"
                : "KILL: ALL VELOCITY";
            ShowConfirm(new List<string>
            {
                $"MATCH VELOCITY WITH {aim.Name.ToUpper()}",
                kill,
                $"THRUST: {(estimate.Torch ? "TORCH" : "RCS")}",
                $"DELTA-V: {MathUtils.GetDistUnits(estimate.DeltaV)}/s",
                $"BURN TIME: {FzNumeric.FormatDuration((float)Math.Ceiling(estimate.BurnTime))}",
                propellant
            }, StartMatch);
            matchConfirm = true;
            RenderActions();
        }

        //Things can have changed while the confirmation was up
        private void StartMatch()
        {
            ReadData();
            if (!GetMatchEstimate(out _, out _)) return;
            if (!VelocityMatch.Start(COSelf?.ship, COSelf, GUIOrbitDraw.CrossHairTarget, thrustOnTorch, matchCross)) ShowError("MATCH COULD NOT START");
        }

        //False after showing why a match can't be made
        private bool GetMatchEstimate(out JumpDrive.AimTarget aim, out JumpCorrection.MatchEstimate estimate)
        {
            Ship ship = COSelf?.ship;
            estimate = new JumpCorrection.MatchEstimate();
            if (!JumpDrive.ResolvePOI(ship, GUIOrbitDraw.CrossHairTarget, out aim))
            {
                ShowError("NO TARGET TO MATCH");
                return false;
            }
            if (JumpSequence.IsSeriesRunning(ship) || JumpSequence.IsActive(ship))
            {
                ShowError("CANCEL THE JUMP TO MATCH VELOCITY");
                return false;
            }

            estimate = JumpCorrection.EstimateMatch(ship, aim, thrustOnTorch, matchCross);
            if (estimate.Problem == null) return true;

            ShowError(estimate.Problem);
            return false;
        }

        //Things can have changed while the warning was up
        private void RequestStart()
        {
            ReadData();
            FollowTarget();
            JumpSequence.JumpFailure failure = CheckStart(COSelf?.ship, setting);
            if (failure == JumpSequence.JumpFailure.None) StartJump();
            else ShowFailure(failure);
        }

        private void ShowMessagePage(Page shown)
        {
            if (page != Page.Error && page != Page.Confirm) returnPage = page;
            ShowPage(shown);
        }

        private void ShowPage(Page shown)
        {
            page = shown;
            fieldMap.gameObject.SetActive(page == Page.Field);
            RenderActions();

            nextDataTime = 0f;
            nextMapTime = 0f;
        }

        //Unscaled so it can be set while the game is paused, the further from centre the faster it moves
        private void AdjustSetting(float pull)
        {
            if (page == Page.Error || page == Page.Confirm) return;

            float step = Mathf.Sign(pull) * pull * pull * SettingRate * (float)(seriesMode ? Math.Max(setting, capacity) : MaxSetting - minSetting) * Time.unscaledDeltaTime;
            if (step != 0f) SetFollow(null);
            SetSetting(setting + step);
            RenderScreen();
        }

        //Says why rather than settling for a setting that wouldn't reach. A series' setting goes on following the target.
        private void SetSettingToTarget()
        {
            ReadData();
            double energy = TargetEnergy;
            if (!target.CanPlot) ShowError("NO TARGET TO PLOT");
            else if (energy < minSetting) ShowError("TARGET OUT OF REACH");
            else if (energy > MaxSetting) ShowError(seriesMode ? $"TARGET NEEDS MORE THAN {Constants.MaxSeriesJumps} JUMPS" : "TARGET NEEDS MORE THAN THE SMES HOLD");
            else
            {
                SetSetting((float)energy);
                if (seriesMode) SetFollow(target.Name);
                ReadData();
                ShowPage(Page.Plot);
            }
        }

        //Until the slider is moved or the target changes, the ship drifts while the series waits to be started.
        //A setting that has drifted out of reach is left where it was.
        private void FollowTarget()
        {
            if (calcTarget == null || !seriesMode || seriesEnergy >= 0.0) return;
            //Nothing selected leaves it alone, the console picks its own target back up after it opens
            if (!target.CanPlot) return;
            if (target.Name != calcTarget)
            {
                SetFollow(null);
                return;
            }

            double energy = TargetEnergy;
            if (energy < minSetting || energy > MaxSetting || Math.Abs(energy - setting) < 0.05) return;

            SetSetting((float)energy);
            ReadTarget(COSelf.ship);
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

            JumpSequence.JumpFailure failure = JumpSequence.TakeFailure(COSelf?.ship);
            if (failure != JumpSequence.JumpFailure.None) ShowFailure(failure);

            if (JumpSequence.TakeSeriesFinished(COSelf?.ship)) SetSeriesMode(false);

            RenderActions();
        }

        private void SetSeriesMode(bool on)
        {
            seriesMode = on;
            SetFollow(null);
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
            if (showPlot && seriesMode && plan != null && _guiOrbitDraw != null)
            {
                AddPlanPoints();
            }
            else if (showPlot && situ != null && _guiOrbitDraw != null && magnitude > 0.0)
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
                    int markers = Math.Min(series.Jumps, Constants.MaxSeriesJumps);
                    for (int i = 1; i <= markers; i++)
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

        //The plan is laid out against the aim point, so it is drawn from where the aim point is this frame. The line is
        //drawn in pairs of points, each step its own segment and a burn's curve a run of them, a mark at each arrival.
        private void AddPlanPoints()
        {
            if (!GetAimPoint(out double aimX, out double aimY) || plan.Steps.Count == 0) return;

            Vector2 ToCanvas(double x, double y)
            {
                _guiOrbitDraw.SolarToCanvas(aimX + x, aimY + y, out double canvasX, out double canvasY);
                return new Vector2((float)canvasX, (float)canvasY);
            }

            foreach (SeriesPlan.Step step in plan.Steps)
            {
                Vector2 start = ToCanvas(step.StartX, step.StartY);
                if (step.Kind == SeriesPlan.StepKind.Burn)
                {
                    for (int i = 1; i <= PlotBurnPoints; i++)
                    {
                        double t = step.Duration * i / PlotBurnPoints;
                        Vector2 point = ToCanvas(step.StartX + step.VelocityX * t + 0.5 * step.AccelerationX * t * t, step.StartY + step.VelocityY * t + 0.5 * step.AccelerationY * t * t);
                        plotPoints.Add(start);
                        plotPoints.Add(point);
                        start = point;
                    }
                    continue;
                }

                Vector2 end = ToCanvas(step.EndX, step.EndY);
                plotPoints.Add(start);
                plotPoints.Add(end);
                if (step.Kind == SeriesPlan.StepKind.Jump) AddPlotMarker(end);
            }
        }

        //The running series' saved target, otherwise the crosshair's
        private bool GetAimPoint(out double x, out double y)
        {
            Ship ship = COSelf?.ship;
            JumpDrive.AimTarget aim;
            bool found = JumpSequence.IsSteering(ship) ? SeriesTarget.TryResolve(ship, out aim) : JumpDrive.ResolvePOI(ship, GUIOrbitDraw.CrossHairTarget, out aim);
            x = aim.X;
            y = aim.Y;
            return found;
        }

        //In AU, how far the ship coasts while its cells recharge to spend this much of a series
        private double GetCoastDistance(double energy)
        {
            return speed * JumpDrive.GetCoastTime(energy, available, fillRate);
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
                FollowTarget();
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
            List<CondOwner> installed = Smes.GetAllUnits(ship);
            List<CondOwner> cells = installed.Where(Smes.IsOn).ToList();
            List<CondOwner> taps = SmesTap.GetActiveTaps(ship);

            //Each of these would otherwise scan the dock group for itself
            List<CondOwner> installedEmitters = FieldContour.GetInstalledEmitters(ship);
            List<CondOwner> emitters = FieldContour.GetWorkingEmitters(installedEmitters);

            cellCount = installed.Count;
            poweredCells = cells.Count;
            available = Smes.GetTotalCharge(cells);
            capacity = cells.Sum(Smes.GetCapacity);
            chargeRate = cells.Sum(Smes.GetCurrentRate);
            chargeTime = cells.Count > 0 ? cells.Max(Smes.GetChargeTime) : -1.0;
            activeTaps = taps.Count;
            minSetting = FieldContour.GetTotalFieldEnergy(emitters);
            emitterStatus = FieldContour.GetEmitterStatus(installedEmitters);
            averageStrength = JumpDrive.GetAverageStrength(FieldContour.GetEmitters(emitters));
            uncertainty = JumpDrive.GetUncertainty(emitters);
            speed = JumpDrive.GetSpeed(ship);
            fullMargin = Smes.GetFullMargin(cells);
            fillRate = Smes.GetFillRate(ship, cells, taps);
            burnPerKWh = ReactorFuel.IsFusing(ship?.Reactor) ? SmesTap.GetShipBurnRate(taps) : 0.0;
            chargeWait = Smes.GetTimeUntilCharged(cells, setting - fullMargin);
            seriesEnergy = JumpSequence.GetSeriesEnergy(ship);
            seriesState = JumpSequence.GetSeriesState(ship, cells, out seriesWait);
            ReadTarget(ship);
        }

        //A running series goes by the target and thrust it was started with, so the screens show what it is doing.
        //One running without a target has no plan, it goes along its velocity whatever the crosshair is on.
        private void ReadTarget(Ship ship)
        {
            JumpDrive.AimTarget aim;
            bool running = seriesEnergy >= 0.0;
            bool steering = JumpSequence.IsSteering(ship);
            bool torch = steering ? SeriesTarget.CorrectsOnTorch(ship) : thrustOnTorch;
            bool found = steering ? SeriesTarget.TryResolve(ship, out aim) : JumpDrive.ResolvePOI(ship, GUIOrbitDraw.CrossHairTarget, out aim);

            target = found ? JumpDrive.GetTarget(ship, aim, torch, seriesMode) : new JumpDrive.JumpTarget { Name = aim.Name, Type = aim.Type };
            plan = seriesMode && found && (steering || !running) && ship?.objSS != null ? JumpSequence.PlanSeries(ship, PlotEnergy, aim, torch) : null;

            matchLeft = -1.0;
            if (MatchTarget.TryResolve(ship, out JumpDrive.AimTarget match))
            {
                ShipSitu situ = ship.objSS;
                JumpCorrection.GetMatchDeltaV(situ.vPosx - match.X, situ.vPosy - match.Y, situ.vVelX - match.VelocityX, situ.vVelY - match.VelocityY, MatchTarget.IsCross(ship), out double deltaX, out double deltaY);
                matchLeft = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                matchName = match.Name ?? "";
            }

            correctionLeft = 0.0;
            if (found && seriesState == JumpSequence.SeriesState.Correcting)
            {
                ShipSitu situ = ship.objSS;
                JumpCorrection.GetDeltaV(situ.vPosx - aim.X, situ.vPosy - aim.Y, situ.vVelX - aim.VelocityX, situ.vVelY - aim.VelocityY, out double deltaX, out double deltaY);
                correctionLeft = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            }
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

        private double JumpDistance => seriesMode ? plan?.JumpDistance ?? Series.Distance : JumpDrive.GetDistance(speed, JumpTime);

        //If all of the available energy went into one jump, so the setting has no part in it
        private double TotalJumpDistance => JumpDrive.GetDistance(speed, JumpDrive.GetJumpTime(available - minSetting, averageStrength));

        private int JumpsAvailable => setting > 0f ? (int)Math.Floor((available + fullMargin) / setting) : 0;

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
                case Page.Mode: return GetModeLines();
                case Page.Error: return new[] { Title("ERROR") }.Concat(messageLines);
                case Page.Confirm: return new[] { Title("CONFIRM") }.Concat(messageLines);
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
                $"DISTANCE: {MathUtils.GetDistUnits(JumpDistance)}"
            };
            if (seriesEnergy >= 0.0)
            {
                lines.Add(SeriesLine);
                lines.Add(SeriesEtaLine);
            }
            if (matchLeft >= 0.0)
            {
                lines.Add(MatchLine);
                lines.Add($"MATCH TARGET: {matchName.ToUpper()} ({(MatchTarget.IsCross(COSelf?.ship) ? "CROSS-VELOCITY" : "ALL VELOCITY")})");
            }
            if (JumpCorrection.IsLocked(COSelf?.ship)) lines.Add($"<color={WarnColor}>THRUST LOCKED</color>");
            return lines;
        }

        private IEnumerable<string> GetModeLines()
        {
            return new[]
            {
                Title(ModeKey),
                $"JUMP TYPE: {(seriesMode ? "SERIES" : "SINGLE")}",
                $"THRUST: {(thrustOnTorch ? "TORCH" : "RCS")}"
            };
        }

        private IEnumerable<string> GetEnergyLines()
        {
            return new[]
            {
                Title(EnergyKey),
                $"SMES POWERED: {poweredCells}/{cellCount}",
                $"AVAILABLE: {FzNumeric.FormatEnergy((float)available)}",
                $"CHARGE: {FormatPercent(available, capacity)}",
                $"RATE: {FzNumeric.FormatPower((float)chargeRate)}",
                $"FULL IN: {FormatTime()}",
                $"ACTIVE TAPS: {activeTaps}"
            };
        }

        private IEnumerable<string> GetPlotLines()
        {
            if (seriesMode) return GetSeriesPlotLines();

            return new[]
            {
                Title(PlotKey),
                $"SETTING: {FzNumeric.FormatEnergy(setting)}",
                setting > available + fullMargin ? $"CHARGE USED: {FormatPercent(setting, capacity)} ({FormatPercent(available, capacity)} AVAILABLE)" : $"CHARGE USED: {FormatPercent(setting, capacity)}",
                JumpsAvailable > 0 ? $"JUMPS: {JumpsAvailable}" : $"JUMPS: 0 READY IN {FzNumeric.FormatDuration((float)Math.Ceiling(chargeWait))}",
                $"JUMP TIME: {FzNumeric.FormatDuration((float)Math.Ceiling(JumpTime))} +/-{uncertainty:0.#}%",
                $"JUMP DISTANCE: {MathUtils.GetDistUnits(JumpDistance)}",
                $"TOTAL JUMP DISTANCE: {MathUtils.GetDistUnits(TotalJumpDistance)}"
            };
        }

        //The plan while it is idle, what is left of it once it is running. Without a target there is no plan, the
        //series goes along the current velocity with no corrections.
        private IEnumerable<string> GetSeriesPlotLines()
        {
            JumpDrive.JumpSeries series = Series;
            if (plan == null)
            {
                double coast = GetCoastDistance(PlotEnergy);
                return new[]
                {
                    Title(PlotKey + " SERIES"),
                    SeriesLine,
                    $"ENERGY LEFT: {FzNumeric.FormatEnergy((float)PlotEnergy)}",
                    series.Jumps > series.FullJumps ? $"JUMPS: {series.Jumps} ({series.FullJumps} FULL + 1 SHORT)" : $"JUMPS: {series.Jumps}",
                    $"EACH JUMP: {MathUtils.GetDistUnits(series.FullJumpDistance)} +/-{uncertainty:0.#}%",
                    $"JUMP DISTANCE: {MathUtils.GetDistUnits(series.Distance)}",
                    $"COAST: {MathUtils.GetDistUnits(coast)}",
                    "CORRECTION: NO TARGET",
                    $"TOTAL DISTANCE: {MathUtils.GetDistUnits(series.Distance + coast)}",
                    SeriesEtaLine,
                    ReactantLine
                };
            }

            int shortJumps = plan.Jumps - plan.FullJumps;
            return new[]
            {
                Title(PlotKey + " SERIES"),
                SeriesLine,
                $"ENERGY LEFT: {FzNumeric.FormatEnergy((float)PlotEnergy)}",
                shortJumps > 0 ? $"JUMPS: {plan.Jumps} ({plan.FullJumps} FULL + {shortJumps} SHORT)" : $"JUMPS: {plan.Jumps}",
                $"EACH JUMP: {MathUtils.GetDistUnits(series.FullJumpDistance)} +/-{uncertainty:0.#}%",
                $"JUMP DISTANCE: {MathUtils.GetDistUnits(plan.JumpDistance)}",
                $"COAST: {MathUtils.GetDistUnits(plan.CoastDistance)}",
                CorrectionLine,
                $"TOTAL DISTANCE: {MathUtils.GetDistUnits(plan.PathDistance)}",
                SeriesEtaLine,
                ReactantLine,
                $"BRAKE: {(target.BrakesOnTorch ? "TORCH" : "RCS")}, {MathUtils.GetDistUnits(target.RelativeSpeed)}/s, {(target.BrakingTime < 0.0 ? "--" : FzNumeric.FormatDuration((float)Math.Ceiling(target.BrakingTime)))}"
            };
        }

        //What the plan's corrections add up to, or why it stops short at one
        private string CorrectionLine
        {
            get
            {
                if (plan.Stop == SeriesPlan.StopReason.NoThrust) return $"<color={DangerColor}>CORRECTION: NO THRUST</color>";
                if (plan.Stop == SeriesPlan.StopReason.NoFuel) return $"<color={DangerColor}>CORRECTION: NOT ENOUGH RCS</color>";
                if (plan.CorrectionDeltaV <= 0.0) return "CORRECTION: NONE";

                //The same order as BRAKE, the thrust first, with the propellant RCS takes last
                bool rcs = plan.RcsFuel > 0.0;
                string thrust = plan.TorchUsed ? (rcs ? "TORCH + RCS" : "TORCH") : "RCS";
                string fuel = rcs ? $", {plan.RcsFuel:0.##}kg" : "";
                return $"CORRECTION: {thrust}, {MathUtils.GetDistUnits(plan.CorrectionDeltaV)}/s{fuel}";
            }
        }

        //What the recharges still to come would burn, taps being the only part of charging that does
        private string ReactantLine
        {
            get
            {
                double burn = Math.Max(PlotEnergy - available, 0.0) * burnPerKWh;
                if (burn <= 0.0) return "REACTANT: NONE";
                return $"REACTANT: {burn * FusionIC.aReactantAmounts[0]:0.##}kg D2O, {burn * FusionIC.aReactantAmounts[1]:0.##}kg He-3";
            }
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
                $"BRAKING: {MathUtils.GetDistUnits(target.Braking)} ({ThrustName})",
                $"VREL: {MathUtils.GetDistUnits(target.RelativeSpeed)}/s",
                DecelEtaLine,
                $"MIN PLOT VEL: {(minSpeed < 0.0 ? "--" : MathUtils.GetDistUnits(minSpeed) + "/s")}",
                $"MIN PLOT ENERGY: {(energy < 0.0 ? "--" : FzNumeric.FormatEnergy((float)energy))}"
            };
        }

        private static string Title(string name)
        {
            return $"<align=center>====[ {name} ]====</align>";
        }

        //Says why a torch asked for isn't being used, and when a series stops short of a no wake zone to use it
        private string ThrustName
        {
            get
            {
                switch (target.TorchBlock)
                {
                    case JumpDrive.TorchBlock.NoTorch: return "RCS, NO TORCH";
                    case JumpDrive.TorchBlock.NoWakeZone: return "RCS, IN NO WAKE ZONE";
                    default:
                        if (target.OutsideNoWakeZone) return "TORCH, OUTSIDE NO WAKE ZONE";
                        return target.BrakesOnTorch ? "TORCH" : "RCS";
                }
            }
        }

        //What a running match's burn is doing and what it has left to take out
        private string MatchLine
        {
            get
            {
                Ship ship = COSelf?.ship;
                if (JumpCorrection.IsWaiting(ship)) return "MATCH: STALLED, NO THRUST";

                string left = $"{MathUtils.GetDistUnits(matchLeft)}/s LEFT";
                switch (JumpCorrection.GetBurnState(ship))
                {
                    case JumpCorrection.BurnState.Turning: return $"MATCH: TURNING FOR TORCH, {left}";
                    case JumpCorrection.BurnState.Torch: return $"MATCH: BURNING ON TORCH, {left}";
                    case JumpCorrection.BurnState.Rcs: return $"MATCH: BURNING ON RCS, {left}";
                    default: return $"MATCH: STARTING, {left}";
                }
            }
        }

        private string SeriesLine
        {
            get
            {
                switch (seriesState)
                {
                    case JumpSequence.SeriesState.Jumping: return "SERIES: JUMPING";
                    case JumpSequence.SeriesState.Thrusting: return "SERIES: HOLDING FOR THRUST";
                    case JumpSequence.SeriesState.Correcting: return $"SERIES: CORRECTING, {MathUtils.GetDistUnits(correctionLeft)}/s LEFT";
                    case JumpSequence.SeriesState.NoThrust: return "SERIES: STALLED, NO THRUST TO CORRECT";
                    case JumpSequence.SeriesState.Recharging:
                        if (seriesWait >= 0.0) return $"SERIES: NEXT JUMP IN {FzNumeric.FormatDuration((float)Math.Ceiling(seriesWait))}";
                        if (capacity <= minSetting) return "SERIES: STALLED, SMES TOO SMALL";
                        return fillRate > 0.0 ? "SERIES: STALLED" : "SERIES: STALLED, NO CHARGE SOURCE";
                    default: return calcTarget != null ? "SERIES: IDLE, FOLLOWING TARGET" : "SERIES: IDLE";
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

        //Time left is the plan's recharging and corrections, without a plan all recharging
        private string SeriesEtaLine
        {
            get
            {
                if (plan != null)
                {
                    if (plan.Stop != SeriesPlan.StopReason.OutOfEnergy && plan.Stop != SeriesPlan.StopReason.Arrived) return "SERIES ETA: --";
                    return $"SERIES ETA: {FzNumeric.FormatDuration((float)Math.Ceiling(plan.Time))}";
                }
                if (PlotEnergy > available && fillRate <= 0.0) return "SERIES ETA: --";
                return $"SERIES ETA: {FzNumeric.FormatDuration((float)Math.Ceiling(JumpDrive.GetCoastTime(PlotEnergy, available, fillRate)))}";
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
