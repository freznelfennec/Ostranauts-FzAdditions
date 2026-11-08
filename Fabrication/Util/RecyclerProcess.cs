using Ostranauts.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Recycling, run from each recycler's power update about once a game second, so it stops while the game is paused
    //and keeps to game time under time acceleration. It doesn't run during a fast forward (GUIFFWD).
    //
    //While RECYCLE or AUTO is on and the recycler is working, it takes the recyclable item nearest it in its area,
    //locks it and everything in it (IsFzRecycleLocked, which nothing may touch), and works through it: each update it
    //draws that slice of the item's energy from the grid and puts the same share of its yield into the ship's silos and
    //reserve canisters, so material arrives as it goes. When the run ends, contents that can't be recycled are dropped
    //where the item was, and the item and its recyclable contents are gone.
    //
    //An item can't be left part recycled: it's one thing with a fixed recipe, and anything less than whole would be
    //recycled in full again. So a run stopped partway, by RECYCLE and AUTO going off, the power failing or the recycler
    //being switched off, damaged or uninstalled, deletes the item, keeping what has already been delivered. A person
    //in the area, or no room for the rest with DUMP EXCESS off, only pauses it; the item stays locked.
    //
    //The run is kept in the recycler's prop map and the lock is a persisted condition, so both are saved.
    public static class RecyclerProcess
    {
        public const string LockCond = "IsFzRecycleLocked";

        public static bool IsLocked(CondOwner co) => co.HasCond(LockCond);

        //Called from Powered.Run with the game seconds since the last update
        public static void Run(Powered power, double seconds)
        {
            CondOwner co = power.CO;
            if (!Recycler.IsRecycler(co) || co.ship == null) return;
            if (GUIFFWD.Active) return;

            bool running = Recycler.IsWorking(co) && (Recycler.IsRecycleOn(co) || Recycler.IsAuto(co));
            CondOwner item = Recycler.CurrentItem(co);
            if (Recycler.HasRun(co) && item == null)
            {
                //Its item is gone, removed some other way; drop the run
                Recycler.SetCurrent(co, null);
            }
            if (item != null && !running)
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

            if (item == null)
            {
                item = Start(co);
                if (item == null)
                {
                    //Nothing left: RECYCLE does one pass of the area, AUTO keeps waiting for more
                    if (!Recycler.IsAuto(co) && Recycler.IsRecycleOn(co) && Recycler.Blocked(co) == null) Recycler.SetRecycleOn(co, false);
                    SetWorking(co, false);
                    return;
                }
            }

            if (Paused(co, item))
            {
                SetWorking(co, false);
                return;
            }

            double runTime = Recycler.RunTime(item);
            double done = Recycler.Progress(co);
            //Progress is in standard seconds of work, so a faster recycler does more of them each game second
            double work = Math.Min(Math.Max(0.0, seconds) * WorkArea.Speed(co), Math.Max(0.0, runTime - done));

            //This slice's energy, on top of the idle draw Powered has already taken. Short of it, the run fails.
            double kwh = Recycler.ItemPower(item) * work / 3600.0;
            if (kwh > 0.0)
            {
                if (kwh > power.PowerConnected + 1e-9)
                {
                    FzAdditions.Logger.LogInfo($"Recycler {co.strID} lost power recycling {item.strName}, it's lost");
                    Abort(co);
                    SetWorking(co, false);
                    return;
                }
                power.UserPowerExt(kwh);
            }

            double before = runTime > 0.0 ? done / runTime : 1.0;
            done += work;
            double after = runTime > 0.0 ? Math.Min(1.0, done / runTime) : 1.0;
            Deliver(co, item, after - before);
            BeamFormer.Wear(co, Recycler.WearUnits(item) * (after - before));
            Recycler.SetProgress(co, done);
            SetWorking(co, true);

            if (after >= 1.0 - 1e-9)
            {
                ReverseEngineer(item);
                Remove(co, item);
                Recycler.SetCurrent(co, null);
            }
        }

        //Picks the recyclable item nearest the recycler and locks it, or null when there's none it can start on
        private static CondOwner Start(CondOwner co)
        {
            if (Recycler.Blocked(co) != null) return null;
            Vector2 origin = co.tf.position;
            CondOwner next = Recycler.ItemsInArea(co)
                .Where(i => Recycler.IsRecyclable(i) && !Locks.IsLocked(i))
                .OrderBy(i => ((Vector2)i.tf.position - origin).sqrMagnitude)
                .FirstOrDefault();
            if (next == null) return null;
            if (!Recycler.IsDumpExcess(co) && Recycler.NoRoom(co.ship, Recycler.Breakdown(next), Recycler.ReservesIn(next)).Count > 0) return null;
            if (BeamFormer.Check(co, Recycler.WearUnits(next)) != null) return null;

            Lock(next);
            Recycler.SetCurrent(co, next);
            return next;
        }

        //A person in the area, no beam former good for the rest of the item, or no room for the rest of the yield
        //when it won't dump the excess
        private static bool Paused(CondOwner co, CondOwner item)
        {
            if (Recycler.AnyoneInArea(co)) return true;
            double runTime = Recycler.RunTime(item);
            double left = runTime > 0.0 ? 1.0 - Math.Min(1.0, Recycler.Progress(co) / runTime) : 0.0;
            //Its beam former, taken out or swapped for one too worn for the rest of the item
            if (left > 0.0 && BeamFormer.Check(co, Recycler.WearUnits(item) * left) != null) return true;
            if (Recycler.IsDumpExcess(co)) return false;
            if (left <= 0.0) return false;
            var materials = Recycler.Breakdown(item).ToDictionary(m => m.Key, m => m.Value * left);
            var reserves = Recycler.ReservesIn(item).ToDictionary(r => r.Key, r => r.Value * left);
            return Recycler.NoRoom(co.ship, materials, reserves).Count > 0;
        }

        //Puts a share of the item's yield into the ship. What doesn't fit is lost, which only happens when it dumps
        //the excess, as otherwise the run pauses first.
        private static void Deliver(CondOwner co, CondOwner item, double share)
        {
            if (share <= 0.0) return;
            foreach (var material in Recycler.Breakdown(item))
            {
                Silo.Fill(co.ship, material.Key, material.Value * share);
            }
            foreach (var held in Recycler.ReservesIn(item))
            {
                Reserves.Reserve reserve = Reserves.Get(held.Key);
                if (reserve != null) Reserves.Fill(co.ship, reserve, held.Value * share);
            }
        }

        //Ends a run partway: the item is deleted, as it can't be left part recycled
        //A finished run counts the item and everything recycled with it towards reverse engineering their blueprints
        //(Blueprints.Recycled), and tells the player of any it finished. A run stopped partway counts nothing.
        private static void ReverseEngineer(CondOwner item)
        {
            foreach (CondOwner part in Recycler.WithContents(item).ToList())
            {
                string learned = Blueprints.Recycled(part);
                if (learned == null) continue;
                var recipes = FabData.GetBlueprintRecipes(learned);
                string name = recipes != null && recipes.Count > 0 ? recipes[0].strNameFriendly : learned;
                CrewSim.coPlayer?.LogMessage($"Reverse engineered the blueprint for {name} from what the recycler took apart.", "Good", "Game");
            }
        }

        public static void Abort(CondOwner co)
        {
            CondOwner item = Recycler.CurrentItem(co);
            if (item != null) Remove(co, item);
            Recycler.SetCurrent(co, null);
        }

        private static void Remove(CondOwner co, CondOwner item)
        {
            Discard(item, item.ship ?? co.ship, co.tf != null ? (Vector2)co.tf.position : Vector2.zero);
            SetWorking(co, false);
        }

        //Drops the contents that can't be recycled where the item was, unlocked, and removes the item and its
        //recyclable contents, deepest first
        private static void Discard(CondOwner item, Ship ship, Vector2 fallback)
        {
            Vector2 where = item.tf != null ? (Vector2)item.tf.position : fallback;
            CloseWindows(item);
            foreach (CondOwner kept in Kept(item).ToList())
            {
                Unlock(kept);
                try { ship?.DropCO(kept, where); }
                catch (Exception ex) { FzAdditions.Logger.LogError($"Dropping {kept.strName} out of a recycled {item.strName} failed: {ex}"); }
            }
            foreach (CondOwner part in Recycler.WithContents(item).Reverse().ToList())
            {
                if (part == null || part.bDestroyed) continue;
                part.RemoveFromCurrentHome(bForce: true);
                part.Destroy();
            }
        }

        //Destroys locked items no recycler is working on, as a recycler that was destroyed outright, or a run lost some
        //other way, would leave its item locked for good. An item is rightly locked only while it, or the item it's in,
        //is a recycler's current item. An orphaned item that can be recycled goes as a run's would, its unrecyclable
        //contents dropped and unlocked; one that can't is only unlocked. Not run all the time, as it looks through
        //every item on a ship: before each save, and as each ship loads. Returns how many it dealt with.
        public static int SweepOrphans(Ship ship)
        {
            if (ship == null || ship.bDestroyed) return 0;
            List<CondOwner> everything = ship.GetCOs(null, bSubObjects: true, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>();
            List<CondOwner> locked = everything.Where(c => c != null && !c.bDestroyed && IsLocked(c)).ToList();
            if (locked.Count == 0) return 0;

            var claimed = new HashSet<string>();
            foreach (CondOwner recycler in everything.Where(Recycler.IsRecycler))
            {
                CondOwner item = Recycler.CurrentItem(recycler);
                if (item != null) foreach (CondOwner part in Everything(item)) claimed.Add(part.strID);
            }

            var orphans = new HashSet<string>(locked.Where(c => !claimed.Contains(c.strID)).Select(c => c.strID));
            int dealt = 0;
            //Outermost orphans only, their contents go with them
            foreach (CondOwner orphan in locked.Where(c => orphans.Contains(c.strID) && (c.objCOParent == null || !orphans.Contains(c.objCOParent.strID))))
            {
                if (orphan.bDestroyed) continue;
                dealt++;
                if (Recycler.IsRecyclable(orphan))
                {
                    FzAdditions.Logger.LogInfo($"Removing {orphan.strName}, locked for recycling with no recycler working on it");
                    Discard(orphan, orphan.ship ?? ship, Vector2.zero);
                }
                else
                {
                    FzAdditions.Logger.LogInfo($"Unlocking {orphan.strName}, locked for recycling with no recycler working on it");
                    Unlock(orphan);
                }
            }
            return dealt;
        }

        //Contents of a recyclable item that can't be recycled themselves, at any depth. Their own contents stay in them.
        private static IEnumerable<CondOwner> Kept(CondOwner item)
        {
            foreach (CondOwner inside in Recycler.Contents(item))
            {
                if (!Recycler.IsRecyclable(inside)) yield return inside;
                else foreach (CondOwner deeper in Kept(inside)) yield return deeper;
            }
        }

        //Locks an item and everything in it, and closes any of their inventory windows
        private static void Lock(CondOwner item)
        {
            foreach (CondOwner part in Everything(item))
            {
                if (!IsLocked(part)) part.AddCondAmount(LockCond, 1.0);
            }
            CloseWindows(item);
        }

        private static void Unlock(CondOwner item)
        {
            foreach (CondOwner part in Everything(item)) part.ZeroCondAmount(LockCond);
        }

        private static IEnumerable<CondOwner> Everything(CondOwner item)
        {
            yield return item;
            foreach (CondOwner inside in Recycler.Contents(item))
                foreach (CondOwner part in Everything(inside)) yield return part;
        }

        //Closes the inventory windows of an item or anything in it, and lets go of any of them being dragged
        private static void CloseWindows(CondOwner item)
        {
            try
            {
                GUIInventory gui = GUIInventory.instance;
                if (gui == null) return;
                var ids = new HashSet<string>(Everything(item).Select(p => p.strID));
                if (gui.Selected != null && gui.Selected.CO != null && ids.Contains(gui.Selected.CO.strID)) gui.Reset();
                foreach (GUIInventoryWindow window in gui.activeWindows.ToList())
                {
                    if (window != null && window.CO != null && ids.Contains(window.CO.strID)) gui.Close(window);
                }
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Closing a recycled item's inventory failed: " + ex);
            }
        }

        //IsFzRecycling, which makes the recycler draw its working power and shows it working
        private static void SetWorking(CondOwner co, bool working)
        {
            bool has = co.HasCond(Recycler.RecyclingCond);
            if (working && !has) co.AddCondAmount(Recycler.RecyclingCond, 1.0);
            else if (!working && has) co.ZeroCondAmount(Recycler.RecyclingCond);
        }
    }
}
