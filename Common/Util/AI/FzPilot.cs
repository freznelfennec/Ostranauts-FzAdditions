using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using System;

namespace Freznel.FzAdditions.Util.AI
{
    //A ship flown by the mod's own AI, run by FzAI rather than the game's AIShipManager. It's an IAICharacter, so the
    //game's own commands (Shoot, AreThreatsNearby, sequence and selector nodes) can go in its tree beside the mod's.
    //What has to last is kept on the ship (FzAI's conditions); everything here is rebuilt when the ship is found again
    //after a load.
    public abstract class FzPilot : IAICharacter
    {
        private Ship ship;

        public string RegID { get; }

        //The pilot type, also the end of the ship condition that marks it (FzAI.PilotCond)
        public abstract string Type { get; }

        //Game seconds between updates
        public virtual double Interval => 1.0;

        //Game time this pilot is next due, and when it last ran
        public double NextThink { get; set; }
        public double LastThink { get; set; }

        protected FzPilot(Ship ship)
        {
            this.ship = ship;
            RegID = ship.strRegID;
            Blackboard = new Blackboard();
            PriorityQueue = new QueuedCommandsNode("PriorityQueue", this);
        }

        //Builds the tree once the pilot is set up, since the game's commands read the ship from the pilot as they're made
        public void Init()
        {
            BehaviorTree = BuildTree();
        }

        protected abstract ICommand BuildTree();

        //One update, dt game seconds after the last
        public virtual void Think(double dt)
        {
            BehaviorTree?.RunCommand(Blackboard);
        }

        //A time skip of dt game seconds, run instead of Think while GUIFFWD is skipping
        public virtual void FastForward(double dt)
        {
        }

        //The pilot has been taken off its ship, or its ship has gone
        public virtual void OnRemoved()
        {
        }

        //A line for 'fz ai'
        public virtual string Describe()
        {
            Ship us = ShipUs;
            return $"{Type}, alert {FzAI.AlertName(FzAI.GetAlert(us))}, target {us?.shipCombatTarget?.strRegID ?? "none"}";
        }

        //Ships can be made again under the same ID as they load and unload, so the ship is looked up again once its
        //old object is gone
        public Ship ShipUs
        {
            get
            {
                if (ship == null || ship.bDestroyed) ship = CrewSim.system?.GetShipByRegID(RegID);
                return ship;
            }
        }

        //The game's commands want someone to judge factions by. A crewless ship or station judges by its ShipCO, which
        //carries the ship's factions.
        public CondOwner CaptainUs
        {
            get
            {
                Ship us = ShipUs;
                if (us == null) return null;
                CondOwner captain = null;
                if (us.Comms != null && us.Comms.GetCaptain(out captain) && captain != null) return captain;
                return us.ShipCO;
            }
        }

        public QueuedCommandsNode PriorityQueue { get; set; }

        public ICommand BehaviorTree { get; set; }

        public Blackboard Blackboard { get; set; }

        //NA keeps the ship out of everything the game picks by AI type
        public AIType AIType => AIType.NA;

        public virtual TargetData GetTarget()
        {
            Ship us = ShipUs;
            return us?.objSS != null ? new TargetData(us.objSS) : null;
        }

        public virtual TargetData GetTargetHome()
        {
            return GetTarget();
        }

        //AU per second, as the game's pilots give it. Stations hold still.
        public virtual double MaxSpeed(bool? inAtmo = null)
        {
            return 0.0;
        }

        public virtual ICommand FFWD(ICommand lastActiveCommand, Blackboard blackboard)
        {
            return lastActiveCommand;
        }
    }
}
