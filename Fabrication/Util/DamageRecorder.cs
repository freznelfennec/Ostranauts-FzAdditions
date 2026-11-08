using Ostranauts.Inventory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //A combat damage recorder: any CO with IsFzDamageRecorder. While one is working it logs every installed item on
    //its ship that damage destroys outright, where it stood and which way it faced, and can queue a build order to put
    //each one back, the same order the PDA's build menu places.
    //
    //The log is kept on the ship's recorders, not the ship: every working recorder holds a copy in its prop map, so
    //it's saved with them, and a recorder that stops working (switched off, unpowered, damaged, uninstalled or
    //destroyed) drops its copy. While any one keeps working the log survives; once none does, it's gone. A recorder
    //coming back on takes the newest copy from the others, by its revision number.
    public static class DamageRecorder
    {
        public const string RecorderCond = "IsFzDamageRecorder";
        public const string WorkingTrigger = "TIsFzDamageRecorderWorking";
        public const string PropMap = "FzDamageRecorderGUI";
        public const string RecordsProp = "FzRecords";
        public const string RevisionProp = "FzRevision";
        public const string AutoQueueProp = "FzAutoQueue"; //Queues a build order for each loss as it's logged

        private const double AutoQueueInterval = 10.0; //Game seconds between tries to queue what's still waiting
        private const float SamePlace = 0.6f; //Tiles: an installed item this close, of the same kind, is the loss rebuilt

        //A destroyed item: what it was, where on the ship it stood (from the ship's position, so it holds as the ship
        //moves) and which way it faced, when, and the build order queued for it, if any
        public class Record
        {
            public string Id;
            public string CO;
            public float X, Y, Rotation;
            public double Time;
            public string Task = ""; //The placeholder's ID

            public bool Queued => !string.IsNullOrEmpty(Task);
            public Vector2 WorldPos(Ship ship) => ship.vShipPos + new Vector2(X, Y);

            public override string ToString() => string.Join(",", Id, CO, F(X), F(Y), F(Rotation), Time.ToString("R", CultureInfo.InvariantCulture), Task);

            public static Record Parse(string text)
            {
                string[] f = text.Split(',');
                if (f.Length < 7) return null;
                return new Record
                {
                    Id = f[0],
                    CO = f[1],
                    X = P(f[2]),
                    Y = P(f[3]),
                    Rotation = P(f[4]),
                    Time = double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0.0,
                    Task = f[6]
                };
            }

            private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            private static float P(string v) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 0f;
        }

        //When each ship's lead recorder next tries to queue waiting losses, by ship. Only a cache, cleared on a load.
        private static readonly Dictionary<string, double> nextAutoQueue = new Dictionary<string, double>();

        public static void Clear() => nextAutoQueue.Clear();

        public static bool IsRecorder(CondOwner co) => co != null && co.HasCond(RecorderCond);

        public static bool IsWorking(CondOwner co)
        {
            CondTrigger trigger = DataHandler.GetCondTrigger(WorkingTrigger);
            return IsRecorder(co) && !co.bDestroyed && trigger != null && trigger.Triggered(co);
        }

        //The ship's working recorders, by ID so every one agrees which leads
        public static List<CondOwner> Working(Ship ship)
        {
            if (ship == null || ship.bDestroyed) return new List<CondOwner>();
            CondTrigger trigger = DataHandler.GetCondTrigger(WorkingTrigger);
            if (trigger == null) return new List<CondOwner>();
            return (ship.GetCOs(trigger, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>())
                .Where(c => c != null && !c.bDestroyed).OrderBy(c => c.strID, StringComparer.Ordinal).ToList();
        }

        private static Dictionary<string, string> Props(CondOwner co, bool create)
        {
            if (co?.mapGUIPropMaps == null) return null;
            if (co.mapGUIPropMaps.TryGetValue(PropMap, out var props)) return props;
            if (!create) return null;
            props = new Dictionary<string, string>();
            co.mapGUIPropMaps[PropMap] = props;
            return props;
        }

        private static string Get(CondOwner co, string key) => Props(co, false) is var p && p != null && p.TryGetValue(key, out string v) ? v : null;
        private static void Set(CondOwner co, string key, string value) { var p = Props(co, true); if (p != null) p[key] = value; }

        public static bool IsAutoQueue(CondOwner co) => bool.TryParse(Get(co, AutoQueueProp), out bool on) && on;
        public static void SetAutoQueue(CondOwner co, bool on) => Set(co, AutoQueueProp, on.ToString().ToLower());

        private static int Revision(CondOwner co) => int.TryParse(Get(co, RevisionProp), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : 0;

        private static List<Record> Load(CondOwner co)
        {
            string text = Get(co, RecordsProp);
            if (string.IsNullOrEmpty(text)) return new List<Record>();
            return text.Split(';').Select(Record.Parse).Where(r => r != null).ToList();
        }

        private static void Store(CondOwner co, List<Record> records, int revision)
        {
            Set(co, RecordsProp, string.Join(";", records.Select(r => r.ToString())));
            Set(co, RevisionProp, revision.ToString(CultureInfo.InvariantCulture));
        }

        //The ship's log: the newest copy any working recorder holds, or null with no working recorder
        public static List<Record> Records(Ship ship)
        {
            List<CondOwner> working = Working(ship);
            if (working.Count == 0) return null;
            CondOwner newest = working.OrderByDescending(Revision).First();
            return Load(newest);
        }

        //Writes the log to every working recorder on the ship, as one new revision
        private static void Write(Ship ship, List<Record> records)
        {
            List<CondOwner> working = Working(ship);
            if (working.Count == 0) return;
            int revision = working.Max(Revision) + 1;
            foreach (CondOwner recorder in working) Store(recorder, records, revision);
        }

        //A recorder that stops working loses its copy. Run on its power updates, and as it changes form.
        public static void Forget(CondOwner co)
        {
            if (!IsRecorder(co) || string.IsNullOrEmpty(Get(co, RecordsProp))) return;
            Store(co, new List<Record>(), 0);
        }

        //Logs an installed item damage has just destroyed, before it's replaced by its wreckage. Nothing is kept
        //without a working recorder aboard.
        public static void Log(CondOwner destroyed)
        {
            Ship ship = destroyed?.ship;
            if (ship == null || destroyed.strName == null || destroyed.strName.EndsWith("_Placeholder")) return;
            List<Record> records = Records(ship);
            if (records == null) return;
            if (records.Any(r => r.Id == destroyed.strID)) return;

            Vector2 pos = destroyed.tf.position;
            Vector2 local = pos - ship.vShipPos;
            records.Add(new Record
            {
                Id = destroyed.strID,
                CO = destroyed.strName,
                X = local.x,
                Y = local.y,
                Rotation = destroyed.Item != null ? destroyed.Item.fLastRotation : destroyed.tf.rotation.eulerAngles.z,
                Time = StarSystem.fEpoch
            });
            Write(ship, records);
            FzAdditions.Logger.LogInfo($"Damage recorder on {ship.strRegID} logged {destroyed.strName} destroyed at {local}");
        }

        //Each working recorder's power update: one that's behind takes the newest copy. The lead recorder, the first
        //working one, also checks the build orders and queues waiting losses with AUTO QUEUE on.
        public static void Upkeep(CondOwner co)
        {
            if (!IsWorking(co))
            {
                Forget(co);
                return;
            }
            List<CondOwner> working = Working(co.ship);
            if (working.Count == 0) return;
            CondOwner newest = working.OrderByDescending(Revision).First();
            if (newest != co && Revision(newest) > Revision(co)) Store(co, Load(newest), Revision(newest));
            if (working[0] != co) return;

            List<Record> records = Load(co);
            bool changed = Resolve(co.ship, records);
            if (working.Any(IsAutoQueue) && records.Any(r => !r.Queued))
            {
                double now = StarSystem.fEpoch;
                string key = co.ship.strRegID ?? "";
                if (!nextAutoQueue.TryGetValue(key, out double next) || now >= next)
                {
                    nextAutoQueue[key] = now + AutoQueueInterval;
                    foreach (Record record in records.Where(r => !r.Queued)) changed |= Queue(co.ship, record, out _);
                }
            }
            if (changed) Write(co.ship, records);
        }

        //Drops losses that are back, and clears build orders that have gone: rebuilt, or cancelled by the player
        private static bool Resolve(Ship ship, List<Record> records)
        {
            bool changed = false;
            foreach (Record record in records.ToList())
            {
                if (record.Queued)
                {
                    CondOwner task = ship.GetCOByID(record.Task);
                    if (task != null && !task.bDestroyed)
                    {
                        if (task.strName != null && task.strName.EndsWith("_Placeholder")) continue;
                        records.Remove(record); //Installed in its place, keeping its ID
                        changed = true;
                        continue;
                    }
                    record.Task = "";
                    changed = true;
                }
                if (Rebuilt(ship, record))
                {
                    records.Remove(record);
                    changed = true;
                }
            }
            return changed;
        }

        //An installed item of the same kind back where the loss stood, however it got there
        private static bool Rebuilt(Ship ship, Record record)
        {
            JsonInstallable job = FindJob(record.CO);
            string bare = Bare(record.CO);
            Vector2 at = record.WorldPos(ship);
            foreach (CondOwner co in ship.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>())
            {
                if (co == null || co.bDestroyed || !co.HasCond("IsInstalled") || co.strName == null || co.strName.EndsWith("_Placeholder")) continue;
                if (((Vector2)co.tf.position - at).sqrMagnitude > SamePlace * SamePlace) continue;
                if (co.strName == record.CO || (job != null && co.strName == job.strStartInstall) || Bare(co.strName) == bare) return true;
            }
            return false;
        }

        //Queues a build order for a loss. False, with why, when it can't be placed now.
        public static bool Queue(Ship ship, Record record, out string reason)
        {
            reason = null;
            if (record.Queued) return false;
            JsonInstallable job = FindJob(record.CO);
            if (job == null)
            {
                reason = "NO BUILD JOB";
                return false;
            }
            if (GUIInventory.instance != null && GUIInventory.instance.Selected != null)
            {
                reason = "BUSY"; //The game's fit test checks reach from whoever's holding the selected item
                return false;
            }

            CondOwner installed = null, action = null;
            try
            {
                installed = DataHandler.GetCondOwner(job.strStartInstall);
                action = DataHandler.GetCondOwner(job.strActionCO);
                if (installed?.Item == null || action == null)
                {
                    reason = "NO BUILD JOB";
                    return false;
                }
                Vector2 at = record.WorldPos(ship);
                installed.tf.position = new Vector3(at.x, at.y, installed.tf.position.z);
                installed.tf.rotation = Quaternion.Euler(0f, 0f, record.Rotation);
                installed.Item.fLastRotation = record.Rotation;
                installed.Item.ResetTransforms(at.x, at.y);
                if (!installed.Item.CheckFit(installed.Item.rend.bounds.center, ship))
                {
                    reason = "BLOCKED";
                    return false;
                }

                action.strPersistentCO = job.strPersistentCO;
                action.strPersistentCT = job.CTThem;
                CondOwner placeholder = DataHandler.GetCOPlaceholder(installed, action, job.strInteractionName);
                if (placeholder == null)
                {
                    reason = "NO BUILD JOB";
                    return false;
                }
                MeshRenderer renderer = placeholder.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_Color", new Color(1f, 1f, 1f, 0.5f));
                    renderer.SetPropertyBlock(block);
                }
                ship.AddCO(placeholder, bTiles: true);
                CrewSim.objInstance?.workManager?.AddTask(new Task2
                {
                    strDuty = "Construct",
                    strInteraction = job.strInteractionName,
                    strName = DataHandler.GetInteraction(job.strInteractionName)?.strTitle ?? "",
                    strTargetCOID = placeholder.strID
                });
                record.Task = placeholder.strID;
                return true;
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Queuing a rebuild of {record.CO} failed: {ex}");
                reason = "FAILED";
                return false;
            }
            finally
            {
                if (installed != null && !installed.bDestroyed) installed.Destroy();
                if (action != null && !action.bDestroyed) action.Destroy();
            }
        }

        //Queues one loss on the ship's log by its ID, and writes the log to every recorder
        public static bool QueueOne(Ship ship, string id, out string reason)
        {
            reason = null;
            List<Record> records = Records(ship);
            Record record = records?.FirstOrDefault(r => r.Id == id);
            if (record == null) return false;
            if (record.Queued)
            {
                reason = "ALREADY QUEUED";
                return false;
            }
            if (!Queue(ship, record, out reason)) return false;
            Write(ship, records);
            return true;
        }

        //Queues every waiting loss on the ship. Returns how many were queued and how many couldn't be.
        public static void QueueAll(Ship ship, out int queued, out int failed)
        {
            queued = failed = 0;
            List<Record> records = Records(ship);
            if (records == null) return;
            foreach (Record record in records.Where(r => !r.Queued))
            {
                if (Queue(ship, record, out _)) queued++;
                else failed++;
            }
            if (queued > 0) Write(ship, records);
        }

        //Takes losses off the log; a build order already queued for one stays queued
        public static void Dismiss(Ship ship, IEnumerable<string> ids)
        {
            List<Record> records = Records(ship);
            if (records == null) return;
            var drop = new HashSet<string>(ids);
            if (records.RemoveAll(r => drop.Contains(r.Id)) > 0) Write(ship, records);
        }

        //A form's state at the end of its name, as FabData has it: ItmWall1x1DmgLoose is a form of ItmWall1x1
        private static readonly Regex StateWords = new Regex(@"(?:Dmg\d*|Patch|Loose|Off|On)+$");

        private static string Bare(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (DataHandler.dictCOOverlays != null && DataHandler.dictCOOverlays.TryGetValue(name, out JsonCOOverlay overlay) && !string.IsNullOrEmpty(overlay.strCOBase))
                name = overlay.strCOBase;
            return StateWords.Replace(name, "");
        }

        private static readonly Dictionary<string, JsonInstallable> jobCache = new Dictionary<string, JsonInstallable>();

        //The build job that puts back an undamaged form of what was lost: the menu's install job for the item, its Off
        //form, or the form without its state words, never one that installs a damaged form
        public static JsonInstallable FindJob(string strCO)
        {
            if (string.IsNullOrEmpty(strCO)) return null;
            if (jobCache.TryGetValue(strCO, out JsonInstallable cached)) return cached;
            JsonInstallable found = null;
            var options = Installables.dictJobBuildOptions;
            if (options != null)
            {
                string bare = Bare(strCO);
                var names = new List<string> { strCO, bare, bare + "Off", bare + "On" }.Where(n => !n.Contains("Dmg")).Distinct().ToList();
                foreach (string name in names)
                {
                    foreach (var byType in options.Values)
                    {
                        if (byType.TryGetValue(name, out JsonInstallable job) && job != null && !string.IsNullOrEmpty(job.strInteractionName)
                            && !(job.strActionCO ?? "").Contains("Dmg"))
                        {
                            found = job;
                            break;
                        }
                    }
                    if (found != null) break;
                }
            }
            jobCache[strCO] = found;
            return found;
        }

        //An item's friendly name from its definition, without the state words loose items have
        public static string FriendlyName(string strCO)
        {
            JsonCondOwner def = DataHandler.GetCondOwnerDef(strCO);
            string name = def?.strNameFriendly ?? strCO ?? "";
            return Regex.Replace(name, @"\s*\((?:Loose|Off|On|Installed)\)\s*$", "");
        }

        //Structure is walls and floors, which hold the ship together, the rest are systems
        public static bool IsStructure(string strCO)
        {
            string bare = Bare(strCO) ?? "";
            return bare.StartsWith("ItmWall") || bare.StartsWith("ItmFloor") || bare.Contains("Hull");
        }
    }
}
