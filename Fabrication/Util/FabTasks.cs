using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //What a fabricator with AUTO on builds: the items the ship's build orders are waiting for. A build order is a
    //placeholder, placed by the PDA's build menu or a damage recorder, which crew fill with a loose item matching its
    //trigger and install. An order already holding its item, or naming one particular item, needs nothing built.
    //
    //Orders are grouped by what they take. A group is missing whatever its orders need beyond the matching items
    //already aboard (loose, in containers or carried) and the ones other fabricators are building for it now, so two
    //fabricators never build for the same order: each run names its group, and runs start one at a time, from each
    //fabricator's own power update, so each sees the runs started before it.
    public static class FabTasks
    {
        public class Need
        {
            public string Key; //The orders' trigger, or CO: and the item, when they have none
            public string Trigger;
            public string ActionCO; //The loose item the orders take
            public JsonFabRecipe Recipe;
            public int Orders, Aboard, Building;
            public int Missing => Orders - Aboard - Building;
        }

        //Every group of the ship's build orders, with what's missing for each. Except leaves that fabricator's own run
        //out of what's being built, so it can see what its run was for.
        public static List<Need> Needs(Ship ship, CondOwner except = null)
        {
            var needs = new Dictionary<string, Need>();
            if (ship == null || ship.bDestroyed) return new List<Need>();
            List<CondOwner> all = ship.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>();
            foreach (CondOwner co in all)
            {
                Placeholder order = co != null && !co.bDestroyed ? co.GetComponent<Placeholder>() : null;
                if (order == null || !string.IsNullOrEmpty(order.strPersistentCO) || string.IsNullOrEmpty(order.strActionCO)) continue;
                List<CondOwner> lot = co.GetLotCOs(bSubItems: false);
                if (lot != null && lot.Count > 0) continue;
                string key = string.IsNullOrEmpty(order.strPersistentCT) ? "CO:" + order.strActionCO : order.strPersistentCT;
                if (!needs.TryGetValue(key, out Need need))
                {
                    need = new Need { Key = key, Trigger = order.strPersistentCT, ActionCO = order.strActionCO, Recipe = RecipeFor(order.strActionCO) };
                    needs[key] = need;
                }
                need.Orders++;
            }
            if (needs.Count == 0) return new List<Need>();

            foreach (Need need in needs.Values) need.Aboard = Aboard(ship, need);
            foreach (CondOwner fabricator in all.Where(Fabricator.IsFabricator))
            {
                if (fabricator == except) continue;
                string task = Fabricator.RunTask(fabricator);
                if (task != null && needs.TryGetValue(task, out Need need)) need.Building++;
            }
            return needs.Values.ToList();
        }

        //The group this fabricator should build for next: missing something it has the blueprint, materials, a good
        //beam former and room for. The one missing most comes first.
        public static Need Pick(CondOwner fabricator)
        {
            Ship ship = fabricator?.ship;
            foreach (Need need in Needs(ship, fabricator).Where(n => n.Missing > 0 && n.Recipe != null).OrderByDescending(n => n.Missing))
            {
                if (Fabricator.CanBuild(ship, need.Recipe) < 1) continue;
                if (BeamFormer.Check(fabricator, need.Recipe.WearUnits) != null) continue;
                string strCO = need.Recipe.strCOFabricate;
                if (Fabricator.FindStack(fabricator, strCO) == null && !Fabricator.FindSpot(fabricator, strCO, out _)) continue;
                return need;
            }
            return null;
        }

        //How many waiting build orders the ship's fabricators could build for: groups missing items with a known blueprint
        public static int Waiting(Ship ship) => Needs(ship).Where(n => n.Recipe != null).Sum(n => Math.Max(0, n.Missing));

        //The fabricable recipe for the loose item an order takes, while its blueprint is known
        private static JsonFabRecipe RecipeFor(string actionCO)
        {
            JsonFabRecipe recipe = FabData.GetRecipeForCO(actionCO);
            if (recipe == null || !recipe.bFabricate) recipe = FabData.GetFabricableFor(actionCO);
            return recipe != null && recipe.bFabricate && Blueprints.IsKnown(recipe.strBlueprint) ? recipe : null;
        }

        //Items aboard an order could take: loose, not locked, not still being made, each unit of a stack counted
        private static int Aboard(Ship ship, Need need)
        {
            CondTrigger trigger = string.IsNullOrEmpty(need.Trigger) ? null : DataHandler.GetCondTrigger(need.Trigger);
            List<CondOwner> items = ship.GetCOs(trigger, bSubObjects: true, bAllowDocked: false, bAllowLocked: false) ?? new List<CondOwner>();
            int count = 0;
            foreach (CondOwner item in items)
            {
                if (item == null || item.bDestroyed || item.HasCond("IsInstalled") || Locks.IsLocked(item) || item.HasCond(FabricatorProcess.UnfinishedCond)) continue;
                if (item.GetComponent<Placeholder>() != null) continue;
                if (trigger == null && item.strCODef != need.ActionCO) continue;
                count += Math.Max(1, item.StackCount);
            }
            return count;
        }
    }
}
