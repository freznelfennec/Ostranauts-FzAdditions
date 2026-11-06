using Ostranauts.ShipGUIs.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Util
{
    //Brings the ship's velocity against a target, or just its cross-velocity, to nothing, flown the same way as a series' course corrections and
    //saved the same way, so it carries on through a save and a time skip until it is done or stopped. A ship can't
    //match while a jump or series runs, nor start either while it matches.
    public static class VelocityMatch
    {
        private const string SkipLog = "[SFF match]";
        private const int MaxSkipBurns = 3; //Gravity can leave a little to trim after a burn

        public static bool IsMatching(Ship ship)
        {
            return MatchTarget.IsMatching(ship);
        }

        //False if it can't start, nothing has changed then. Station is the nav station it is started from. Cross only
        //takes out the velocity across the line to the target.
        public static bool Start(Ship ship, CondOwner station, NavPOI poi, bool torch, bool cross)
        {
            if (ship == null || station == null || JumpSequence.IsSeriesRunning(ship) || JumpSequence.IsActive(ship)) return false;
            if (!JumpDrive.ResolvePOI(ship, poi, out JumpDrive.AimTarget aim)) return false;

            MatchTarget.Save(ship, station, aim, torch, cross);
            ship.objSS?.ResetNavData();
            JumpCorrection.HoldControls(ship);
            FzAdditions.Logger.LogInfo($"{ship.strRegID} {(cross ? "cross-velocity" : "velocity")} match with {aim.Name} started on {(torch ? "torch" : "RCS")}");
            return true;
        }

        //Hands the ship's thrust back
        public static void Stop(Ship ship)
        {
            if (!IsMatching(ship)) return;
            JumpCorrection.Stop(ship);
            MatchTarget.Clear(ship);
        }

        //Leaves the reason for the nav console, the same as a jump that fails
        private static void Fail(Ship ship, JumpSequence.JumpFailure failure)
        {
            ship.ShipCO?.SetCondAmount(Constants.JumpFailureCond, (int)failure);
            Stop(ship);
        }

        //Every check while the ship is the one loaded, each frame while it burns
        public static void Advance(Ship ship)
        {
            if (!IsMatching(ship)) return;

            JumpCorrection.HoldControls(ship);
            if (!MatchTarget.TryResolve(ship, out JumpDrive.AimTarget aim))
            {
                Fail(ship, JumpSequence.JumpFailure.MatchTargetLost);
                return;
            }

            switch (JumpCorrection.Update(ship, aim, MatchTarget.OnTorch(ship), MatchTarget.GetGoal(ship)))
            {
                case JumpCorrection.Result.Done:
                    Stop(ship);
                    break;
                case JumpCorrection.Result.Failed:
                    Fail(ship, JumpSequence.JumpFailure.MatchFailed);
                    break;
            }
        }

        //Plays a match out over a time skip, which has no frames to steer in, the same way a series' corrections are.
        //Advance runs the game's own update for part of the skip. A match with nothing to thrust on carries on after.
        public static void RunSkip(Ship ship, double seconds, Action<double> advance)
        {
            JumpCorrection.Stop(ship);
            double left = seconds;
            void Pass(double piece)
            {
                advance(piece);
                left -= piece;
            }

            for (int burns = 0; burns < MaxSkipBurns && left > 0.0 && IsMatching(ship); burns++)
            {
                if (!MatchTarget.TryResolve(ship, out JumpDrive.AimTarget aim))
                {
                    Fail(ship, JumpSequence.JumpFailure.MatchTargetLost);
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} match ended, target lost");
                    break;
                }

                JumpCorrection.HoldControls(ship);
                JumpCorrection.Result result = JumpCorrection.PlanSkipBurn(ship, aim, MatchTarget.OnTorch(ship), MatchTarget.GetGoal(ship), out JumpCorrection.SkipBurn burn);
                if (result == JumpCorrection.Result.Done)
                {
                    Stop(ship);
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} velocity matched with {aim.Name}");
                    break;
                }
                if (result == JumpCorrection.Result.Waiting)
                {
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} match waiting, no thrust to match with");
                    break;
                }
                if (result == JumpCorrection.Result.Failed || !JumpCorrection.RunSkipBurn(ship, burn, () => left, Pass, $"{SkipLog} {ship.strRegID} match"))
                {
                    Fail(ship, JumpSequence.JumpFailure.MatchFailed);
                    FzAdditions.Logger.LogInfo($"{SkipLog} {ship.strRegID} match ended, not enough RCS propellant");
                    break;
                }
            }

            if (left > 0.0) advance(left);
        }
    }
}
