using Freznel.FzAdditions.Patches;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Hacking a data vault, or a site's equipment lockout controller (SiteLockout), worked from the same panel
    //(FzBreachApp, in the PDA). A controller's targets are the kinds of equipment it holds, each released for good when done.
    //
    //A data vault is worked from the PDA app (FzBreachApp) the way the game's lock panel hacks a door: the work
    //goes on while the panel is open and its operator stands at the vault.
    //
    //A vault holds archive entries (blueprints) and its sealed core storage. The operator picks one target and a method:
    //QUIET is slow and barely raises the site's trace; FORCE is three times faster and raises it a lot (SiteTrace). A
    //finished entry is learned, or, already known, comes out as a data chip; finished core storage opens
    //(IsFzVaultStorageOpen). Each start costs the owner's standing the way hacking a lock does, through the game's
    //HackingCrimePenalty, which SiteIdentity forgives for a player who docked dark.
    //
    //Hacking needs SkillHacking or a breach spike: a spike lets the unskilled work at a quarter of the pace and the
    //skilled at double, and starting a target with one uses one of its jobs.
    //
    //Everything about a hack is in the vault's FzDataVault prop map, so it can be left and taken up again, and survives
    //saves: strBlueprints (the entries), strDone (finished entries), strProgress (work done on each, in standard
    //seconds), strStorage (work done on the storage), strTarget and strMethod.
    public static class VaultHack
    {
        public const string Panel = "FzDataVault";
        public const int StorageTarget = -2; //strTarget for the core storage; -1 is none
        public const string StorageOpenCond = "IsFzVaultStorageOpen";

        public const double EntryWork = 480.0;   //Standard seconds of skilled QUIET work to decrypt an entry
        public const double StorageWork = 300.0; //And to open the core storage
        public const double ForceSpeed = 3.0;
        public const double QuietTracePerSecond = 2.5 / 60.0;  //Trace per standard second of work
        public const double ForceTracePerSecond = 10.0 / 60.0;
        public const float OperatorRange = 2.5f;  //Tiles from the vault's use point the operator has to stay within
        public const string DataChip = "ItmFzDataChip";

        public enum Method { Quiet, Force }

        //Who's working and how fast: 0 when they can't hack at all
        public static double Speed(CondOwner op)
        {
            if (op == null) return 0.0;
            bool skilled = op.HasCond("SkillHacking");
            bool spike = BreachSpikePatch.Spike(op) != null;
            if (skilled) return spike ? BreachSpikePatch.SkillBoost : 1.0;
            return spike ? 1.0 / BreachSpikePatch.SpikeSlowdown : 0.0;
        }

        public static string SpeedNote(CondOwner op)
        {
            if (op == null) return "NO OPERATOR";
            bool skilled = op.HasCond("SkillHacking");
            bool spike = BreachSpikePatch.Spike(op) != null;
            if (skilled && spike) return "SKILLED + SPIKE x2";
            if (skilled) return "SKILLED";
            if (spike) return "SPIKE ONLY x0.25";
            return "NO ACCESS";
        }

        public static Dictionary<string, string> Props(CondOwner vault)
        {
            if (vault?.mapGUIPropMaps == null) return null;
            vault.mapGUIPropMaps.TryGetValue(PanelOf(vault), out Dictionary<string, string> props);
            return props;
        }

        private static string Get(CondOwner vault, string key)
        {
            var props = Props(vault);
            return props != null && props.TryGetValue(key, out string v) ? v : null;
        }

        private static void Set(CondOwner vault, string key, string value)
        {
            if (vault.mapGUIPropMaps == null) vault.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>();
            string panel = PanelOf(vault);
            if (!vault.mapGUIPropMaps.TryGetValue(panel, out var props)) vault.mapGUIPropMaps[panel] = props = new Dictionary<string, string>();
            props[key] = value;
        }

        public static string PanelOf(CondOwner co) => SiteLockout.IsController(co) ? SiteLockout.Panel : Panel;

        public static string Corp(CondOwner vault) => Get(vault, "strCorp");

        //A vault's blueprints, or the kinds of equipment a controller holds
        public static List<string> Entries(CondOwner vault)
        {
            if (SiteLockout.IsController(vault)) return SiteLockout.Kinds.Select(k => k.Name).ToList();
            string list = Get(vault, "strBlueprints");
            return string.IsNullOrEmpty(list) ? new List<string>() : list.Split(',').Where(s => s.Length > 0).ToList();
        }

        public static HashSet<int> Done(CondOwner vault)
        {
            string list = Get(vault, "strDone");
            var done = new HashSet<int>();
            if (string.IsNullOrEmpty(list)) return done;
            foreach (string s in list.Split(',')) if (int.TryParse(s, out int i)) done.Add(i);
            return done;
        }

        private static void SetDone(CondOwner vault, HashSet<int> done) => Set(vault, "strDone", string.Join(",", done.OrderBy(i => i)));

        public static double Progress(CondOwner vault, int target)
        {
            if (target == StorageTarget) return Parse(Get(vault, "strStorage"));
            string[] parts = (Get(vault, "strProgress") ?? "").Split(',');
            return target >= 0 && target < parts.Length ? Parse(parts[target]) : 0.0;
        }

        private static void SetProgress(CondOwner vault, int target, double value)
        {
            string text = value.ToString("0.##", CultureInfo.InvariantCulture);
            if (target == StorageTarget)
            {
                Set(vault, "strStorage", text);
                return;
            }
            int count = Entries(vault).Count;
            var parts = (Get(vault, "strProgress") ?? "").Split(',').ToList();
            while (parts.Count < count) parts.Add("0");
            if (target >= 0 && target < parts.Count) parts[target] = text;
            Set(vault, "strProgress", string.Join(",", parts));
        }

        private static double Parse(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;

        public static double WorkFor(CondOwner co, int target) => SiteLockout.IsController(co) ? SiteLockout.KindWork : target == StorageTarget ? StorageWork : EntryWork;

        public static bool IsStorageOpen(CondOwner vault) => vault != null && vault.HasCond(StorageOpenCond);

        public static bool IsFinished(CondOwner vault, int target)
        {
            if (SiteLockout.IsController(vault)) return target >= 0 && target < SiteLockout.Kinds.Length && SiteLockout.IsReleased(vault.ship, SiteLockout.Kinds[target]);
            return target == StorageTarget ? IsStorageOpen(vault) : Done(vault).Contains(target);
        }

        public static int Cores(CondOwner vault)
        {
            try
            {
                return vault.GetCOs(true, DataHandler.GetCondTrigger("TIsFitContainerFzDataCore"))?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        //Starts or resumes work on a target: the standing cost, and a spike's job if it's started fresh with one
        public static string Begin(CondOwner vault, CondOwner op, int target, Method method)
        {
            if (vault == null || op == null) return "NO OPERATOR";
            if (IsFinished(vault, target)) return "ALREADY DONE";
            if (Speed(op) <= 0.0) return "NEEDS HACKING SKILL OR A BREACH SPIKE";
            Interaction penalty = DataHandler.GetInteraction("HackingCrimePenalty");
            if (penalty != null)
            {
                penalty.objUs = op;
                penalty.objThem = vault;
                penalty.ApplyChain();
            }
            if (Progress(vault, target) <= 0.0)
            {
                CondOwner spike = BreachSpikePatch.Spike(op);
                if (spike != null) BreachSpikePatch.UseOne(spike, op);
            }
            Set(vault, "strTarget", target.ToString(CultureInfo.InvariantCulture));
            Set(vault, "strMethod", method.ToString());
            return null;
        }

        //Whether the operator is close enough to the vault to work it
        public static bool InReach(CondOwner vault, CondOwner op)
        {
            if (vault == null || op == null || op.ship != vault.ship) return false;
            Vector2 use = vault.GetPos("use");
            return Vector2.Distance(use, op.tf.position) <= OperatorRange;
        }

        //Does dt game seconds of work. Returns what finished, or null.
        public static string Work(CondOwner vault, CondOwner op, int target, Method method, double dt)
        {
            if (vault == null || op == null || dt <= 0.0 || IsFinished(vault, target)) return null;
            double speed = Speed(op) * (method == Method.Force ? ForceSpeed : 1.0);
            if (speed <= 0.0) return null;
            double work = dt * speed;
            double progress = Progress(vault, target) + work;
            Ship site = vault.ship;
            //Trace grows with time spent at it, so a slow operator draws more of it per entry
            SiteTrace.Add(site, dt * (method == Method.Force ? ForceTracePerSecond : QuietTracePerSecond), Corp(vault));
            if (progress < WorkFor(vault, target))
            {
                SetProgress(vault, target, progress);
                return null;
            }
            SetProgress(vault, target, WorkFor(vault, target));
            return Finish(vault, op, target);
        }

        private static string Finish(CondOwner vault, CondOwner op, int target)
        {
            if (SiteLockout.IsController(vault))
            {
                if (target < 0 || target >= SiteLockout.Kinds.Length) return null;
                SiteLockout.Kind kind = SiteLockout.Kinds[target];
                SiteLockout.Release(vault.ship, kind);
                op.LogMessage($"The lockout releases the station's {kind.Label.ToLowerInvariant()}. They can be uninstalled now.", "Good", op.strName);
                return kind.Label + " RELEASED";
            }
            if (target == StorageTarget)
            {
                vault.SetCondAmount(StorageOpenCond, 1.0);
                op.LogMessage("The vault's core storage unlocks.", "Good", op.strName);
                return "CORE STORAGE OPEN";
            }
            var done = Done(vault);
            done.Add(target);
            SetDone(vault, done);
            List<string> entries = Entries(vault);
            if (target < 0 || target >= entries.Count) return null;
            string blueprint = entries[target];
            string name = BlueprintName(blueprint);
            if (!Blueprints.IsKnown(blueprint))
            {
                Blueprints.Learn(blueprint);
                op.LogMessage($"Decrypted the blueprint for {name}. It can be fabricated now.", "Good", op.strName);
                return "LEARNED " + name.ToUpperInvariant();
            }
            //Already known: per the user, it comes out as a data chip to sell instead
            CondOwner chip = DataHandler.GetCondOwnerDef(DataChip) != null ? DataHandler.GetCondOwner(DataChip, null, null, false) : null;
            if (chip != null && op.ship != null)
            {
                chip.strNameFriendly = "Data Chip: " + name;
                op.ship.DropCO(chip, op.tf.position);
            }
            op.LogMessage($"Already known: copied {name} to a data chip instead.", "Good", op.strName);
            return "COPIED TO DATA CHIP";
        }

        public static string BlueprintName(string blueprint)
        {
            var recipes = FabData.GetBlueprintRecipes(blueprint);
            string name = recipes != null && recipes.Count > 0 ? recipes[0].strNameFriendly : null;
            return string.IsNullOrEmpty(name) ? blueprint : name;
        }

        public static string BlueprintCategory(string blueprint)
        {
            var recipes = FabData.GetBlueprintRecipes(blueprint);
            string category = recipes != null && recipes.Count > 0 ? recipes[0].strCategory : null;
            if (string.IsNullOrEmpty(category)) return "Unknown";
            int dot = category.IndexOf('.');
            return dot > 0 ? category.Substring(0, dot) : category;
        }
    }
}
