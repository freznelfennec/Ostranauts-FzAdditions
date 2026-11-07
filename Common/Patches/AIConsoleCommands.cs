using Freznel.FzAdditions.Util.AI;
using Ostranauts.UI.MegaToolTip;
using System;
using System.Linq;

namespace Freznel.FzAdditions.Patches
{
    //Console commands for the mod's AI. A ship is named by its registration, or [them] for the one selected in the
    //tooltip.
    public static class AIConsoleCommands
    {
        public static void Register()
        {
            ConsolePatch.AddCommand("ai", KeywordAi,
                "shows and changes ships flown by the mod's AI",
                "'fz ai' lists every flown ship with its pilot, alert, target and state",
                "'fz ai types' lists the pilot types",
                "'fz ai attach <type> <ship>' puts a pilot on a ship, by registration or [them]",
                "'fz ai detach <ship>' takes the pilot off",
                "'fz ai alert <calm|alerted|hostile> <ship>' sets a flown ship's alert level");
        }

        private static bool KeywordAi(ref string strInput, string[] args)
        {
            string sub = args.Length > 1 ? args[1].ToLower() : "list";
            switch (sub)
            {
                case "list":
                    if (FzAI.All.Count == 0)
                    {
                        strInput += "\nNo ships are flown by the mod's AI.";
                        return true;
                    }
                    foreach (FzPilot pilot in FzAI.All)
                    {
                        Ship ship = pilot.ShipUs;
                        string name = string.IsNullOrEmpty(ship?.publicName) ? "" : $" ({ship.publicName})";
                        strInput += $"\n{pilot.RegID}{name}: {pilot.Describe()}, {(ship != null && ship.LoadState > Ship.Loaded.Shallow ? "loaded" : "shallow")}";
                    }
                    return true;

                case "types":
                    strInput += "\nPilot types: " + string.Join(", ", FzAI.Types);
                    return true;

                case "attach":
                    {
                        if (args.Length != 4) return Usage(ref strInput);
                        Ship ship = FindShip(args[3], ref strInput);
                        if (ship == null) return false;
                        string type = FzAI.Types.FirstOrDefault(t => t.Equals(args[2], StringComparison.OrdinalIgnoreCase)) ?? args[2];
                        if (!FzAI.Attach(ship, type, out string error))
                        {
                            strInput += "\n" + error;
                            return false;
                        }
                        strInput += $"\n{ship.strRegID} is now flown by a {type} pilot.";
                        return true;
                    }

                case "detach":
                    {
                        if (args.Length != 3) return Usage(ref strInput);
                        Ship ship = FindShip(args[2], ref strInput);
                        if (ship == null) return false;
                        FzAI.Detach(ship);
                        strInput += $"\n{ship.strRegID} is no longer flown by the mod's AI.";
                        return true;
                    }

                case "alert":
                    {
                        if (args.Length != 4) return Usage(ref strInput);
                        int level;
                        switch (args[2].ToLower())
                        {
                            case "calm": case "0": level = FzAI.Calm; break;
                            case "alerted": case "1": level = FzAI.Alerted; break;
                            case "hostile": case "2": level = FzAI.Hostile; break;
                            default: return Usage(ref strInput);
                        }
                        Ship ship = FindShip(args[3], ref strInput);
                        if (ship == null) return false;
                        FzAI.SetAlert(ship, level);
                        strInput += $"\n{ship.strRegID} is now {FzAI.AlertName(level)}.";
                        return true;
                    }

                default:
                    return Usage(ref strInput);
            }
        }

        private static bool Usage(ref string strInput)
        {
            strInput += "\nUsage: fz ai, fz ai types, fz ai attach <type> <ship>, fz ai detach <ship>, fz ai alert <calm|alerted|hostile> <ship>";
            return false;
        }

        private static Ship FindShip(string name, ref string strInput)
        {
            Ship ship;
            if (name.Equals("[them]", StringComparison.OrdinalIgnoreCase))
            {
                ship = GUIMegaToolTip.Selected?.ship;
                if (ship == null) strInput += "\nNothing is selected for [them].";
                return ship;
            }
            ship = CrewSim.system?.GetShipByRegID(name) ?? CrewSim.system?.GetShipByRegID(name.ToUpper());
            if (ship == null) strInput += $"\nNo ship registered as {name}.";
            return ship;
        }
    }
}
