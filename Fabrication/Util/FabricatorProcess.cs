using Ostranauts.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Fabrication, run from each fabricator's power update about once a game second, so it stops while the game is
    //paused and keeps to game time under time acceleration. It doesn't run during a fast forward (GUIFFWD).
    //
    //While FABRICATE, REPEAT or AUTO is on and the fabricator is working, it builds one item at a time. AUTO builds
    //what the ship's build orders are waiting for (FabTasks) first. Otherwise it builds the chosen recipe: FABRICATE
    //builds COUNT of them, counting down, and switches itself off at 0, and REPEAT keeps building while there's room
    //in the area, whatever the count. With nothing it can start, it looks again every few game seconds.
    //
    //Each update draws that slice of the item's energy from the grid and the same share of its materials from the
    //silos. Short of materials it waits; short of power the run fails. An item can't be left part built, so a run
    //stopped partway deletes the unfinished item, and what it used is lost. A person in the area only pauses it.
    //
    //A canister it makes starts empty, and with FILL on is filled from the ship's reserves when it's done.
    public static class FabricatorProcess
    {
        public const string LockCond = "IsFzFabricateLocked";
        public const string UnfinishedCond = "IsFzUnfinished";
        private const double RetrySeconds = 5.0; //Game seconds between looks for room or materials while waiting

        public static bool IsLocked(CondOwner co) => co.HasCond(LockCond);

        //When each waiting fabricator next looks for a way to start, by ID, in game time. Only a cache: lost on a
        //load, when it simply looks again.
        private static readonly Dictionary<string, double> nextTry = new Dictionary<string, double>();

        public static void Clear() => nextTry.Clear();

        //Called from Powered.Run with the game seconds since the last update
        public static void Run(Powered power, double seconds)
        {
            CondOwner co = power.CO;
            if (!Fabricator.IsFabricator(co) || co.ship == null) return;
            if (GUIFFWD.Active) return;

            bool running = Fabricator.IsWorking(co) && Fabricator.IsTrying(co);
            if (Fabricator.HasRun(co) && !RunIntact(co))
            {
                FzAdditions.Logger.LogInfo($"Fabricator {co.strID} lost what it was building");
                Abort(co);
            }
            if (Fabricator.HasRun(co) && !running)
            {
                Abort(co);
                SetWorking(co, false);
                return;
            }
            if (!running)
            {
                SetWorking(co, false);
                return;
            }

            if (!Fabricator.HasRun(co))
            {
                if (Fabricator.IsFabricateOn(co) && !Fabricator.IsRepeat(co) && Fabricator.GetCount(co) <= 0) Fabricator.SetFabricateOn(co, false);
                if (!Fabricator.IsTrying(co))
                {
                    SetWorking(co, false);
                    return;
                }
                double now = co.Clock.Epoch;
                if (nextTry.TryGetValue(co.strID, out double next) && now < next)
                {
                    SetWorking(co, false);
                    return;
                }
                if (!Start(co))
                {
                    nextTry[co.strID] = now + RetrySeconds;
                    SetWorking(co, false);
                    return;
                }
                nextTry.Remove(co.strID);
            }

            if (WorkArea.AnyoneInArea(co))
            {
                SetWorking(co, false);
                return;
            }

            JsonFabRecipe recipe = Fabricator.RunRecipe(co);
            double runTime = Fabricator.RunTime(recipe);
            double done = Fabricator.Progress(co);
            //Progress is in standard seconds of work, so a faster fabricator does more of them each game second
            double work = Math.Min(Math.Max(0.0, seconds) * WorkArea.Speed(co), Math.Max(0.0, runTime - done));
            double share = runTime > 0.0 ? work / runTime : 1.0;

            //A beam former good for the rest of the item: taken out, or swapped for one too worn, it waits
            if (BeamFormer.Check(co, recipe.WearUnits * (runTime > 0.0 ? Math.Max(0.0, 1.0 - done / runTime) : 0.0)) != null)
            {
                SetWorking(co, false);
                return;
            }

            //This slice's materials, all of them or none
            if (share > 0.0 && !Fabricator.HasShare(co.ship, recipe, share))
            {
                SetWorking(co, false);
                return;
            }

            //This slice's energy, on top of the idle draw Powered has already taken. Short of it, the run fails.
            double kwh = Fabricator.ItemPower(recipe) * work / 3600.0;
            if (kwh > 0.0)
            {
                if (kwh > power.PowerConnected + 1e-9)
                {
                    FzAdditions.Logger.LogInfo($"Fabricator {co.strID} lost power building {recipe.strName}, it's lost");
                    Abort(co);
                    SetWorking(co, false);
                    return;
                }
                power.UserPowerExt(kwh);
            }

            if (share > 0.0)
            {
                foreach (var material in recipe.mapMaterials.Where(m => m.Value > 0)) Silo.Draw(co.ship, material.Key, material.Value * share);
                BeamFormer.Wear(co, recipe.WearUnits * share);
            }
            done += work;
            Fabricator.SetProgress(co, done);
            SetWorking(co, true);

            if (runTime <= 0.0 || done >= runTime - 1e-9) Finish(co, recipe);
        }

        //Whether the run's recipe, and its unfinished item or the stack it's building onto, are still there
        private static bool RunIntact(CondOwner co)
        {
            if (Fabricator.RunRecipe(co) == null) return false;
            return Fabricator.CurrentItem(co) != null || Fabricator.Target(co) != null;
        }

        //Starts building what's needed next (Fabricator.NextRecipe): onto a stack in the area with room, or as a new
        //item in the nearest place it fits. False when there's nothing to build, no materials or room, or someone's in
        //the way.
        private static bool Start(CondOwner co)
        {
            JsonFabRecipe recipe = Fabricator.NextRecipe(co, out string task);
            if (recipe == null || Fabricator.CanBuild(co.ship, recipe) < 1 || WorkArea.AnyoneInArea(co)) return false;
            if (BeamFormer.Check(co, recipe.WearUnits) != null) return false;
            string strCO = recipe.strCOFabricate;

            CondOwner stack = Fabricator.FindStack(co, strCO);
            if (stack != null)
            {
                Lock(stack);
                Fabricator.SetRun(co, recipe, null, stack, task);
                return true;
            }

            if (!Fabricator.FindSpot(co, strCO, out Vector2 spot)) return false;
            CondOwner item = Spawn(strCO);
            if (item == null) return false;
            try
            {
                Place(co.ship, item, spot);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError($"Placing a new {strCO} failed: {ex}");
                Destroy(item);
                return false;
            }
            item.AddCondAmount(UnfinishedCond, 1.0);
            Lock(item);
            Fabricator.SetRun(co, recipe, item, null, task);
            return true;
        }

        //A new item, empty: no loot in it, and any canister drained
        private static CondOwner Spawn(string strCO)
        {
            CondOwner item = DataHandler.GetCondOwner(strCO, null, null, false);
            if (item == null)
            {
                FzAdditions.Logger.LogError("A fabricator couldn't make " + strCO);
                return null;
            }
            Reserves.Empty(item);
            return item;
        }

        //Puts a new item loose on the ship at a point, the way the game drops things
        private static void Place(Ship ship, CondOwner item, Vector2 at)
        {
            item.tf.position = new Vector3(at.x, at.y, item.tf.position.z);
            ship.AddCO(item, true);
            item.Item?.ResetTransforms(at.x, at.y);
        }

        //The item is done: an unfinished one is unlocked where it is, and a stacking one is made and put on its
        //stack. FILL fills it. FABRICATE counts down for its own recipe.
        private static void Finish(CondOwner co, JsonFabRecipe recipe)
        {
            bool forOrders = Fabricator.RunTask(co) != null; //A build order's item doesn't count down FABRICATE's COUNT
            CondOwner item = Fabricator.CurrentItem(co);
            CondOwner stack = Fabricator.Target(co);
            if (item != null)
            {
                item.ZeroCondAmount(UnfinishedCond);
                Unlock(item);
                UI.ItemCut.Remove(item);
                if (Fabricator.IsFillOn(co)) Reserves.FillItem(co.ship, item);
            }
            else
            {
                CondOwner unit = Spawn(recipe.strCOFabricate);
                if (unit != null)
                {
                    if (Fabricator.IsFillOn(co)) Reserves.FillItem(co.ship, unit);
                    Vector2 at = stack != null ? (Vector2)stack.tf.position : (Vector2)co.tf.position;
                    unit.tf.position = new Vector3(at.x, at.y, unit.tf.position.z);
                    CondOwner left = unit;
                    if (stack != null && stack.CanStackOnItem(unit) > 0)
                    {
                        Unlock(stack);
                        try { left = stack.StackCO(unit); }
                        catch (Exception ex) { FzAdditions.Logger.LogError($"Stacking a new {recipe.strCOFabricate} failed: {ex}"); }
                    }
                    if (left != null && !left.bDestroyed && left.ship == null)
                    {
                        //No room on the stack after all: put it down by it instead
                        try { co.ship.DropCO(left, at); }
                        catch (Exception ex) { FzAdditions.Logger.LogError($"Dropping a new {recipe.strCOFabricate} failed: {ex}"); }
                    }
                }
                if (stack != null) Unlock(stack);
            }

            Fabricator.SetRun(co, null, null, null);
            SetWorking(co, false);
            if (!forOrders && !Fabricator.IsRepeat(co) && Fabricator.IsFabricateOn(co))
            {
                int count = Fabricator.GetCount(co) - 1;
                Fabricator.SetCount(co, count);
                if (count <= 0) Fabricator.SetFabricateOn(co, false);
            }
        }

        //Ends a run partway: an unfinished item is deleted, and a stack it was building onto is let go
        public static void Abort(CondOwner co)
        {
            CondOwner item = Fabricator.CurrentItem(co);
            if (item != null) Destroy(item);
            CondOwner stack = Fabricator.Target(co);
            if (stack != null) Unlock(stack);
            Fabricator.SetRun(co, null, null, null);
        }

        private static void Destroy(CondOwner item)
        {
            if (item == null || item.bDestroyed) return;
            CloseWindows(item);
            item.RemoveFromCurrentHome(bForce: true);
            item.Destroy();
        }

        //Deals with items left locked by a fabricator that's no longer working on them: an unfinished one is deleted,
        //a stack is let go. Run with the recycler's sweep, before each save and as each ship loads.
        public static int SweepOrphans(Ship ship)
        {
            if (ship == null || ship.bDestroyed) return 0;
            List<CondOwner> everything = ship.GetCOs(null, bSubObjects: true, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>();
            List<CondOwner> locked = everything.Where(c => c != null && !c.bDestroyed && (IsLocked(c) || c.HasCond(UnfinishedCond))).ToList();
            if (locked.Count == 0) return 0;

            var claimed = new HashSet<string>();
            foreach (CondOwner fabricator in everything.Where(Fabricator.IsFabricator))
            {
                CondOwner building = Fabricator.CurrentItem(fabricator);
                if (building != null) claimed.Add(building.strID);
                CondOwner stack = Fabricator.Target(fabricator);
                if (stack != null) foreach (CondOwner part in StackOf(stack)) claimed.Add(part.strID);
            }

            int dealt = 0;
            foreach (CondOwner orphan in locked.Where(c => !claimed.Contains(c.strID)))
            {
                if (orphan.bDestroyed) continue;
                dealt++;
                if (orphan.HasCond(UnfinishedCond))
                {
                    FzAdditions.Logger.LogInfo($"Removing {orphan.strName}, left unfinished by a fabricator");
                    Destroy(orphan);
                }
                else
                {
                    FzAdditions.Logger.LogInfo($"Unlocking {orphan.strName}, held by a fabricator no longer working on it");
                    orphan.ZeroCondAmount(LockCond);
                }
            }
            return dealt;
        }

        //An item and the rest of its stack
        private static IEnumerable<CondOwner> StackOf(CondOwner item)
        {
            List<CondOwner> stack = item.StackAsList;
            return stack != null && stack.Count > 0 ? stack : new List<CondOwner> { item };
        }

        //Locks an item, its whole stack, and closes any of its inventory windows
        private static void Lock(CondOwner item)
        {
            foreach (CondOwner part in StackOf(item).ToList())
            {
                if (!IsLocked(part)) part.AddCondAmount(LockCond, 1.0);
            }
            CloseWindows(item);
        }

        private static void Unlock(CondOwner item)
        {
            foreach (CondOwner part in StackOf(item).ToList()) part.ZeroCondAmount(LockCond);
        }

        private static void CloseWindows(CondOwner item)
        {
            try
            {
                GUIInventory gui = GUIInventory.instance;
                if (gui == null) return;
                var ids = new HashSet<string>(StackOf(item).Select(p => p.strID));
                if (gui.Selected != null && gui.Selected.CO != null && ids.Contains(gui.Selected.CO.strID)) gui.Reset();
                foreach (GUIInventoryWindow window in gui.activeWindows.ToList())
                {
                    if (window != null && window.CO != null && ids.Contains(window.CO.strID)) gui.Close(window);
                }
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Closing a fabricator item's inventory failed: " + ex);
            }
        }

        //IsFzFabricating, which makes the fabricator draw its working power and shows it working
        private static void SetWorking(CondOwner co, bool working)
        {
            bool has = co.HasCond(Fabricator.FabricatingCond);
            if (working && !has) co.AddCondAmount(Fabricator.FabricatingCond, 1.0);
            else if (!working && has) co.ZeroCondAmount(Fabricator.FabricatingCond);
        }
    }
}
