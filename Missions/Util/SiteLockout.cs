using System;
using System.Collections.Generic;
using System.Linq;

namespace Freznel.FzAdditions.Util
{
    //Per the user, a generated site's mounted equipment can't be uninstalled or dismantled until its Equipment Lockout
    //Controller (ItmFzLockoutController01, a copy of the base game's terminal placed in a random room) is hacked for its
    //kind. The controller is hacked from the same PDA app as a data vault (FzBreachApp, VaultHack), one kind at a
    //time, so the player chooses what to free first.
    //
    //A kind released is a persisted condition on the site's ShipCO (IsFzSiteUnlock<Kind>), so it lasts through saves.
    //SiteLockoutPatch fails uninstall and dismantle interactions on locked equipment.
    public static class SiteLockout
    {
        public const string ControllerCond = "IsFzLockoutController";
        public const string Panel = "FzLockout";
        public const double KindWork = 240.0; //Standard seconds of skilled QUIET work to release one kind

        public class Kind
        {
            public string Name;      //As shown, and in its condition
            public string Label;
            public Func<CondOwner, bool> Has;
            public string Cond => "IsFzSiteUnlock" + Name;
        }

        public static readonly Kind[] Kinds =
        {
            new Kind { Name = "Weapons", Label = "WEAPONS", Has = co => co.HasCond("IsShipWeapon") },
            new Kind { Name = "Sensors", Label = "SENSORS", Has = co => co.HasCond("IsCategorySensors") },
            new Kind { Name = "Power", Label = "POWER", Has = co => co.HasCond("IsPowerGen") || co.HasCond("IsPowerStorage") },
            new Kind { Name = "LifeSupport", Label = "LIFE SUPPORT", Has = co => co.HasCond("IsCategoryHVAC") || co.HasCond("IsCategoryLifeSupport") }
        };

        public static bool IsController(CondOwner co) => co != null && co.HasCond(ControllerCond);

        public static bool IsReleased(Ship site, Kind kind) => site?.ShipCO != null && site.ShipCO.HasCond(kind.Cond);

        public static void Release(Ship site, Kind kind)
        {
            if (site?.ShipCO == null) return;
            site.ShipCO.SetCondAmount(kind.Cond, 1.0);
        }

        //The kind a piece of equipment falls under, or null for anything the lockout doesn't hold
        public static Kind KindOf(CondOwner co)
        {
            if (co == null || !co.HasCond("IsInstalled")) return null;
            return Kinds.FirstOrDefault(k => k.Has(co));
        }

        //Whether this equipment is held by its site's lockout
        public static bool IsLocked(CondOwner co)
        {
            Ship site = co?.ship;
            if (site == null || !SiteDocking.IsSite(site)) return false;
            Kind kind = KindOf(co);
            return kind != null && !IsReleased(site, kind);
        }

        //How many of each kind a site has, for the panel
        public static Dictionary<string, int> Counts(Ship site)
        {
            var counts = Kinds.ToDictionary(k => k.Name, k => 0);
            if (site == null) return counts;
            foreach (CondOwner co in site.GetCOs(null, false, false, true) ?? new List<CondOwner>())
            {
                Kind kind = KindOf(co);
                if (kind != null) counts[kind.Name]++;
            }
            return counts;
        }
    }
}
