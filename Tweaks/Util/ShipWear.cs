using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Spreads a loaded ship's wear over several frames. The game applies it every 300 game seconds by breaking in
    //every object on the ship in one frame, which takes tens of milliseconds on a big ship, and every ship's timer
    //starts as the save loads, so they all come due on the same frame. Each object still gets the same wear, only
    //later by a frame or so.
    public static class ShipWear
    {
        private const double WearPerSecond = 1.5844382307706396E-09; //The game's, Ship.DamageOverTime
        private const double FrameBudgetMs = 1.0; //Of wear a frame, at least one object is always done

        private class Job
        {
            public Ship Ship;
            public List<CondOwner> Objects;
            public float Amount;
            public int Next;
        }

        private static readonly Queue<Job> jobs = new Queue<Job>();

        public static int Pending => jobs.Count;

        //Outlives any save loaded, the jobs themselves go with the world
        public static void Init()
        {
            GameObject runner = new GameObject("FzShipWear");
            runner.AddComponent<WearRunner>();
            UnityEngine.Object.DontDestroyOnLoad(runner);
        }

        //Seconds is the game time the wear covers. The objects are the ones the game would break in now.
        public static void Queue(Ship ship, double seconds)
        {
            List<CondOwner> objects = ship.GetCOs(DataHandler.GetCondTrigger("Blank"), bSubObjects: true, bAllowDocked: false, bAllowLocked: true);
            if (objects.Count == 0) return;
            jobs.Enqueue(new Job { Ship = ship, Objects = objects, Amount = (float)(WearPerSecond * seconds) });
        }

        //A save is made with every ship's wear applied, so none of it is lost to a load
        public static void FinishAll()
        {
            while (jobs.Count > 0)
            {
                Run(jobs.Peek(), long.MaxValue);
                jobs.Dequeue();
            }
        }

        //A save loading has ships of its own
        public static void Reset()
        {
            jobs.Clear();
        }

        private static void RunFrame()
        {
            long deadline = Stopwatch.GetTimestamp() + (long)(FrameBudgetMs / 1000.0 * Stopwatch.Frequency);
            bool first = true;
            while (jobs.Count > 0 && (first || Stopwatch.GetTimestamp() < deadline))
            {
                first = false;
                if (Run(jobs.Peek(), deadline)) jobs.Dequeue();
            }
        }

        //True once the job is done. The same as Ship.DamageAllCOs for each object, an object gone since is skipped.
        private static bool Run(Job job, long deadline)
        {
            if (job.Ship == null || job.Ship.bDestroyed) return true;

            CondOwner selectedCrew = CrewSim.GetSelectedCrew();
            do
            {
                if (job.Next >= job.Objects.Count) return true;

                CondOwner co = job.Objects[job.Next++];
                if (co == null) continue;
                co.BreakIn(job.Amount, allowMultiple: true);
                if (selectedCrew != null && selectedCrew.ship == co.ship && co.GetDamageState() <= 0.0) BeatManager.ResetTensionTimer();
            }
            while (Stopwatch.GetTimestamp() < deadline);
            return job.Next >= job.Objects.Count;
        }

        //Waits out a pause, the game only wears ships while time passes
        private class WearRunner : MonoBehaviour
        {
            private void LateUpdate()
            {
                if (jobs.Count == 0 || CrewSim.Paused) return;
                try
                {
                    RunFrame();
                }
                catch (Exception ex)
                {
                    FzAdditions.Logger.LogError("Ship wear failed, its remaining objects are skipped: " + ex);
                    if (jobs.Count > 0) jobs.Dequeue();
                }
            }
        }
    }
}
