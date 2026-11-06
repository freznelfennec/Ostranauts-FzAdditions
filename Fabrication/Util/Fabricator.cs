using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //A fabricator: any CO with IsFzFabricator. It builds items from the blueprints the player knows into its working
    //area, which it shares with the recycler in WorkArea, drawing their materials from the ship's silos. Its settings
    //and its run are in its panel's prop map, so they're saved and carry over between its forms.
    //
    //A run builds one item. An item that doesn't stack is put in the area when the run starts, unfinished and locked,
    //so its space is taken. One that stacks is built onto a stack of the same thing in the area when there's one with
    //room, which is locked while it's built, and only made when it's done; otherwise it starts a new stack.
    public static class Fabricator
    {
        public const string FabricatorCond = WorkArea.FabricatorCond;
        public const string FabricatingCond = "IsFzFabricating"; //Set while it's building, so it draws its full power
        public const string WorkingTrigger = "TIsFzFabricatorWorking";
        public const string InstalledTrigger = "TIsFzFabricatorInstalled";

        public const string RecipeProp = "FzRecipe"; //The recipe chosen on its panel
        public const string CountProp = "FzCount"; //How many more FABRICATE builds
        public const string FabricateProp = "FzFabricate";
        public const string FillProp = "FzFill"; //Canisters it makes are filled from the ship's reserves
        public const string AutoProp = "FzAuto"; //Fills the area, whatever the count
        public const string AutoCoveredProp = "FzAutoCovered";
        public const string RunRecipeProp = "FzRunRecipe"; //The recipe of the run under way, empty with none
        public const string TargetProp = "FzTarget"; //The stack a stacking run builds onto

        public const int MaxCount = 999;

        public static bool IsFabricator(CondOwner co) => co != null && co.HasCond(FabricatorCond);

        public static bool IsWorking(CondOwner co)
        {
            CondTrigger trigger = DataHandler.GetCondTrigger(WorkingTrigger);
            return IsFabricator(co) && trigger != null && trigger.Triggered(co);
        }

        public static bool IsAuto(CondOwner co) => WorkArea.GetBool(co, AutoProp);
        public static bool IsAutoCovered(CondOwner co) => WorkArea.GetBool(co, AutoCoveredProp, true);
        public static bool IsFabricateOn(CondOwner co) => WorkArea.GetBool(co, FabricateProp);
        public static bool IsFillOn(CondOwner co) => WorkArea.GetBool(co, FillProp);
        public static int GetCount(CondOwner co) => Mathf.Clamp(WorkArea.GetInt(co, CountProp, 1), 0, MaxCount);
        public static void SetAuto(CondOwner co, bool on) => WorkArea.SetProp(co, AutoProp, on.ToString().ToLower());
        public static void SetAutoCovered(CondOwner co, bool closed) => WorkArea.SetProp(co, AutoCoveredProp, closed.ToString().ToLower());
        public static void SetFabricateOn(CondOwner co, bool on) => WorkArea.SetProp(co, FabricateProp, on.ToString().ToLower());
        public static void SetFillOn(CondOwner co, bool on) => WorkArea.SetProp(co, FillProp, on.ToString().ToLower());
        public static void SetCount(CondOwner co, int count) => WorkArea.SetProp(co, CountProp, Mathf.Clamp(count, 0, MaxCount).ToString(CultureInfo.InvariantCulture));

        //Every recipe the player can build: those of the known blueprints, by category then name
        public static List<JsonFabRecipe> KnownRecipes()
        {
            return Blueprints.Known.SelectMany(FabData.GetBlueprintRecipes).Where(r => r.bFabricate).Distinct()
                .OrderBy(r => r.strCategory, StringComparer.Ordinal).ThenBy(r => r.strNameFriendly, StringComparer.Ordinal).ToList();
        }

        //The recipe chosen on its panel, while its blueprint is known
        public static JsonFabRecipe Selected(CondOwner co)
        {
            JsonFabRecipe recipe = FabData.GetRecipe(WorkArea.GetString(co, RecipeProp));
            return recipe != null && recipe.bFabricate && Blueprints.IsKnown(recipe.strBlueprint) ? recipe : null;
        }

        public static void Select(CondOwner co, JsonFabRecipe recipe) => WorkArea.SetProp(co, RecipeProp, recipe?.strName ?? "");

        //The run under way: its recipe, the unfinished item it put in the area, or the stack it's building onto
        public static bool HasRun(CondOwner co) => !string.IsNullOrEmpty(WorkArea.GetString(co, RunRecipeProp));
        public static JsonFabRecipe RunRecipe(CondOwner co) => FabData.GetRecipe(WorkArea.GetString(co, RunRecipeProp));
        public static CondOwner CurrentItem(CondOwner co) => WorkArea.CurrentItem(co);
        public static double Progress(CondOwner co) => WorkArea.Progress(co);
        public static void SetProgress(CondOwner co, double seconds) => WorkArea.SetProgress(co, seconds);

        public static CondOwner Target(CondOwner co)
        {
            string id = WorkArea.GetString(co, TargetProp);
            if (string.IsNullOrEmpty(id)) return null;
            CondOwner target = co.ship?.GetCOByID(id);
            return target != null && !target.bDestroyed ? target : null;
        }

        public static void SetRun(CondOwner co, JsonFabRecipe recipe, CondOwner item, CondOwner target)
        {
            WorkArea.SetCurrent(co, item);
            WorkArea.SetProp(co, TargetProp, target != null ? target.strID : "");
            WorkArea.SetProp(co, RunRecipeProp, recipe != null ? recipe.strName : "");
        }

        //What the run is building on: the unfinished item, or the stack
        public static CondOwner Building(CondOwner co) => CurrentItem(co) ?? Target(co);

        //Game seconds to build one of a recipe: each unit of its materials at that material's fabricate time, scaled
        //by the recipe
        public static double RunTime(JsonFabRecipe recipe)
        {
            if (recipe?.mapMaterials == null) return 0.0;
            double seconds = recipe.mapMaterials.Sum(m => m.Value * (FabData.Materials.TryGetValue(m.Key, out JsonFabMaterial def) ? def.fFabricateTime : FabSilo.DefaultUnitTime));
            return seconds * recipe.fTimeScale;
        }

        //kWh to build one of a recipe: each unit of its materials at that material's fabricate energy
        public static double Energy(JsonFabRecipe recipe)
        {
            if (recipe?.mapMaterials == null) return 0.0;
            return recipe.mapMaterials.Sum(m => m.Value * (FabData.Materials.TryGetValue(m.Key, out JsonFabMaterial def) ? def.fFabricateEnergy : FabSilo.DefaultUnitEnergy));
        }

        //kW drawn while building one, on top of the idle draw
        public static double ItemPower(JsonFabRecipe recipe)
        {
            double seconds = RunTime(recipe);
            return seconds > 0.0 ? Energy(recipe) / seconds * 3600.0 : 0.0;
        }

        public static double PowerDraw(CondOwner co)
        {
            if (co == null || !co.HasCond("IsPowered") || co.HasCond("IsOff")) return 0.0;
            JsonPowerInfo pi = co.Pwr != null ? co.Pwr.jsonPI : null;
            double idle = pi != null ? pi.fAmount * 3600.0 : 0.0;
            JsonFabRecipe running = co.HasCond(FabricatingCond) ? RunRecipe(co) : null;
            return idle + (running != null ? ItemPower(running) * WorkArea.Speed(co) : 0.0);
        }

        //How many of a recipe the ship's silos hold the materials for
        public static int CanBuild(Ship ship, JsonFabRecipe recipe)
        {
            if (recipe?.mapMaterials == null) return 0;
            int most = int.MaxValue;
            foreach (var material in recipe.mapMaterials.Where(m => m.Value > 0))
            {
                most = Math.Min(most, (int)Math.Floor(Silo.ShipAmount(ship, material.Key) / material.Value + 1e-6));
                if (most == 0) return 0;
            }
            return most == int.MaxValue ? 0 : most;
        }

        //Whether the ship's silos hold a share of a recipe's materials
        public static bool HasShare(Ship ship, JsonFabRecipe recipe, double share)
        {
            if (recipe?.mapMaterials == null) return false;
            return recipe.mapMaterials.Where(m => m.Value > 0).All(m => Silo.ShipAmount(ship, m.Key) + 1e-6 >= m.Value * share);
        }

        //An item's size in tiles, unturned, from its item definition
        public static Vector2Int Size(string strCO)
        {
            JsonCondOwner def = DataHandler.GetCondOwnerDef(strCO);
            JsonItemDef item = def != null ? DataHandler.GetItemDef(def.strItemDef) : null;
            if (item == null) return Vector2Int.one;
            int cols = Math.Max(1, item.nCols);
            int rows = Math.Max(1, (item.aSocketAdds?.Length ?? 0) / cols);
            return new Vector2Int(cols, rows);
        }

        public static bool Stacks(string strCO)
        {
            JsonCondOwner def = DataHandler.GetCondOwnerDef(strCO);
            return def != null && def.nStackLimit > 1;
        }

        //A stack of the item in the area, unlocked and with room for one more, the nearest the fabricator
        public static CondOwner FindStack(CondOwner co, string strCO)
        {
            JsonCondOwner def = DataHandler.GetCondOwnerDef(strCO);
            if (def == null || def.nStackLimit <= 1) return null;
            Vector2 origin = co.tf.position;
            return WorkArea.ItemsInArea(co)
                .Where(i => i.strCODef == strCO && !Locks.IsLocked(i) && i.StackCount < Math.Max(i.nStackLimit, def.nStackLimit))
                .OrderBy(i => ((Vector2)i.tf.position - origin).sqrMagnitude)
                .FirstOrDefault();
        }

        //The centre of the nearest place in the area an item of that size fits, on open tiles with nothing on them
        public static bool FindSpot(CondOwner co, string strCO, out Vector2 centre)
        {
            centre = Vector2.zero;
            if (co?.ship == null || co.tf == null) return false;
            Vector2Int size = Size(strCO);
            bool[,] open = WorkArea.OpenTiles(co);
            HashSet<int> taken = WorkArea.Occupied(co);
            Vector2 origin = co.tf.position;
            Vector2 offset = new Vector2((size.x - 1) / 2f, (size.y - 1) / 2f);
            bool found = false;
            float best = float.MaxValue;
            for (int col = 0; col < open.GetLength(0); col++)
            {
                for (int row = 0; row < open.GetLength(1); row++)
                {
                    if (!open[col, row]) continue;
                    //This tile as the footprint's lower left, in world axes, which an unturned item keeps
                    Vector2 corner = WorkArea.TileCentre(co, col, row);
                    Vector2 middle = corner + offset;
                    float distance = (middle - origin).sqrMagnitude;
                    if (distance >= best) continue;
                    bool fits = true;
                    for (int x = 0; x < size.x && fits; x++)
                    {
                        for (int y = 0; y < size.y && fits; y++)
                        {
                            Vector2 tile = corner + new Vector2(x, y);
                            fits = WorkArea.InArea(co, tile, open) && !taken.Contains(WorkArea.TileIndex(co.ship, tile));
                        }
                    }
                    if (!fits) continue;
                    best = distance;
                    centre = middle;
                    found = true;
                }
            }
            return found;
        }

        //Why it can't start or carry on now, or null
        public static string Blocked(CondOwner co)
        {
            if (WorkArea.AnyoneInArea(co)) return "SOMEONE IN AREA";
            JsonFabRecipe recipe = HasRun(co) ? RunRecipe(co) : Selected(co);
            if (recipe == null) return "NO BLUEPRINT";
            double share = HasRun(co) ? 1.0 - Math.Min(1.0, Progress(co) / Math.Max(1e-9, RunTime(recipe))) : 1.0;
            string former = BeamFormer.Check(co, recipe.TotalUnits * share);
            if (former != null) return former;
            if (HasRun(co)) return HasShare(co.ship, recipe, 0.0001) ? null : "NO MATERIALS";
            if (CanBuild(co.ship, recipe) < 1) return "NO MATERIALS";
            if (FindStack(co, recipe.strCOFabricate) == null && !FindSpot(co, recipe.strCOFabricate, out _)) return "AREA FULL";
            return null;
        }
    }

    //Items a machine is working on: one a recycler is breaking down (IsFzRecycleLocked), or one a fabricator is
    //building or building onto (IsFzFabricateLocked). Nothing may touch them.
    public static class Locks
    {
        public static bool IsLocked(CondOwner co) => co != null && (co.HasCond(RecyclerProcess.LockCond) || co.HasCond(FabricatorProcess.LockCond));
    }
}
