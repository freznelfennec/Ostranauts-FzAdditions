using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //A recycler: any CO with IsFzRecycler. It works on a rectangle of floor behind it, its working area, set on its
    //panel and kept in its prop map, so it's saved and carries over between its forms. Its panel is used from the
    //bottom of its sprite, where its use point is, and the area reaches out from the top.
    public static class Recycler
    {
        public const string RecyclerCond = "IsFzRecycler";
        public const string RecyclingCond = "IsFzRecycling"; //Set while it's breaking something down, so it draws its full power
        public const string WorkingTrigger = "TIsFzRecyclerWorking";
        public const string PropMap = WorkArea.RecyclerProps; //The panel's prop map key, where its settings are kept
        public const string AutoProp = "FzAuto";
        public const string RecycleProp = "FzRecycle";
        public const string AutoCoveredProp = "FzAutoCovered"; //Whether AUTO's safety cover is closed
        public const string DumpExcessProp = "FzDumpExcess"; //Material with no room in the ship is destroyed rather than stopping it
        public const string DumpCoveredProp = "FzDumpCovered"; //Whether DUMP EXCESS's safety cover is closed


        public static bool IsRecycler(CondOwner co) => co != null && co.HasCond(RecyclerCond);

        public static bool IsWorking(CondOwner co)
        {
            CondTrigger trigger = DataHandler.GetCondTrigger(WorkingTrigger);
            return IsRecycler(co) && trigger != null && trigger.Triggered(co);
        }

        public static bool IsAuto(CondOwner co) => WorkArea.GetBool(co, AutoProp);
        public static bool IsRecycleOn(CondOwner co) => WorkArea.GetBool(co, RecycleProp);
        public static bool IsAutoCovered(CondOwner co) => WorkArea.GetBool(co, AutoCoveredProp, true);
        public static bool IsDumpExcess(CondOwner co) => WorkArea.GetBool(co, DumpExcessProp);
        public static bool IsDumpCovered(CondOwner co) => WorkArea.GetBool(co, DumpCoveredProp, true);
        public static void SetDumpExcess(CondOwner co, bool on) => WorkArea.SetProp(co, DumpExcessProp, on.ToString().ToLower());
        public static void SetDumpCovered(CondOwner co, bool closed) => WorkArea.SetProp(co, DumpCoveredProp, closed.ToString().ToLower());
        public static void SetAutoCovered(CondOwner co, bool closed) => WorkArea.SetProp(co, AutoCoveredProp, closed.ToString().ToLower());
        public static void SetAuto(CondOwner co, bool on) => WorkArea.SetProp(co, AutoProp, on.ToString().ToLower());
        public static void SetRecycleOn(CondOwner co, bool on) => WorkArea.SetProp(co, RecycleProp, on.ToString().ToLower());

        //The working area and the run, shared with the fabricator in WorkArea
        public static int GetWidth(CondOwner co) => WorkArea.GetWidth(co);
        public static int GetDepth(CondOwner co) => WorkArea.GetDepth(co);
        public static void SetWidth(CondOwner co, int width) => WorkArea.SetWidth(co, width);
        public static void SetDepth(CondOwner co, int depth) => WorkArea.SetDepth(co, depth);
        public static int FirstColumn(int width) => WorkArea.FirstColumn(width);
        public static Rect LocalArea(CondOwner co) => WorkArea.LocalArea(co);
        public static bool[,] OpenTiles(CondOwner co) => WorkArea.OpenTiles(co);
        public static List<CondOwner> ItemsInArea(CondOwner co) => WorkArea.ItemsInArea(co);
        public static bool AnyoneInArea(CondOwner co) => WorkArea.AnyoneInArea(co);
        public static CondOwner CurrentItem(CondOwner co) => WorkArea.CurrentItem(co);
        public static void SetCurrent(CondOwner co, CondOwner item) => WorkArea.SetCurrent(co, item);
        public static bool HasRun(CondOwner co) => WorkArea.HasRun(co);
        public static double Progress(CondOwner co) => WorkArea.Progress(co);
        public static void SetProgress(CondOwner co, double seconds) => WorkArea.SetProgress(co, seconds);
        //Why it can't work now, or null. A person in the area, or nowhere on the ship to put what it recycles, unless
        //it's set to dump what doesn't fit.
        public static string Blocked(CondOwner co)
        {
            if (AnyoneInArea(co)) return "SOMEONE IN AREA";
            if (BeamFormer.Check(co, 0.0) != null) return BeamFormer.MissingReason;
            if (!IsDumpExcess(co) && !Silo.GetWorking(co.ship).Any(s => Silo.Free(s) > 0)) return "NO SILO SPACE";
            return null;
        }

        //An item a recycler can break down: one with a recipe that allows recycling
        public static bool IsRecyclable(CondOwner item)
        {
            JsonFabRecipe recipe = FabData.GetRecipeForCO(item?.strCODef);
            return recipe != null && recipe.bRecycle;
        }

        //What's directly inside an item that's a container
        public static List<CondOwner> Contents(CondOwner item)
        {
            var found = new List<CondOwner>();
            if (item?.objContainer == null) return found;
            foreach (CondOwner inside in item.objContainer.ContainedCOs)
            {
                if (inside != null && !inside.bDestroyed) found.Add(inside);
            }
            return found;
        }

        //A container is recycled all at once with everything in it that can be recycled, at every depth. What can't
        //be recycled is left, with anything inside it. So an item's yield, time and energy are its own plus those of
        //its recyclable contents.
        public static IEnumerable<CondOwner> WithContents(CondOwner item)
        {
            if (!IsRecyclable(item)) yield break;
            yield return item;
            foreach (CondOwner inside in Contents(item))
                foreach (CondOwner part in WithContents(inside)) yield return part;
        }

        //What an item, with its whole stack and its recyclable contents, breaks down into: material to units, after
        //each recipe's yield. Null when it can't be recycled.
        public static Dictionary<string, double> Breakdown(CondOwner item)
        {
            if (!IsRecyclable(item)) return null;
            var total = new Dictionary<string, double>();
            foreach (CondOwner part in WithContents(item))
            {
                JsonFabRecipe recipe = FabData.GetRecipeForCO(part.strCODef);
                int count = Math.Max(1, part.StackCount);
                foreach (var material in recipe.mapMaterials.Where(m => m.Value > 0))
                    total[material.Key] = (total.TryGetValue(material.Key, out double had) ? had : 0) + material.Value * recipe.fRecycleYield * count;
            }
            return total;
        }

        //Material units an item and its recyclable contents yield, which is what wears a beam former. Reserves don't.
        public static double Units(CondOwner item) => Breakdown(item)?.Values.Sum() ?? 0.0;

        //The reserves an item and its recyclable contents hold, in kg, which go to the ship's canisters
        public static Dictionary<string, double> ReservesIn(CondOwner item)
        {
            var total = new Dictionary<string, double>();
            foreach (CondOwner part in WithContents(item))
                foreach (var held in Reserves.InItem(part)) total[held.Key] = (total.TryGetValue(held.Key, out double had) ? had : 0) + held.Value;
            return total;
        }

        //Game seconds to recycle an item with its whole stack and its recyclable contents: each unit of each recipe's
        //full material cost at that material's recycle time, scaled by the recipe. 0 when it can't be recycled.
        public static double RunTime(CondOwner item)
        {
            return WithContents(item).Sum(part =>
            {
                JsonFabRecipe recipe = FabData.GetRecipeForCO(part.strCODef);
                double seconds = recipe.mapMaterials.Sum(m => m.Value * (FabData.Materials.TryGetValue(m.Key, out JsonFabMaterial def) ? def.fRecycleTime : FabSilo.DefaultUnitTime));
                return seconds * Math.Max(1, part.StackCount) * recipe.fTimeScale;
            });
        }

        //kWh to recycle an item with its whole stack and its recyclable contents: each unit of each recipe's full
        //material cost at that material's recycle energy. 0 when it can't be recycled.
        public static double Energy(CondOwner item)
        {
            return WithContents(item).Sum(part =>
            {
                JsonFabRecipe recipe = FabData.GetRecipeForCO(part.strCODef);
                double kwh = recipe.mapMaterials.Sum(m => m.Value * (FabData.Materials.TryGetValue(m.Key, out JsonFabMaterial def) ? def.fRecycleEnergy : FabSilo.DefaultUnitEnergy));
                return kwh * Math.Max(1, part.StackCount);
            });
        }

        //Which of a yield's materials and reserves the ship has no room for. Materials are put into the ship's silos
        //the way Silo.Fill will, each in turn into the silos that take it, fullest first, sharing their free space; a
        //material that doesn't all fit is short. A reserve is short when its canisters can't take it all.
        public static HashSet<string> NoRoom(Ship ship, IDictionary<string, double> materials, IDictionary<string, double> reserves)
        {
            var short_ = new HashSet<string>();
            List<CondOwner> silos = Silo.GetWorking(ship);
            var free = silos.ToDictionary(s => s, Silo.Free);
            foreach (string material in FabData.Materials.Keys)
            {
                if (materials == null || !materials.TryGetValue(material, out double units) || units <= 0) continue;
                double left = units;
                foreach (CondOwner silo in silos.Where(s => Silo.Accepts(s, material)).OrderByDescending(s => Silo.Amount(s, material)))
                {
                    double put = Math.Min(left, free[silo]);
                    free[silo] -= put;
                    left -= put;
                    if (left <= 0) break;
                }
                if (left > 1e-6) short_.Add(material);
            }
            foreach (Reserves.Reserve reserve in Reserves.All)
            {
                if (reserves == null || !reserves.TryGetValue(reserve.Key, out double kg) || kg <= 0) continue;
                if (kg > Reserves.ShipCapacity(ship, reserve) - Reserves.ShipAmount(ship, reserve) + 1e-6) short_.Add(reserve.Key);
            }
            return short_;
        }

        //kW drawn while recycling an item, on top of the recycler's idle draw: its energy spread over its run time.
        //Items made of harder materials draw more.
        public static double ItemPower(CondOwner item)
        {
            double seconds = RunTime(item);
            return seconds > 0.0 ? Energy(item) / seconds * 3600.0 : 0.0;
        }

        //Power drawn now in kW: its idle draw from its power info, plus the current item's draw while it's recycling.
        //Nothing when it's unpowered or off.
        public static double PowerDraw(CondOwner co)
        {
            if (co == null || !co.HasCond("IsPowered") || co.HasCond("IsOff")) return 0.0;
            JsonPowerInfo pi = co.Pwr != null ? co.Pwr.jsonPI : null;
            double idle = pi != null ? pi.fAmount * 3600.0 : 0.0; //StatPower is kWh, drawn each game second
            CondOwner current = co.HasCond(RecyclingCond) ? CurrentItem(co) : null;
            return idle + (current != null ? ItemPower(current) * WorkArea.Speed(co) : 0.0);
        }

        //Material the code shows on narrow readouts: the initials of a two word name, or the first two letters of one word
        public static string Code(string material)
        {
            string name = FabData.Materials.TryGetValue(material, out JsonFabMaterial def) ? def.strNameFriendly : material;
            string[] words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string code = words.Length >= 2 ? $"{words[0][0]}{words[1][0]}" : name.Length >= 2 ? name.Substring(0, 2) : name;
            return code.ToUpperInvariant();
        }
    }
}
