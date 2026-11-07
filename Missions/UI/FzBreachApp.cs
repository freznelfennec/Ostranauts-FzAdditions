using Freznel.FzAdditions.Patches;
using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI
{
    //BREACH//OS, the intrusion app the player's PDA runs against a data vault or an equipment lockout controller
    //(VaultHack, SiteLockout). Per the user, it opens in the game's own PDA like one of its apps, rather than as a
    //machine panel: it's a screen of its own inside GUIPDA, beside Bounties and Gig Nexus and placed as they are, so
    //the PDA's real frame, top bar and nav bar surround it, and the PDA slides in the way it does for its own apps.
    //
    //Opening: the target's "Access" interaction raises its prop map (FzDataVault or FzLockout); BreachAppPatch catches
    //that in CrewSim.RaiseUI and calls Open, which links the app to the target, shows its screen and sets the PDA to
    //BreachState, a state the game doesn't know, so it hides its own apps and slides in with ours showing. Any other
    //state (the PDA's home and close keys, its hotkey, another app) hides ours again.
    //
    //The app follows the game apps' pattern, laid out from 'fz dumpui pda', in its own blue: a title bar, a link strip
    //naming the target and the device (the PDA, or a breach spike and its jobs left), TRACE, a list with column
    //headers whose rows are clicked to pick, a SESSION box outlined in the accent, and rounded buttons. The PDA's
    //sprites and font come off the live PDA (PdaLook).
    //
    //As the game's lock panel does, the hack runs while the app shows, in game time (it stops while paused), and only
    //while its operator, the selected crew, stays at the target. A finished target stops the hack.
    public class FzBreachApp : MonoBehaviour
    {
        public const GUIPDA.UIState BreachState = (GUIPDA.UIState)90;

        private const int ListLines = 9;

        private static readonly Color AppBg = Hex("#0B1220FF");
        private static readonly Color Accent = Hex("#4F8FE8FF");
        private static readonly Color AccentDark = Hex("#0A1428FF");
        private static readonly Color Text = Hex("#D6E4F5FF");
        private static readonly Color Dim = Hex("#6A7F99FF");
        private static readonly Color Track = Hex("#1A2740FF");
        private static readonly Color IdleKey = Hex("#1E2D47FF");
        private static readonly Color Warn = Hex("#FF6B5AFF");
        private const string AccentHex = "#4F8FE8", DimHex = "#6A7F99", GoodHex = "#7EE08A", WarnHex = "#FF6B5A";

        private static Color Hex(string h) => SpriteUtil.ColorFromHex(h);

        private static FzBreachApp instance;

        //Opens the app on a target in the player's PDA. False when there's no PDA to open it in.
        public static bool Open(CondOwner target)
        {
            if (target == null || GUIPDA.instance == null) return false;
            if (instance == null) instance = Build(GUIPDA.instance);
            instance.Link(target);
            CanvasManager.ShowCanvasGroup(instance.group);
            GUIPDA.instance.State = BreachState;
            return true;
        }

        //The PDA has left the app: closed, gone home, or opened another
        public static void Hide()
        {
            if (instance == null || instance.group == null) return;
            instance.hacking = false;
            instance.target = null;
            CanvasManager.HideCanvasGroup(instance.group);
        }

        public static bool IsShowing => instance != null && instance.target != null && instance.group != null && instance.group.interactable;

        //Runs on EmptyScene: the PDA it lived in goes with the scene
        public static void Forget()
        {
            instance = null;
        }

        //The app's screen, built once inside the PDA where the game's own apps sit (pnlBounties' rect, as ratios of
        //the PDA), and drawn in front of them, behind the PDA's nav bar and frame
        private static FzBreachApp Build(GUIPDA pda)
        {
            GameObject app = new GameObject("pnlFzBreachOS", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup), typeof(FzUIElement));
            app.layer = pda.gameObject.layer;
            app.GetComponent<RectTransform>().SetParent(pda.transform, false);
            app.GetComponent<FzUIElement>().SetTransform(0.7752f, 0.5693f, 0.1124f, 0.2125f);
            app.GetComponent<Image>().color = Color.clear;
            Transform nav = pda.transform.Find("pnlNavigation");
            if (nav != null) app.transform.SetSiblingIndex(nav.GetSiblingIndex());

            Box(app, "AppBg", 1f, 0.94f, 0f, 0.06f, AppBg);
            GameObject title = Box(app, "TitleBar", 1f, 0.053f, 0f, 0.947f, Accent).gameObject;
            Words(title, "AppTitle", "BREACH//OS", 0.6f, 0.9f, 0.2f, 0.05f, AccentDark, TextAlignmentOptions.Center, 10f, 30f);

            Words(app, "LinkText", "LINK >  DATA VAULT", 0.66f, 0.035f, 0.02f, 0.907f, Text, TextAlignmentOptions.Left, 6f, 14f);
            Words(app, "DeviceText", "PDA", 0.3f, 0.035f, 0.68f, 0.907f, Dim, TextAlignmentOptions.Right, 6f, 14f);

            Words(app, "TraceLabel", "TRACE", 0.1f, 0.03f, 0.02f, 0.867f, Dim, TextAlignmentOptions.Left, 6f, 14f);
            GameObject track = Box(app, "TraceTrack", 0.72f, 0.016f, 0.126f, 0.874f, Track).gameObject;
            Box(track, "TraceFill", 0f, 1f, 0f, 0f, Warn);
            Words(app, "TraceText", "0%", 0.13f, 0.03f, 0.85f, 0.867f, Warn, TextAlignmentOptions.Right, 6f, 14f);

            //Column headers, the PDA's "LABEL |" style, and a rule under them
            Words(app, "HeadEntry", "<b>ENTRY</b>  |", 0.12f, 0.025f, 0.025f, 0.838f, Accent, TextAlignmentOptions.Left, 6f, 11f);
            Words(app, "HeadCategory", "<b>CATEGORY</b>  |", 0.4f, 0.025f, 0.14f, 0.838f, Accent, TextAlignmentOptions.Left, 6f, 11f);
            Words(app, "HeadStatus", "<b>STATUS</b>  |", 0.35f, 0.025f, 0.62f, 0.838f, Accent, TextAlignmentOptions.Left, 6f, 11f);
            Box(app, "HeadRule", 0.972f, 0.0025f, 0.014f, 0.831f, new Color(Accent.r, Accent.g, Accent.b, 0.5f));

            //The list: a row each, clicked to pick it
            float rowH = 0.355f / ListLines;
            for (int i = 0; i < ListLines; i++)
            {
                float y = 0.826f - rowH * (i + 1);
                Image row = Box(app, "Row" + i, 0.972f, rowH * 0.94f, 0.014f, y, Color.clear);
                row.raycastTarget = true;
                row.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
                Box(row.gameObject, "Marker", 0.006f, 1f, 0f, 0f, Color.clear);
                Words(row.gameObject, "Num", "", 0.1f, 0.8f, 0.012f, 0.1f, Dim, TextAlignmentOptions.Left, 7f, 15f);
                Words(row.gameObject, "Name", "", 0.47f, 0.8f, 0.13f, 0.1f, Text, TextAlignmentOptions.Left, 7f, 15f);
                Words(row.gameObject, "Status", "", 0.38f, 0.8f, 0.62f, 0.1f, Dim, TextAlignmentOptions.Left, 7f, 15f);
                Box(row.gameObject, "Rule", 1f, 0.03f, 0f, -0.03f, new Color(Accent.r, Accent.g, Accent.b, 0.2f));
            }

            //SESSION: the PDA's bordered detail box
            Image sessionBox = Box(app, "SessionBox", 0.98f, 0.30f, 0.01f, 0.16f, Accent);
            if (!PdaLook.Apply(sessionBox, PdaLook.Border4)) sessionBox.color = new Color(Accent.r, Accent.g, Accent.b, 0.12f);
            GameObject session = sessionBox.gameObject;
            Words(session, "SessionLabel", "SESSION", 0.4f, 0.07f, 0.03f, 0.9f, Accent, TextAlignmentOptions.Left, 6f, 11f);
            Words(session, "Target", "", 0.94f, 0.1f, 0.03f, 0.78f, Text, TextAlignmentOptions.Left, 7f, 16f);
            GameObject sTrack = Box(session, "ProgressTrack", 0.8f, 0.06f, 0.03f, 0.68f, Track).gameObject;
            Box(sTrack, "ProgressFill", 0f, 1f, 0f, 0f, Accent);
            Words(session, "ProgressText", "0%", 0.13f, 0.1f, 0.84f, 0.66f, Accent, TextAlignmentOptions.Left, 7f, 16f);
            string[] labels = { "STATUS", "USER", "ACCESS", "MODE" };
            for (int i = 0; i < labels.Length; i++)
            {
                float y = 0.52f - i * 0.11f;
                Words(session, "Label" + i, labels[i], 0.2f, 0.08f, 0.03f, y, Dim, TextAlignmentOptions.Left, 6f, 11f);
                Words(session, "Value" + i, "", 0.74f, 0.1f, 0.24f, y - 0.01f, Text, TextAlignmentOptions.Left, 7f, 14f);
            }
            Words(session, "Result", "", 0.94f, 0.1f, 0.03f, 0.04f, Text, TextAlignmentOptions.Left, 7f, 14f);

            //Rounded buttons, the Ferry app's
            FzAppButton.Create(app, "QuietBtn", "QUIET", 0.28f, 0.07f, 0.04f, 0.075f, IdleKey, Accent, Text, true);
            FzAppButton.Create(app, "ForceBtn", "FORCE", 0.28f, 0.07f, 0.36f, 0.075f, IdleKey, Warn, Text, true);
            FzAppButton.Create(app, "RunBtn", "RUN", 0.28f, 0.07f, 0.68f, 0.075f, IdleKey, Accent, Text, true);
            Sprite rounded = PdaLook.Sprite(PdaLook.RoundedX32);
            if (rounded != null) foreach (FzAppButton button in app.GetComponentsInChildren<FzAppButton>(true)) button.SetSprite(rounded);

            TMP_FontAsset font = PdaLook.Font(PdaLook.KodeMono);
            if (font != null) foreach (TMP_Text text in app.GetComponentsInChildren<TMP_Text>(true)) text.font = font;

            FzBreachApp component = app.AddComponent<FzBreachApp>();
            component.group = app.GetComponent<CanvasGroup>();
            component.Wire();
            CanvasManager.HideCanvasGroup(component.group);
            return component;
        }

        private static Image Box(GameObject parent, string name, float w, float h, float x, float y, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FzUIElement));
            obj.layer = parent.layer;
            obj.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            obj.GetComponent<FzUIElement>().SetTransform(w, h, x, y);
            Image image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Words(GameObject parent, string name, string text, float w, float h, float x, float y, Color color, TextAlignmentOptions align, float min, float max)
        {
            TMP_Text t = Label.Create(parent, name, text, w, h, x, y, min, max).GetComponent<TMP_Text>();
            t.color = color;
            t.alignment = align;
            t.richText = true;
            t.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            return t;
        }

        private CanvasGroup group;
        private CondOwner target;
        private TMP_Text linkText, deviceText, traceText, headEntry, headCategory;
        private TMP_Text targetText, progressText, resultText;
        private TMP_Text[] values;
        private FzUIElement traceFill, progressFill;
        private FzAppButton quietBtn, forceBtn, runBtn;
        private struct Row
        {
            public Image Back, Marker;
            public TMP_Text Num, Name, Status;
        }
        private Row[] rows;

        private int selected;   //Row of the list: entries, then (for a vault) the core storage
        private int first;      //The row shown at the top
        private VaultHack.Method method = VaultHack.Method.Quiet;
        private bool hacking;
        private double lastGameSec;
        private string result = "";
        private bool controller; //An equipment lockout controller, not a data vault
        private Dictionary<string, int> kindCounts = new Dictionary<string, int>();

        private T Find<T>(string name) where T : Component => UnityUtil.FindDescendant<T>(transform, name);

        private void Wire()
        {
            linkText = Find<TMP_Text>("LinkText");
            deviceText = Find<TMP_Text>("DeviceText");
            traceText = Find<TMP_Text>("TraceText");
            headEntry = Find<TMP_Text>("HeadEntry");
            headCategory = Find<TMP_Text>("HeadCategory");
            targetText = Find<TMP_Text>("Target");
            progressText = Find<TMP_Text>("ProgressText");
            resultText = Find<TMP_Text>("Result");
            values = Enumerable.Range(0, 4).Select(i => Find<TMP_Text>("Value" + i)).ToArray();
            traceFill = Find<FzUIElement>("TraceFill");
            progressFill = Find<FzUIElement>("ProgressFill");
            quietBtn = Find<FzAppButton>("QuietBtn");
            forceBtn = Find<FzAppButton>("ForceBtn");
            runBtn = Find<FzAppButton>("RunBtn");

            rows = new Row[ListLines];
            for (int i = 0; i < ListLines; i++)
            {
                Transform row = UnityUtil.FindDescendant(transform, "Row" + i);
                rows[i] = new Row
                {
                    Back = row.GetComponent<Image>(),
                    Marker = UnityUtil.FindDescendant<Image>(row, "Marker"),
                    Num = UnityUtil.FindDescendant<TMP_Text>(row, "Num"),
                    Name = UnityUtil.FindDescendant<TMP_Text>(row, "Name"),
                    Status = UnityUtil.FindDescendant<TMP_Text>(row, "Status")
                };
                int line = i;
                row.GetComponent<Button>().onClick.AddListener(() => Pick(first + line));
            }

            quietBtn.OnChanged += on => SetMethod(on ? VaultHack.Method.Quiet : VaultHack.Method.Force);
            forceBtn.OnChanged += on => SetMethod(on ? VaultHack.Method.Force : VaultHack.Method.Quiet);
            runBtn.OnChanged += SetHacking;
        }

        //Points the app at a target, picking up any hack left on it
        private void Link(CondOwner co)
        {
            target = co;
            hacking = false;
            result = "";
            selected = 0;
            first = 0;
            method = VaultHack.Method.Quiet;
            controller = SiteLockout.IsController(co);
            kindCounts = controller ? SiteLockout.Counts(co.ship) : new Dictionary<string, int>();
            headEntry.text = controller ? "<b>#</b>  |" : "<b>ENTRY</b>  |";
            headCategory.text = controller ? "<b>EQUIPMENT</b>  |" : "<b>CATEGORY</b>  |";

            var props = VaultHack.Props(co);
            if (props != null && props.TryGetValue("strMethod", out string m) && Enum.TryParse(m, out VaultHack.Method saved)) method = saved;
            if (props != null && props.TryGetValue("strTarget", out string t) && int.TryParse(t, out int last)) selected = RowOf(last);

            quietBtn.SetOnWithoutNotify(method == VaultHack.Method.Quiet);
            forceBtn.SetOnWithoutNotify(method == VaultHack.Method.Force);
            runBtn.SetOnWithoutNotify(false);

            string corp = VaultHack.Corp(co), name = corp;
            try
            {
                name = SiteDataLoader.Get().Corp(corp)?.strNameFriendly ?? corp;
            }
            catch (Exception)
            {
            }
            string what = controller ? "LOCKOUT CONTROLLER" : "DATA VAULT";
            linkText.text = "LINK >  " + (string.IsNullOrEmpty(name) ? what : name.ToUpperInvariant() + "  /  " + what);
            Refresh();
        }

        //A vault's entries and then its core storage; a controller's kinds of equipment only
        private int Rows => VaultHack.Entries(target).Count + (controller ? 0 : 1);
        private int TargetOf(int row) => !controller && row >= Rows - 1 ? VaultHack.StorageTarget : row;
        private int RowOf(int t) => t == VaultHack.StorageTarget ? Rows - 1 : Mathf.Clamp(t, 0, Rows - 1);

        private void Pick(int row)
        {
            if (target == null || hacking || row < 0 || row >= Rows) return; //Stay on the target being worked
            selected = row;
            Refresh();
        }

        private void SetMethod(VaultHack.Method m)
        {
            method = m;
            quietBtn.SetOnWithoutNotify(m == VaultHack.Method.Quiet);
            forceBtn.SetOnWithoutNotify(m == VaultHack.Method.Force);
            if (hacking && target != null) VaultHack.Begin(target, CrewSim.GetSelectedCrew(), TargetOf(selected), method);
            Refresh();
        }

        private void SetHacking(bool on)
        {
            if (target == null) return;
            if (!on)
            {
                hacking = false;
                Refresh();
                return;
            }
            CondOwner op = CrewSim.GetSelectedCrew();
            string error = !VaultHack.InReach(target, op) ? "STAND AT THE TARGET TO LINK" : VaultHack.Begin(target, op, TargetOf(selected), method);
            if (error != null)
            {
                result = error;
                runBtn.SetOnWithoutNotify(false);
                hacking = false;
                Refresh();
                return;
            }
            hacking = true;
            result = "";
            lastGameSec = CrewSim.fTotalGameSec;
            Refresh();
        }

        private void Update()
        {
            if (target == null || group == null || !group.interactable) return;
            if (target.bDestroyed)
            {
                Hide();
                return;
            }
            if (hacking)
            {
                double now = CrewSim.fTotalGameSec;
                double dt = Math.Max(0.0, now - lastGameSec);
                lastGameSec = now;
                CondOwner op = CrewSim.GetSelectedCrew();
                if (!VaultHack.InReach(target, op))
                {
                    result = "LINK LOST: OPERATOR WALKED AWAY";
                    hacking = false;
                    runBtn.SetOnWithoutNotify(false);
                }
                else if (dt > 0.0)
                {
                    string done = VaultHack.Work(target, op, TargetOf(selected), method, dt);
                    if (done != null)
                    {
                        result = done;
                        hacking = false;
                        runBtn.SetOnWithoutNotify(false);
                    }
                }
            }
            Refresh();
        }

        private void Refresh()
        {
            if (target == null) return;
            List<string> entries = VaultHack.Entries(target);
            HashSet<int> done = VaultHack.Done(target);
            int count = Rows;
            selected = Mathf.Clamp(selected, 0, count - 1);
            first = Mathf.Clamp(first, 0, Math.Max(0, count - ListLines));
            if (selected < first) first = selected;
            if (selected >= first + ListLines) first = selected - ListLines + 1;

            for (int i = 0; i < ListLines; i++)
            {
                int row = first + i;
                Row r = rows[i];
                bool shown = row < count;
                bool picked = shown && row == selected;
                r.Back.color = picked ? new Color(Accent.r, Accent.g, Accent.b, 0.22f) : Color.clear;
                r.Marker.color = picked ? Accent : Color.clear;
                if (!shown)
                {
                    r.Num.text = r.Name.text = r.Status.text = "";
                    continue;
                }
                r.Num.text = (row + 1).ToString("00");
                if (controller)
                {
                    SiteLockout.Kind kind = SiteLockout.Kinds[row];
                    int units = kindCounts.TryGetValue(kind.Name, out int n) ? n : 0;
                    double p = VaultHack.Progress(target, row) / SiteLockout.KindWork;
                    bool released = SiteLockout.IsReleased(target.ship, kind);
                    r.Name.text = $"{kind.Label}  ({units})";
                    r.Status.text = released ? $"<color={GoodHex}>RELEASED</color>" : "LOCKED" + (p > 0 ? $"  <color={AccentHex}>{p * 100:0}%</color>" : "");
                }
                else if (row < entries.Count)
                {
                    string blueprint = entries[row];
                    double p = VaultHack.Progress(target, row) / VaultHack.EntryWork;
                    bool decrypted = done.Contains(row);
                    r.Name.text = decrypted ? $"<color={GoodHex}>{VaultHack.BlueprintName(blueprint)}</color>" : VaultHack.BlueprintCategory(blueprint);
                    r.Status.text = decrypted ? $"<color={GoodHex}>DECRYPTED</color>" : "ENCRYPTED" + (p > 0 ? $"  <color={AccentHex}>{p * 100:0}%</color>" : "");
                }
                else
                {
                    r.Num.text = "--";
                    bool open = VaultHack.IsStorageOpen(target);
                    int cores = VaultHack.Cores(target);
                    double p = VaultHack.Progress(target, VaultHack.StorageTarget) / VaultHack.StorageWork;
                    r.Name.text = $"CORE STORAGE  ({cores} {(cores == 1 ? "CORE" : "CORES")})";
                    r.Status.text = open ? $"<color={GoodHex}>OPEN</color>" : "SEALED" + (p > 0 ? $"  <color={AccentHex}>{p * 100:0}%</color>" : "");
                }
            }

            //TRACE, and the device running the app
            double trace = SiteTrace.Get(target.ship);
            traceFill.SetTransform((float)Math.Min(1.0, trace / SiteTrace.Full), 1f, 0f, 0f);
            traceText.text = $"{Math.Floor(trace):0}%";
            CondOwner op = CrewSim.GetSelectedCrew();
            CondOwner spike = BreachSpikePatch.Spike(op);
            deviceText.text = spike != null ? $"BREACH SPIKE  {spike.GetCondAmount(BreachSpikePatch.UsesCond):0}/10" : "PDA";

            //SESSION
            int t = TargetOf(selected);
            double work = VaultHack.WorkFor(target, t);
            double progress = Math.Min(1.0, VaultHack.Progress(target, t) / work);
            targetText.text = controller ? SiteLockout.Kinds[Mathf.Clamp(t, 0, SiteLockout.Kinds.Length - 1)].Label + " LOCKS"
                : t == VaultHack.StorageTarget ? "CORE STORAGE"
                : done.Contains(t) ? VaultHack.BlueprintName(entries[t])
                : $"ENTRY {t + 1:00}  ({VaultHack.BlueprintCategory(entries[t])})";
            progressFill.SetTransform((float)progress, 1f, 0f, 0f);
            progressText.text = $"{progress * 100:0}%";
            double speed = VaultHack.Speed(op) * (method == VaultHack.Method.Force ? VaultHack.ForceSpeed : 1.0);
            bool finished = VaultHack.IsFinished(target, t);
            string left = finished ? "DONE" : speed > 0 ? FzNumeric.FormatDuration((float)Math.Ceiling((work - progress * work) / speed)) : "--";
            string state = hacking ? $"<color={AccentHex}>RUNNING</color>" : finished ? $"<color={GoodHex}>COMPLETE</color>" : $"<color={DimHex}>IDLE</color>";
            values[0].text = $"{state}     ETA {left}";
            values[1].text = op?.strNameFriendly ?? "NONE";
            values[2].text = VaultHack.SpeedNote(op);
            values[3].text = method == VaultHack.Method.Force ? $"<color={WarnHex}>FORCE  (FAST, LOUD)</color>" : "QUIET  (SLOW, CAREFUL)";
            bool good = result.StartsWith("LEARNED") || result.StartsWith("COPIED") || result.StartsWith("CORE") || result.EndsWith("RELEASED");
            resultText.text = string.IsNullOrEmpty(result) ? "" : $"<color={(good ? GoodHex : WarnHex)}>{result}</color>";
        }
    }
}
