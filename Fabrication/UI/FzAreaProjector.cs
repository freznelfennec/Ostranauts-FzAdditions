using System;
using Freznel.FzAdditions.Util;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Freznel.FzAdditions.UI
{
    //Keeps a projected working area under every recycler and fabricator on the loaded ship and the ships docked to it.
    //An area shows while its machine is installed, powered and on, and while its panel is open, so it can be set up
    //with the power off. Its colour shows the recycler's state: blue idle, amber working, red blocked. The stripes run while it works.
    //
    //Projections are only drawing, rebuilt from the recyclers, so they're all dropped when the scene empties.
    public class FzAreaProjector : MonoBehaviour
    {
        private const float ScanInterval = 0.5f; //Real seconds between looks for recyclers and their state
        //Tiles a second the stripes run round the area: steadily when idle or previewing, faster while working, and not at
        //all when blocked, so the red reads as stopped
        private const float IdleSpeed = 0.35f, WorkingSpeed = 1.2f;

        private static FzAreaProjector instance;

        //The recycler whose panel is open, shown whatever its state
        public static CondOwner Previewing { get; set; }

        public static void Init()
        {
            if (instance != null) return;
            GameObject obj = new GameObject("FzAreaProjector");
            DontDestroyOnLoad(obj);
            instance = obj.AddComponent<FzAreaProjector>();
        }

        //Before a load or new game, and when leaving to the menu
        public static void Clear()
        {
            Previewing = null;
            if (instance == null) return;
            foreach (var entry in instance.areas.Values) entry.Destroy();
            instance.areas.Clear();
            instance.nextScan = 0f;
        }

        private class Area
        {
            public CondOwner Machine;
            public FzAreaProjection Projection;
            public bool Show;
            public FzAreaProjection.Look Look;
            public bool Working;
            public FzRecycleBeams Beams;

            public void Destroy()
            {
                if (Projection != null) UnityEngine.Object.Destroy(Projection.gameObject);
                if (Beams != null) UnityEngine.Object.Destroy(Beams.gameObject);
            }
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<Powered, double> LastPowerUpdate = HarmonyLib.AccessTools.FieldRefAccess<Powered, double>("fUpdateLast");

        //The item being recycled is drawn cut as far as its run has gone, and the beams show while it's working. The
        //run only moves on each power update, about once a game second, so while it's working the share is carried
        //on between them by the game time since the last one, which stands still while the game is paused.
        private static void ShowBeams(Area area)
        {
            CondOwner machine = area.Machine;
            if (Fabricator.IsFabricator(machine))
            {
                ShowBuilding(area);
                return;
            }
            CondOwner item = Recycler.CurrentItem(machine);
            if (item == null)
            {
                area.Beams?.Hide();
                return;
            }
            if (area.Beams == null) area.Beams = FzRecycleBeams.Create("FzBeams_" + machine.strID);
            bool working = machine.HasCond(Recycler.RecyclingCond);
            double runTime = Recycler.RunTime(item);
            double since = working && machine.Pwr != null ? Math.Max(0.0, Math.Min(1.0, machine.Clock.Epoch - LastPowerUpdate(machine.Pwr))) * WorkArea.Speed(machine) : 0.0;
            float share = runTime > 0.0 ? (float)Math.Min(1.0, (Recycler.Progress(machine) + since) / runTime) : 1f;
            area.Beams.Show(machine, item, share, working);
        }

        //A fabricator's run the other way round: the unfinished item grows in as its run goes, or a stack it's building
        //onto is lit by the beams
        private static void ShowBuilding(Area area)
        {
            CondOwner machine = area.Machine;
            JsonFabRecipe recipe = Fabricator.HasRun(machine) ? Fabricator.RunRecipe(machine) : null;
            CondOwner placed = recipe != null ? Fabricator.CurrentItem(machine) : null;
            CondOwner item = placed ?? (recipe != null ? Fabricator.Target(machine) : null);
            if (item == null)
            {
                area.Beams?.Hide();
                return;
            }
            if (area.Beams == null) area.Beams = FzRecycleBeams.Create("FzBeams_" + machine.strID);
            bool working = machine.HasCond(Fabricator.FabricatingCond);
            double runTime = Fabricator.RunTime(recipe);
            double since = working && machine.Pwr != null ? Math.Max(0.0, Math.Min(1.0, machine.Clock.Epoch - LastPowerUpdate(machine.Pwr))) * WorkArea.Speed(machine) : 0.0;
            float share = runTime > 0.0 ? (float)Math.Min(1.0, (Fabricator.Progress(machine) + since) / runTime) : 1f;
            area.Beams.Show(machine, item, share, working, building: true, grow: placed != null, materials: recipe.mapMaterials);
        }

        private readonly Dictionary<string, Area> areas = new Dictionary<string, Area>();
        private float nextScan;

        private void LateUpdate()
        {
            if (CrewSim.objInstance == null) return;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + ScanInterval;
                Scan();
            }

            foreach (Area area in areas.Values)
            {
                CondOwner machine = area.Machine;
                bool preview = machine == Previewing;
                bool alive = machine != null && !machine.bDestroyed && machine.tf != null;
                if (alive) ShowBeams(area); else area.Beams?.Hide();
                bool show = (area.Show || preview) && alive;
                area.Projection.SetVisible(show);
                if (!show) continue;
                area.Projection.Follow(machine.tf);
                area.Projection.SetArea(WorkArea.LocalArea(machine)); //Settings change from the panel between scans
                area.Projection.SetLook(area.Show ? area.Look : FzAreaProjection.Look.Preview);
                FzAreaProjection.Look look = area.Show ? area.Look : FzAreaProjection.Look.Preview;
                area.Projection.Animate(look == FzAreaProjection.Look.Working ? WorkingSpeed : look == FzAreaProjection.Look.Blocked ? 0f : IdleSpeed);
            }
        }

        //Finds the recyclers and works out each one's state, which needs looks around the ship, so not every frame
        private void Scan()
        {
            var found = new HashSet<string>();
            foreach (CondOwner machine in FindRecyclers())
            {
                found.Add(machine.strID);
                if (!areas.TryGetValue(machine.strID, out Area area) || area.Projection == null)
                {
                    area = new Area { Projection = FzAreaProjection.Create("FzArea_" + machine.strID) };
                    areas[machine.strID] = area;
                }
                area.Machine = machine; //A new form after a mode switch keeps the ID

                bool fabricator = Fabricator.IsFabricator(machine);
                area.Show = fabricator ? Fabricator.IsWorking(machine) : Recycler.IsWorking(machine);
                area.Working = area.Show && machine.HasCond(fabricator ? Fabricator.FabricatingCond : Recycler.RecyclingCond);
                //A fabricator is only blocked while it's trying to build
                bool blocked = area.Show && (fabricator
                    ? (Fabricator.IsFabricateOn(machine) || Fabricator.IsAuto(machine)) && !area.Working && Fabricator.Blocked(machine) != null
                    : Recycler.Blocked(machine) != null);
                area.Look = !area.Show ? FzAreaProjection.Look.Idle : blocked ? FzAreaProjection.Look.Blocked : area.Working ? FzAreaProjection.Look.Working : FzAreaProjection.Look.Idle;
            }

            foreach (string gone in areas.Keys.Where(id => !found.Contains(id)).ToList())
            {
                areas[gone].Destroy();
                areas.Remove(gone);
            }
        }

        //Installed, undamaged recyclers and fabricators on the loaded ship and its docked ships
        private static IEnumerable<CondOwner> FindRecyclers()
        {
            Ship ship = CrewSim.shipCurrentLoaded;
            if (ship == null || ship.bDestroyed) yield break;
            var triggers = new[] { "TIsFzRecyclerInstalled", Fabricator.InstalledTrigger }.Select(DataHandler.GetCondTrigger).Where(t => t != null).ToList();
            if (triggers.Count == 0) yield break;
            var ships = new List<Ship> { ship };
            var docked = ship.GetAllDockedShips();
            if (docked != null) ships.AddRange(docked.Where(s => s != null && !s.bDestroyed));
            foreach (Ship member in ships)
            {
                foreach (CondTrigger installed in triggers)
                {
                    foreach (CondOwner co in member.GetCOs(installed, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>())
                    {
                        if (co != null && !co.bDestroyed) yield return co;
                    }
                }
            }
        }
    }
}
