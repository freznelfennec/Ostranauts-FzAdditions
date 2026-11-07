using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    //Signal Traces: a nav station module on the Jump NavCom's frame, for the traces the ship's sideband interceptors log
    //(SignalTraces). The main page is a paged list of traces with one picked; INVEST sets the picked one as the one
    //being investigated, TARGET puts the console's crosshair on where it leads, and INFO shows all of it, with DISCARD.
    //COSelf here is the nav station, not the module's item.
    public class FzSignalTracesNavMod : NavModBase
    {
        private enum Page { List, Info, Confirm, Error }

        private static readonly string FramePrefab = "GUIShip/GUIOrbitDraw/NavModSensorsMFD";
        private static readonly string FrameSprite = "GUIPanel256x256";
        private static readonly string InsetSprite = "GUIRectEmbossed";
        private static readonly Color OverlayColor = new Color(1f, 1f, 1f, 0.004f); //Edit mode's drag target, nearly invisible
        private static readonly Color BackgroundColor = new Color(0.251f, 0.306f, 0.380f);
        private static readonly Color InsetColor = new Color(0.286f, 0.314f, 0.341f);
        private static readonly Color TitleColor = new Color(0.827f, 0.827f, 0.827f);
        private static readonly Color ScreenTextColor = new Color(0.631f, 0.894f, 0.392f);
        private const string PickColor = "#FFFFFF";
        private const string DimColor = "#5E8A3A";
        private const string WarnColor = "#FFD800";
        private const string GoodColor = "#3CFF3C";
        private static readonly float TitleMinFontSize = 6f;
        private static readonly float TitleMaxFontSize = 10f;
        private static readonly float ScreenMinFontSize = 5f;
        private static readonly float ScreenMaxFontSize = 9f;
        private static readonly float ActionMinFontSize = 5f;
        private static readonly float ActionMaxFontSize = 8f;
        private static readonly float ActionHeight = 0.12f; //Of the screen
        private static readonly float ActionWidth = 0.95f;  //Of each key's slot
        private static readonly int KeyCount = 6;
        private static readonly int RowsPerPage = 5;
        private static readonly float DataInterval = 0.5f; //Real seconds between readings, which scan the ship

        private const string UpKey = "UP";
        private const string DownKey = "DOWN";
        private const string PageKey = "PAGE";
        private const string InfoKey = "INFO";
        private const string TargetKey = "TARGET";
        private const string InvestKey = "INVEST";
        private const string StopKey = "STOP";
        private const string BackKey = "BACK";
        private const string DiscardKey = "DISCARD";
        private const string ConfirmKey = "CONFIRM";
        private const string CancelKey = "CANCEL";
        private const string DismissKey = "DISMISS";

        private static readonly MethodInfo LockTarget = AccessTools.Method(typeof(GUIOrbitDraw), "LockTarget", new[] { typeof(NavPOI) });
        private static GameObject prefabHolder;

        public static GameObject CreatePrefab() => CreatePrefab("FzSignalTraces", false);

        public static GameObject CreateDamagedPrefab() => CreatePrefab("FzSignalTracesDmg", true);

        //As the Jump NavCom's: LoadModules sizes a disabled module through its Awake, so the root has to be active. Under
        //an inactive holder it stays dormant here, and its copies wake up in the nav station.
        private static GameObject CreatePrefab(string name, bool damaged)
        {
            if (prefabHolder == null)
            {
                prefabHolder = new GameObject("FzSignalTracesPrefabs");
                prefabHolder.SetActive(false);
                DontDestroyOnLoad(prefabHolder);
            }

            GameObject root = new GameObject(name, typeof(RectTransform), typeof(FzUIElement), typeof(FzSignalTracesNavMod));
            root.layer = LayerMask.NameToLayer("UI");
            root.GetComponent<RectTransform>().SetParent(prefabHolder.transform, false);
            root.GetComponent<FzUIElement>().SetTransform(1f, 1f, 0f, 0f);

            GameObject container = CreateLayer(root, "Container", 0.25f, 0.4f, 0f, 0f, null, OverlayColor);
            container.AddComponent<CanvasGroup>();
            container.AddComponent<Ostranauts.ShipGUIs.NavStation.Draggable>(); //Named in full, the game has a global Draggable too

            CreateLayer(container, "bg", 1f, 1f, 0f, 0f, null, BackgroundColor);
            CreateLayer(container, "pnlScrews", 1f, 1f, 0f, 0f, SpriteUtil.FindSprite(FramePrefab, FrameSprite), Color.white);
            Label.Create(container, "lblTitle", "Signal Traces", 0.69f, 0.07f, 0.15f, 0.91f, TitleMinFontSize, TitleMaxFontSize).GetComponent<TMP_Text>().color = TitleColor;
            CreateLayer(container, "bgPanel", 0.96f, 0.82f, 0.02f, 0.09f, SpriteUtil.FindSprite(FramePrefab, InsetSprite), InsetColor);

            if (damaged) return root;

            GameObject screen = CreateLayer(container, "Screen", 0.88f, 0.73f, 0.06f, 0.12f, null, Color.black);

            TMP_Text text = Label.Create(screen, "txtScreen", "", 0.92f, 0.84f, 0.04f, 0.14f, ScreenMinFontSize, ScreenMaxFontSize).GetComponent<TMP_Text>();
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
        private bool wired;
        private Page page = Page.List;
        private Page returnPage = Page.List;
        private int picked; //The picked trace's id
        private float nextDataTime;
        private SignalTraces.Readings readings;
        private List<string> messageLines = new List<string>();
        private Action confirmed;

        //Runs again each time the nav station leaves edit mode
        protected override void Init()
        {
            screenText = UnityUtil.FindDescendant<TMP_Text>(transform, "txtScreen");
            if (screenText == null) return;
            actionTexts = Enumerable.Range(0, KeyCount).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, "txtAction" + i)).ToArray();

            if (!wired)
            {
                wired = true;
                UnityUtil.FindDescendant<FzSoftKeyRow>(transform, "KeyRow").OnPressed += PressKey;
            }
            if (picked == 0) picked = SignalTraces.Investigating;
            ShowPage(page == Page.Info ? Page.Info : Page.List);
        }

        private List<SignalTrace> Traces => SignalTraces.All.OrderBy(t => t.Id).ToList();

        private SignalTrace Picked
        {
            get
            {
                List<SignalTrace> traces = Traces;
                SignalTrace trace = traces.FirstOrDefault(t => t.Id == picked) ?? traces.FirstOrDefault();
                picked = trace?.Id ?? 0;
                return trace;
            }
        }

        private string[] GetActions()
        {
            string[] actions = new string[KeyCount];
            if (page == Page.Error)
            {
                actions[0] = DismissKey;
                return actions;
            }
            if (page == Page.Confirm)
            {
                actions[0] = ConfirmKey;
                actions[KeyCount - 1] = CancelKey;
                return actions;
            }

            SignalTrace trace = Picked;
            if (page == Page.List)
            {
                actions[0] = UpKey;
                actions[1] = DownKey;
                if (Traces.Count > RowsPerPage) actions[2] = PageKey;
                if (trace != null) actions[3] = InfoKey;
            }
            else
            {
                actions[0] = BackKey;
                if (trace != null) actions[3] = DiscardKey;
            }
            if (trace != null)
            {
                actions[4] = TargetKey;
                actions[5] = SignalTraces.Investigating == trace.Id ? StopKey : InvestKey;
            }
            return actions;
        }

        private void PressKey(int key)
        {
            if (page == Page.Confirm)
            {
                if (key == 0) confirmed?.Invoke();
                else if (key == KeyCount - 1) ShowPage(returnPage);
                return;
            }

            switch (GetActions()[key])
            {
                case UpKey: Move(-1); break;
                case DownKey: Move(1); break;
                case PageKey: Move(RowsPerPage, wholePage: true); break;
                case InfoKey: ShowPage(Page.Info); break;
                case BackKey: ShowPage(Page.List); break;
                case DismissKey: ShowPage(returnPage); break;
                case TargetKey: Target(); break;
                case InvestKey: SignalTraces.Assign(picked); break;
                case StopKey: SignalTraces.Assign(0); break;
                case DiscardKey: RequestDiscard(); break;
            }
            Render();
        }

        //Up and down wrap round the list; PAGE goes to the top of the next page
        private void Move(int step, bool wholePage = false)
        {
            List<SignalTrace> traces = Traces;
            if (traces.Count == 0) return;
            int index = Math.Max(0, traces.FindIndex(t => t.Id == picked));
            index = wholePage ? (index / RowsPerPage + 1) * RowsPerPage : index + step;
            if (index >= traces.Count) index = wholePage ? 0 : 0;
            if (index < 0) index = traces.Count - 1;
            picked = traces[index].Id;
        }

        private void Target()
        {
            SignalTrace trace = Picked;
            NavPOI poi = SignalTraces.TargetFor(trace);
            if (poi == null)
            {
                ShowError(trace != null && !trace.Revealed ? "DECODE THE SIGNAL FIRST" : "NOWHERE TO TARGET");
                return;
            }
            if (LockTarget == null || _guiOrbitDraw == null)
            {
                ShowError("THE CONSOLE WON'T TAKE A TARGET");
                return;
            }
            LockTarget.Invoke(_guiOrbitDraw, new object[] { poi });
        }

        private void RequestDiscard()
        {
            SignalTrace trace = Picked;
            if (trace == null) return;
            int id = trace.Id;
            messageLines = new List<string> { $"DISCARD TRACE {id}?", "IT CAN'T BE GOT BACK." };
            if (SignalTraces.SiteOf(trace) != null) messageLines.Add("ITS SITE GOES WITH IT.");
            confirmed = () =>
            {
                SignalTraces.Discard(id);
                ShowPage(Page.List);
            };
            ShowMessagePage(Page.Confirm);
        }

        private void ShowError(string message)
        {
            messageLines = new List<string> { message };
            ShowMessagePage(Page.Error);
        }

        private void ShowMessagePage(Page shown)
        {
            if (page != Page.Error && page != Page.Confirm) returnPage = page;
            ShowPage(shown);
        }

        private void ShowPage(Page shown)
        {
            page = shown;
            nextDataTime = 0f;
            Render();
        }

        //Readings are taken on an interval, so the screen can redraw every frame without scanning the ship
        private void Update()
        {
            if (actionTexts == null) return;
            if (Time.realtimeSinceStartup >= nextDataTime)
            {
                nextDataTime = Time.realtimeSinceStartup + DataInterval;
                readings = SignalTraces.Read();
                Render();
            }
        }

        private void Render()
        {
            if (screenText == null) return;
            screenText.text = string.Join("\n", GetLines());
            string[] actions = GetActions();
            for (int i = 0; i < KeyCount; i++)
            {
                string text = actions[i] ?? "";
                if (actionTexts[i].text != text) actionTexts[i].text = text;
            }
        }

        private IEnumerable<string> GetLines()
        {
            switch (page)
            {
                case Page.Info: return GetInfoLines();
                case Page.Error: return new[] { Title("ERROR") }.Concat(messageLines);
                case Page.Confirm: return new[] { Title("CONFIRM") }.Concat(messageLines);
                default: return GetListLines();
            }
        }

        private IEnumerable<string> GetListLines()
        {
            List<SignalTrace> traces = Traces;
            var lines = new List<string>
            {
                $"INTERCEPTORS: {readings.Active}/{readings.Installed} ON   OPEN SPACE: {readings.Open * 100.0:0}%{(readings.Skilled ? "   HACKER" : "")}",
                readings.Active == 0
                    ? Colour(WarnColor, "NOT LISTENING: NO INTERCEPTOR ON AND POWERED")
                    : $"LISTENING: ~{readings.DetectPerHour * 24.0:0.0} SIGNALS A DAY   TRACES: {traces.Count}/{SignalTraces.MaxTraces}"
            };
            if (traces.Count == 0)
            {
                lines.Add("");
                lines.Add(Colour(DimColor, "NO SIGNALS LOGGED"));
                return lines;
            }

            SignalTrace pickedTrace = Picked;
            int index = Math.Max(0, traces.IndexOf(pickedTrace));
            int pageStart = index / RowsPerPage * RowsPerPage;
            foreach (SignalTrace trace in traces.Skip(pageStart).Take(RowsPerPage))
            {
                bool isPicked = trace == pickedTrace;
                string mark = SignalTraces.Investigating == trace.Id ? "*" : " ";
                string row = $"{(isPicked ? ">" : " ")}{mark}{trace.Id,2} {Summary(trace)}";
                lines.Add(isPicked ? Colour(PickColor, row) : row);
            }
            int pages = (traces.Count + RowsPerPage - 1) / RowsPerPage;
            if (pages > 1) lines.Add($"<align=right>PAGE {pageStart / RowsPerPage + 1}/{pages}</align>");
            return lines;
        }

        //One line: what it is, and where it's got to
        private string Summary(SignalTrace trace)
        {
            if (!trace.Revealed) return $"UNKNOWN SIGNAL{JobNote(trace)}";
            return $"{SignalTraces.KindName(trace).ToUpper()}, {SignalTraces.FactionName(trace).ToUpper()}: {Where(trace)}{JobNote(trace)}";
        }

        private static string Where(SignalTrace trace)
        {
            if (trace.Located) return SignalTraces.SiteOf(trace) != null ? "LOCATED" : "LOST, REACQUIRE";
            if (trace.Level <= 0) return $"NEAR {trace.Body.ToUpper()}";
            return $"{trace.Body.ToUpper()} +/-{Km(SignalTraces.RadiusKm(trace, trace.Level))}";
        }

        //The investigated trace's progress, or what holds it up
        private string JobNote(SignalTrace trace)
        {
            if (SignalTraces.Investigating != trace.Id) return "";
            SignalTraces.Job job = SignalTraces.JobFor(trace);
            if (job == SignalTraces.Job.None) return "";
            if (readings.Active == 0) return Colour(WarnColor, " NO POWER");
            if (SignalTraces.NeedsArea(job) && !SignalTraces.InArea(trace, readings.Ship)) return Colour(WarnColor, " OUT OF AREA");
            return Colour(GoodColor, $" {Percent(trace, job)}%");
        }

        private IEnumerable<string> GetInfoLines()
        {
            SignalTrace trace = Picked;
            if (trace == null) return new[] { Title("TRACE"), "NO SIGNALS LOGGED" };

            var lines = new List<string> { Title($"TRACE {trace.Id}") };
            lines.Add($"HEARD: {FzNumeric.FormatDuration((float)Math.Max(0.0, StarSystem.fEpoch - trace.Detected))} AGO");
            if (!trace.Revealed)
            {
                lines.Add("ORIGIN: UNKNOWN");
                lines.Add("SIGNATURE: UNKNOWN");
                lines.Add("LOCATION: UNKNOWN");
            }
            else
            {
                lines.Add($"ORIGIN: {SignalTraces.KindName(trace).ToUpper()}");
                lines.Add($"SIGNATURE: {SignalTraces.FactionName(trace).ToUpper()}");
                lines.Add($"LOCATION: {Where(trace)}   STEP {Math.Min(trace.Level, trace.Steps)}/{trace.Steps}");
                double outside = SignalTraces.DistanceToAreaKm(trace, readings.Ship);
                if (!double.IsInfinity(outside)) lines.Add(outside <= 0.0 ? Colour(GoodColor, "SHIP IS IN THE AREA") : $"AREA: {Km(outside)} AWAY");
            }
            lines.Add(JobLine(trace));
            return lines;
        }

        private string JobLine(SignalTrace trace)
        {
            SignalTraces.Job job = SignalTraces.JobFor(trace);
            if (job == SignalTraces.Job.None) return Colour(GoodColor, "SITE ON THE AIR. GO NOW.");
            string name = job == SignalTraces.Job.Reveal ? "DECODE" : job == SignalTraces.Job.Narrow ? "NARROW" : "REACQUIRE";
            string head = $"NEXT: {name} {Percent(trace, job)}%";
            if (SignalTraces.Investigating != trace.Id) return head + Colour(DimColor, "   NOT ASSIGNED");
            if (readings.Active == 0) return head + Colour(WarnColor, "   NO INTERCEPTOR ON");
            if (SignalTraces.NeedsArea(job) && !SignalTraces.InArea(trace, readings.Ship)) return head + Colour(WarnColor, "   GO TO THE AREA");
            double left = (SignalTraces.JobHours(job) - trace.Progress) / Math.Max(1e-6, readings.Speed) * 3600.0;
            return head + $"   ETA {FzNumeric.FormatDuration((float)Math.Ceiling(left))}";
        }

        private static int Percent(SignalTrace trace, SignalTraces.Job job)
        {
            double hours = SignalTraces.JobHours(job);
            return hours <= 0.0 ? 0 : (int)Math.Min(99.0, Math.Floor(trace.Progress / hours * 100.0));
        }

        private static string Km(double km) => km >= 10000.0 ? $"{km / 1000.0:N0}K KM" : $"{km:N0} KM";

        private static string Colour(string colour, string text) => $"<color={colour}>{text}</color>";

        private static string Title(string name) => $"<align=center>====[ {name} ]====</align>";
    }
}
