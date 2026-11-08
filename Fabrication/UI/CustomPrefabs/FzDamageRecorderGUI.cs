using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    //The combat damage recorder's panel. ASSESSMENT sums up the ship's state: how many recorders hold the log, how much
    //of what was installed still stands, what's been lost, queued for rebuilding and damaged. LOSSES lists the log a
    //page at a time, with keys to queue a build order for the one picked or all of them, take one off the log, and
    //AUTO QUEUE, which queues each loss as it's logged. The map shows where the damage is.
    public class FzDamageRecorderGUI : GUIData
    {
        public const string PrefabName = "FzDamageRecorderGUI";
        private const int AssessmentLines = 8;
        private const int LossLines = 9;
        private const float ScanInterval = 0.5f; //Real seconds between redraws of the screens and the map
        private const string WaitingColour = "#FF5A4A", QueuedColour = "#50C4FF", PickedColour = "#FFFFFF";

        public static GameObject CreatePrefab()
        {
            GameObject root = Controls.Panel.CreateContainer(PrefabName, 0.62f, 0.56f, 0.19f, 0.22f);
            root.AddComponent<Animator>();
            root.AddComponent<FzDamageRecorderGUI>();

            GameObject assessment = Controls.Panel.CreatePanel("AssessmentPanel", 0.4f, 0.42f, 0f, 0.58f, root);
            Controls.Panel.AddTitle(assessment, "DAMAGE ASSESSMENT", 0.14f);
            Lines(assessment, "Assessment", AssessmentLines, 0.8f, 0.04f, 0.78f);
            FzRocker.Create(assessment, "PwrRocker", 0.13f, 0.34f, 0.835f, 0.3f, showIO: true);
            FzSiloGUI.Caption(assessment, "PwrCaption", "POWER", 0.2f, 0.16f, 0.82f, 0.08f);

            GameObject losses = Controls.Panel.CreatePanel("LossPanel", 0.4f, 0.565f, 0f, 0f, root);
            Controls.Panel.AddTitle(losses, "LOSSES", 0.1f);
            Lines(losses, "Loss", LossLines, 0.62f, 0.27f);
            FzHoldBtn.Create(losses, "UpBtn", "UP", 0.14f, 0.1f, 0.03f, 0.14f);
            FzHoldBtn.Create(losses, "DownBtn", "DOWN", 0.14f, 0.1f, 0.18f, 0.14f);
            FzHoldBtn.Create(losses, "QueueBtn", "QUEUE", 0.2f, 0.1f, 0.33f, 0.14f);
            FzHoldBtn.Create(losses, "DismissBtn", "DISMISS", 0.2f, 0.1f, 0.54f, 0.14f);
            FzHoldBtn.Create(losses, "QueueAllBtn", "QUEUE ALL", 0.23f, 0.1f, 0.75f, 0.14f);
            LampBtn.Create(losses, "AutoQueueBtn", "AUTO QUEUE", 0.3f, 0.1f, 0.03f, 0.025f);
            FzSiloGUI.Caption(losses, "LossMessage", "", 0.025f, 0.62f, 0.36f, 0.1f);

            GameObject mapPanel = Controls.Panel.CreatePanel("MapPanel", 0.585f, 1f, 0.415f, 0f, root);
            Controls.Panel.AddTitle(mapPanel, "DAMAGE MAP", 0.06f);
            GameObject mapScreen = FzMaterialScreen.CreateRect(mapPanel, "MapScreen", 0.94f, 0.84f, 0.03f, 0.1f);
            Controls.Panel.CreateScreen(mapScreen);
            FzDamageMap.Create(mapScreen, "Map", 0.96f, 0.96f, 0.02f, 0.02f);
            FzSiloGUI.Caption(mapPanel, "Legend", Legend(), 0.03f, 0.94f, 0.03f, 0.06f);

            return root;
        }

        private static void Lines(GameObject panel, string prefix, int count, float height, float y, float width = 0.94f)
        {
            GameObject screen = FzMaterialScreen.CreateRect(panel, prefix + "Screen", width, height, 0.03f, y);
            Controls.Panel.CreateScreen(screen);
            GameObject lines = FzMaterialScreen.CreateRect(screen, prefix + "Lines", 0.94f, 0.92f, 0.03f, 0.04f);
            float h = 1f / count;
            for (int i = 0; i < count; i++)
            {
                float lineY = 1f - h * (i + 1);
                FzMaterialScreen.MakeText(lines, prefix + "Label" + i, "", 0.98f, h * 0.9f, 0.01f, lineY, TextAlignmentOptions.Left);
                FzMaterialScreen.MakeText(lines, prefix + "Value" + i, "", 0.4f, h * 0.9f, 0.59f, lineY, TextAlignmentOptions.Right);
            }
        }

        private static string Legend()
        {
            string Swatch(Color32 c, string text) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>■</color> {text}";
            return string.Join("   ", Swatch(FzDamageMap.LostColor, "LOST"), Swatch(FzDamageMap.QueuedColor, "QUEUED"), Swatch(FzDamageMap.DamagedColor, "DAMAGED"));
        }

        private TMP_Text[] assessLabels, assessValues, lossLabels, lossValues;
        private TMP_Text message;
        private FzDamageMap map;
        private LampBtn autoQueue;
        private int selected, page;
        private float nextScan;
        private List<DamageRecorder.Record> records;

        public override void Init(CondOwner coSelf, Dictionary<string, string> dict, string strCOKey)
        {
            base.Init(coSelf, dict, strCOKey);
            assessLabels = Find("AssessmentLabel", AssessmentLines);
            assessValues = Find("AssessmentValue", AssessmentLines);
            lossLabels = Find("LossLabel", LossLines);
            lossValues = Find("LossValue", LossLines);
            message = UnityUtil.FindDescendant<TMP_Text>(transform, "LossMessage");
            map = UnityUtil.FindDescendant<FzDamageMap>(transform, "Map");

            UnityUtil.FindDescendant<FzHoldBtn>(transform, "UpBtn").Pressed += () => Move(-1);
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "DownBtn").Pressed += () => Move(1);
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "QueueBtn").Pressed += QueuePicked;
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "DismissBtn").Pressed += DismissPicked;
            UnityUtil.FindDescendant<FzHoldBtn>(transform, "QueueAllBtn").Pressed += QueueAll;
            autoQueue = UnityUtil.FindDescendant<LampBtn>(transform, "AutoQueueBtn");
            autoQueue.SetOnWithoutNotify(COSelf != null && DamageRecorder.IsAutoQueue(COSelf));
            autoQueue.OnChanged += on => { if (COSelf != null) DamageRecorder.SetAutoQueue(COSelf, on); Say(on ? "LOSSES WILL BE QUEUED AS LOGGED" : ""); };

            //The game's power override, as the machines' rockers use: switched off, the recorder drops its log
            FzRocker pwrRocker = UnityUtil.FindDescendant<FzRocker>(transform, "PwrRocker");
            if (COSelf != null) pwrRocker.SetOnWithoutNotify(!COSelf.HasCond("IsOverrideOff"));
            pwrRocker.OnChanged += SetPower;
            Refresh();
        }

        private void SetPower(bool on)
        {
            if (COSelf == null) return;
            COSelf.ZeroCondAmount("IsOverrideOff");
            COSelf.ZeroCondAmount("IsOverrideOn");
            COSelf.AddCondAmount(on ? "IsOverrideOn" : "IsOverrideOff", 1.0);
        }

        private TMP_Text[] Find(string prefix, int count) => Enumerable.Range(0, count).Select(i => UnityUtil.FindDescendant<TMP_Text>(transform, prefix + i)).ToArray();

        private void Update()
        {
            if (COSelf == null || Time.unscaledTime < nextScan) return;
            Refresh();
        }

        private void Refresh()
        {
            nextScan = Time.unscaledTime + ScanInterval;
            Ship ship = COSelf?.ship;
            bool working = DamageRecorder.IsWorking(COSelf);
            records = working ? DamageRecorder.Records(ship) ?? new List<DamageRecorder.Record>() : new List<DamageRecorder.Record>();
            records = records.OrderByDescending(r => r.Time).ToList();
            selected = Mathf.Clamp(selected, 0, Math.Max(0, records.Count - 1));
            ShowAssessment(ship, working);
            ShowLosses(working);
            map?.Draw(ship, records);
        }

        private void ShowAssessment(Ship ship, bool working)
        {
            var lines = new List<(string, string)>();
            if (!working)
            {
                lines.Add(("STATUS", COSelf != null && COSelf.HasCond("IsPowered") ? "OFFLINE" : "NO POWER"));
                lines.Add(("LOG", "NONE KEPT"));
            }
            else
            {
                List<CondOwner> installed = (ship?.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>())
                    .Where(c => c != null && !c.bDestroyed && c.HasCond("IsInstalled") && !(c.strName ?? "").EndsWith("_Placeholder")).ToList();
                int damaged = installed.Count(c => c.HasCond("IsDamaged"));
                int lost = records.Count;
                int whole = installed.Count - damaged;
                int total = installed.Count + lost;
                int structure = records.Count(r => DamageRecorder.IsStructure(r.CO));
                lines.Add(("STATUS", lost > 0 || damaged > 0 ? "DAMAGE LOGGED" : "ALL CLEAR"));
                lines.Add(("RECORDERS ONLINE", DamageRecorder.Working(ship).Count.ToString()));
                lines.Add(("INTEGRITY", total > 0 ? $"{Math.Floor(100.0 * whole / total)}%" : "-"));
                lines.Add(("LOST: STRUCTURE", structure.ToString()));
                lines.Add(("LOST: SYSTEMS", (lost - structure).ToString()));
                lines.Add(("BUILD ORDERS QUEUED", records.Count(r => r.Queued).ToString()));
                lines.Add(("NO BUILD JOB", records.Count(r => DamageRecorder.FindJob(r.CO) == null).ToString()));
                lines.Add(("DAMAGED, REPAIRABLE", damaged.ToString()));
            }
            for (int i = 0; i < AssessmentLines; i++)
            {
                var line = i < lines.Count ? lines[i] : ("", "");
                if (assessLabels[i].text != line.Item1) assessLabels[i].text = line.Item1;
                if (assessValues[i].text != line.Item2) assessValues[i].text = line.Item2;
            }
        }

        private void ShowLosses(bool working)
        {
            page = records.Count > 0 ? selected / LossLines : 0;
            for (int i = 0; i < LossLines; i++)
            {
                int index = page * LossLines + i;
                string label = "", value = "";
                if (!working && i == 0) label = "RECORDER OFFLINE";
                else if (working && records.Count == 0 && i == 0) label = "NO LOSSES LOGGED";
                else if (index < records.Count)
                {
                    DamageRecorder.Record record = records[index];
                    string name = DamageRecorder.FriendlyName(record.CO).ToUpperInvariant();
                    bool noJob = DamageRecorder.FindJob(record.CO) == null;
                    string state = record.Queued ? $"<color={QueuedColour}>QUEUED</color>" : noJob ? $"<color={WaitingColour}>NO JOB</color>" : $"<color={WaitingColour}>LOST</color>";
                    label = index == selected ? $"<color={PickedColour}>> {name}</color>" : "  " + name;
                    value = state;
                }
                if (lossLabels[i].text != label) lossLabels[i].text = label;
                if (lossValues[i].text != value) lossValues[i].text = value;
            }
        }

        private void Move(int by)
        {
            if (records == null || records.Count == 0) return;
            selected = Mathf.Clamp(selected + by, 0, records.Count - 1);
            Refresh();
        }

        private DamageRecorder.Record Picked() => records != null && selected >= 0 && selected < records.Count ? records[selected] : null;

        private void QueuePicked()
        {
            DamageRecorder.Record picked = Picked();
            Ship ship = COSelf?.ship;
            if (picked == null || ship == null || !DamageRecorder.IsWorking(COSelf)) return;
            if (picked.Queued)
            {
                Say("ALREADY QUEUED");
                return;
            }
            Say(DamageRecorder.QueueOne(ship, picked.Id, out string reason) ? "BUILD ORDER QUEUED" : reason ?? "COULDN'T QUEUE");
            Refresh();
        }

        private void QueueAll()
        {
            Ship ship = COSelf?.ship;
            if (ship == null || !DamageRecorder.IsWorking(COSelf)) return;
            DamageRecorder.QueueAll(ship, out int queued, out int failed);
            Say(queued == 0 && failed == 0 ? "NOTHING WAITING" : failed > 0 ? $"QUEUED {queued}, {failed} COULDN'T BE" : $"QUEUED {queued}");
            Refresh();
        }

        private void DismissPicked()
        {
            DamageRecorder.Record picked = Picked();
            Ship ship = COSelf?.ship;
            if (picked == null || ship == null || !DamageRecorder.IsWorking(COSelf)) return;
            DamageRecorder.Dismiss(ship, new[] { picked.Id });
            Say("TAKEN OFF THE LOG");
            Refresh();
        }

        private void Say(string text)
        {
            if (message != null) message.text = text ?? "";
        }
    }
}
