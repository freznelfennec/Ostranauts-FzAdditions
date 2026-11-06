using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Util
{
    //A target and which thrust to fly it on, saved on the nav station it was picked from under a prop map of its own.
    //Prop maps only save on parts, not on the ship's own CO, so the station holds it. The target is kept by the IDs
    //the nav console saves its own by and found again each time it is needed, since ships may not have loaded yet
    //when a save does. Anything else that has to last through a save, such as a burn under way, is kept with it.
    public class TargetStore
    {
        private class Holder
        {
            public Ship Ship;
            public CondOwner Station; //Null once the ship has been searched and has none
        }

        private readonly string propMap;

        //Keyed by the ship's strRegID, so the ship isn't searched each time. Kept with the ship it was found on,
        //a save loaded since has its own ships.
        private readonly Dictionary<string, Holder> stations = new Dictionary<string, Holder>();

        public TargetStore(string propMap)
        {
            this.propMap = propMap;
        }

        public void Save(Ship ship, CondOwner station, JumpDrive.AimTarget aim, bool torch)
        {
            Clear(ship);
            station.mapGUIPropMaps[propMap] = new Dictionary<string, string>
            {
                [Constants.SeriesKindProp] = aim.Kind.ToString(),
                [Constants.SeriesIDProp] = aim.ID,
                [Constants.SeriesOffsetXProp] = aim.OffsetX.ToString("R", CultureInfo.InvariantCulture),
                [Constants.SeriesOffsetYProp] = aim.OffsetY.ToString("R", CultureInfo.InvariantCulture),
                [Constants.SeriesTorchProp] = torch.ToString().ToLower()
            };
            stations[ship.strRegID] = new Holder { Ship = ship, Station = station };
        }

        public void Clear(Ship ship)
        {
            if (ship == null) return;
            FindStation(ship)?.mapGUIPropMaps.Remove(propMap);
            stations[ship.strRegID] = new Holder { Ship = ship };
        }

        public bool HasTarget(Ship ship)
        {
            return GetProps(ship) != null;
        }

        //False once the target or the station it was saved on is gone
        public bool TryResolve(Ship ship, out JumpDrive.AimTarget aim)
        {
            aim = new JumpDrive.AimTarget();
            Dictionary<string, string> props = GetProps(ship);
            if (props == null || !props.TryGetValue(Constants.SeriesKindProp, out string kindValue) || !Enum.TryParse(kindValue, out JumpDrive.TargetKind kind)) return false;

            props.TryGetValue(Constants.SeriesIDProp, out string id);
            return JumpDrive.Resolve(ship, kind, id, GetDouble(props, Constants.SeriesOffsetXProp), GetDouble(props, Constants.SeriesOffsetYProp), out aim);
        }

        public bool OnTorch(Ship ship)
        {
            return bool.TryParse(GetValue(ship, Constants.SeriesTorchProp), out bool torch) && torch;
        }

        //Null if there is no target or it doesn't have the value
        public string GetValue(Ship ship, string key)
        {
            Dictionary<string, string> props = GetProps(ship);
            return props != null && props.TryGetValue(key, out string value) ? value : null;
        }

        //Null removes it. False if there is no target to keep it with.
        public bool SetValue(Ship ship, string key, string value)
        {
            Dictionary<string, string> props = GetProps(ship);
            if (props == null) return false;
            if (value == null) props.Remove(key); else props[key] = value;
            return true;
        }

        //A save loading has ships of its own
        public void Reset()
        {
            stations.Clear();
        }

        private Dictionary<string, string> GetProps(Ship ship)
        {
            CondOwner station = FindStation(ship);
            return station != null && station.mapGUIPropMaps.TryGetValue(propMap, out Dictionary<string, string> props) ? props : null;
        }

        //The nav stations are checked first, a station that is off isn't listed among them so the ship is searched after.
        //Only Save adds a target, so a ship found with none isn't searched again.
        private CondOwner FindStation(Ship ship)
        {
            if (ship == null) return null;
            if (stations.TryGetValue(ship.strRegID, out Holder cached) && cached.Ship == ship)
            {
                if (cached.Station == null || IsHolder(cached.Station, ship)) return cached.Station;
            }

            CondOwner station = ship.aNavs?.FirstOrDefault(co => IsHolder(co, ship))
                ?? ship.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: false).FirstOrDefault(co => IsHolder(co, ship));
            stations[ship.strRegID] = new Holder { Ship = ship, Station = station };
            return station;
        }

        private bool IsHolder(CondOwner co, Ship ship)
        {
            return co != null && co.ship == ship && co.mapGUIPropMaps != null && co.mapGUIPropMaps.ContainsKey(propMap);
        }

        private static double GetDouble(Dictionary<string, string> props, string key)
        {
            return props.TryGetValue(key, out string value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : 0.0;
        }
    }
}
