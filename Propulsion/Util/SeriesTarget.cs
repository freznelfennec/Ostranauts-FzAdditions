using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Util
{
    //Where a running series is going and which thrust it corrects on. A series started without a target has none
    //saved, and runs along its velocity.
    public static class SeriesTarget
    {
        public static readonly TargetStore Store = new TargetStore(Constants.SeriesGUIPropMap);

        public static void Save(Ship ship, CondOwner station, JumpDrive.AimTarget aim, bool torch) => Store.Save(ship, station, aim, torch);

        public static void Clear(Ship ship) => Store.Clear(ship);

        //False for a series started without a target, and for any ship with no series
        public static bool HasTarget(Ship ship) => Store.HasTarget(ship);

        public static bool TryResolve(Ship ship, out JumpDrive.AimTarget aim) => Store.TryResolve(ship, out aim);

        public static bool CorrectsOnTorch(Ship ship) => Store.OnTorch(ship);

        public static void Reset() => Store.Reset();
    }

    //What a velocity match is matching, which thrust it burns on and whether it only takes out the cross-velocity,
    //saved the same way as a series' target
    public static class MatchTarget
    {
        public static readonly TargetStore Store = new TargetStore(Constants.MatchGUIPropMap);

        public static void Save(Ship ship, CondOwner station, JumpDrive.AimTarget aim, bool torch, bool cross)
        {
            Store.Save(ship, station, aim, torch);
            Store.SetValue(ship, Constants.MatchCrossProp, cross.ToString().ToLower());
        }

        public static bool IsCross(Ship ship) => bool.TryParse(Store.GetValue(ship, Constants.MatchCrossProp), out bool cross) && cross;

        public static JumpCorrection.Goal GetGoal(Ship ship) => IsCross(ship) ? JumpCorrection.Goal.CrossMatch : JumpCorrection.Goal.Match;

        public static void Clear(Ship ship) => Store.Clear(ship);

        public static bool IsMatching(Ship ship) => Store.HasTarget(ship);

        public static bool TryResolve(Ship ship, out JumpDrive.AimTarget aim) => Store.TryResolve(ship, out aim);

        public static bool OnTorch(Ship ship) => Store.OnTorch(ship);

        public static void Reset() => Store.Reset();
    }
}
