using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    //Times game methods from the console. Its patches go on with a Harmony instance of its own when it starts and
    //come off when it stops. They carry no Harmony attributes, so the plugin's PatchAll never applies them. Times
    //are inclusive, a method's time takes in every timed method it calls, and include a little of the profiler's
    //own cost. Only main thread work is timed.
    public static class FzProfiler
    {
        private const string HarmonyID = "me.freznel.fzadditions.profiler";
        private const int MaxFrameSamples = 200000;

        private class Entry
        {
            public string Name;
            public long Calls;
            public long Ticks;
            public long MaxTicks;
            public long FrameStartTicks; //Ticks as the current frame began, for what each spike frame spent
        }

        private static Harmony harmony;
        private static readonly Dictionary<MethodBase, Entry> entries = new Dictionary<MethodBase, Entry>();
        private static FrameSampler sampler;

        public static bool Running => harmony != null;

        //Returns how many methods were patched. Failed lists the ones Harmony refused.
        public static int Start(out List<string> failed)
        {
            failed = new List<string>();
            if (Running) return 0;

            //A run starts afresh, the last run's entries were only kept for its report
            entries.Clear();
            Reset();
            harmony = new Harmony(HarmonyID);
            HarmonyMethod prefix = new HarmonyMethod(typeof(FzProfiler).GetMethod(nameof(Prefix)));
            HarmonyMethod postfix = new HarmonyMethod(typeof(FzProfiler).GetMethod(nameof(Postfix)));

            int patched = 0;
            foreach (MethodBase method in ProfileTargets.GetTargets())
            {
                if (entries.ContainsKey(method)) continue; //Listed twice, as a frame method and a simulation step
                entries[method] = new Entry { Name = ProfileTargets.GetName(method) };
                try
                {
                    harmony.Patch(method, prefix, postfix);
                    patched++;
                }
                catch (Exception ex)
                {
                    entries.Remove(method);
                    failed.Add(ProfileTargets.GetName(method) + ": " + ex.Message);
                }
            }

            GameObject host = new GameObject("FzProfilerFrames");
            UnityEngine.Object.DontDestroyOnLoad(host);
            sampler = host.AddComponent<FrameSampler>();
            FzAdditions.Logger.LogInfo($"Profiler started on {patched} methods, {failed.Count} failed");
            return patched;
        }

        //Takes every patch off again, what was recorded is kept for the report
        public static void Stop()
        {
            if (!Running) return;
            harmony.UnpatchSelf();
            harmony = null;
            if (sampler != null)
            {
                sampler.Running = false;
                UnityEngine.Object.Destroy(sampler.gameObject);
            }
            FzAdditions.Logger.LogInfo("Profiler stopped");
        }

        public static void Reset()
        {
            foreach (Entry entry in entries.Values)
            {
                entry.Calls = 0;
                entry.Ticks = 0;
                entry.MaxTicks = 0;
            }
            FrameSampler.Clear();
        }

        //Runs before every timed method
        public static void Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        public static void Postfix(long __state, MethodBase __originalMethod)
        {
            long ticks = Stopwatch.GetTimestamp() - __state;
            if (!entries.TryGetValue(__originalMethod, out Entry entry)) return;
            entry.Calls++;
            entry.Ticks += ticks;
            if (ticks > entry.MaxTicks) entry.MaxTicks = ticks;
        }

        //The whole report goes to a file beside the BepInEx log, the console gets the top rows
        public static string Report(int rows, out string path)
        {
            int frames = Math.Max(FrameSampler.Frames, 1);
            double frameMs = FrameSampler.TotalSeconds * 1000.0 / frames;
            List<Entry> sorted = entries.Values.Where(entry => entry.Calls > 0).OrderByDescending(entry => entry.Ticks).ToList();

            StringBuilder full = new StringBuilder();
            full.AppendLine($"FzAdditions profile, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {(Running ? "running" : "stopped")}");
            full.AppendLine(GetFrameSummary(frames, frameMs));
            full.AppendLine(GetCollectionSummary());
            full.AppendLine("Times are inclusive: a method's time takes in every timed method it calls.");
            full.AppendLine();
            full.AppendLine($"{"ms/frame",9} {"% frame",8} {"calls/frame",12} {"avg us",9} {"max ms",8}  method");
            foreach (Entry entry in sorted) full.AppendLine(FormatRow(entry, frames, frameMs));
            full.AppendLine();
            full.AppendLine($"Frames over 100 ms: {FrameSampler.SpikeCount}{(FrameSampler.SpikeCount > FrameSampler.Spikes.Count ? $", the first {FrameSampler.Spikes.Count} shown" : "")}, each with the timed methods that took longest in it");
            foreach (string spike in FrameSampler.Spikes) full.AppendLine("  " + spike);

            path = Path.Combine(Paths.BepInExRootPath, "FzProfile.txt");
            try
            {
                File.WriteAllText(path, full.ToString());
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogError("Could not write the profile: " + ex.Message);
                path = null;
            }

            StringBuilder console = new StringBuilder();
            console.AppendLine(GetFrameSummary(frames, frameMs));
            console.AppendLine(GetCollectionSummary());
            console.AppendLine($"{FrameSampler.SpikeCount} frames over 100 ms, listed in the full report");
            console.AppendLine("ms/frame  % frame  method");
            foreach (Entry entry in sorted.Take(rows))
            {
                console.AppendLine($"{ToMs(entry.Ticks) / frames,8:0.000} {ToMs(entry.Ticks) / frames / frameMs * 100.0,7:0.0}%  {entry.Name}");
            }
            return console.ToString().TrimEnd();
        }

        private static string GetFrameSummary(int frames, double frameMs)
        {
            return $"{FrameSampler.Frames} frames over {FrameSampler.TotalSeconds:0.0}s, {frameMs:0.00} ms a frame, 99th percentile {FrameSampler.GetPercentileMs(0.99):0.00} ms, worst {FrameSampler.WorstMs:0.00} ms, "
                + $"{FrameSampler.Collections} garbage collections, about {FrameSampler.AllocatedPerFrame / 1024.0:0.0} KB allocated a frame";
        }

        //The game's garbage collector stops everything for a whole-heap collection, so a collection shows as a long frame
        private static string GetCollectionSummary()
        {
            return FrameSampler.CollectionFrames > 0
                ? $"{FrameSampler.CollectionFrames} frames ran a garbage collection, averaging {FrameSampler.CollectionFrameMs:0.0} ms, the worst {FrameSampler.WorstCollectionMs:0.0} ms"
                : "No frame ran a garbage collection";
        }

        private static string FormatRow(Entry entry, int frames, double frameMs)
        {
            double ms = ToMs(entry.Ticks);
            return $"{ms / frames,9:0.000} {ms / frames / frameMs * 100.0,7:0.0}% {(double)entry.Calls / frames,12:0.00} {ms * 1000.0 / entry.Calls,9:0.0} {ToMs(entry.MaxTicks),8:0.00}  {entry.Name}";
        }

        private static double ToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        //Real frame times while the profiler runs, how much the managed heap grew between collections, how long the
        //frames a collection ran in took, and which timed methods filled each long frame. A frame is timed from one
        //of its LateUpdates to the next, the same span the methods' times are summed over.
        private class FrameSampler : MonoBehaviour
        {
            private const int SkippedFrames = 2; //The frame the patches went on in is far longer than any other
            private const double SpikeMs = 100.0;
            private const int MaxSpikes = 30;
            private const int SpikeMethods = 6; //The timed methods that took longest in a spike, listed with it

            public bool Running = true;
            private int skipped;
            private long lastStamp;

            public static int Frames;
            public static double TotalSeconds;
            public static double WorstMs;
            public static int Collections;
            public static double AllocatedPerFrame => Frames > 0 ? allocated / Frames : 0.0;
            public static int CollectionFrames;
            public static double CollectionFrameMs => CollectionFrames > 0 ? collectionFrameTotalMs / CollectionFrames : 0.0;
            public static double WorstCollectionMs;
            public static int SpikeCount;
            public static readonly List<string> Spikes = new List<string>();

            private static readonly List<float> samples = new List<float>();
            private static double allocated;
            private static long lastMemory = -1;
            private static int lastCollections = -1;
            private static double collectionFrameTotalMs;

            public static void Clear()
            {
                Frames = 0;
                TotalSeconds = 0.0;
                WorstMs = 0.0;
                Collections = 0;
                CollectionFrames = 0;
                WorstCollectionMs = 0.0;
                SpikeCount = 0;
                Spikes.Clear();
                samples.Clear();
                allocated = 0.0;
                lastMemory = -1;
                lastCollections = -1;
                collectionFrameTotalMs = 0.0;
                foreach (Entry entry in entries.Values) entry.FrameStartTicks = 0;
            }

            public static double GetPercentileMs(double share)
            {
                if (samples.Count == 0) return 0.0;
                List<float> ordered = samples.OrderBy(sample => sample).ToList();
                return ordered[Math.Min((int)(share * ordered.Count), ordered.Count - 1)];
            }

            //Growth only, a collection makes the heap shrink and is counted on its own
            private void LateUpdate()
            {
                if (!Running) return;

                long stamp = Stopwatch.GetTimestamp();
                double ms = lastStamp > 0 ? ToMs(stamp - lastStamp) : 0.0;
                lastStamp = stamp;
                //The game's Boehm collector doesn't always count its collections, the heap shrinking shows one too
                int collections = GC.CollectionCount(0);
                long memory = GC.GetTotalMemory(false);
                bool collected = (lastCollections >= 0 && collections > lastCollections) || (lastMemory >= 0 && memory < lastMemory);
                lastCollections = collections;

                if (skipped < SkippedFrames)
                {
                    skipped++;
                    lastMemory = memory;
                    foreach (Entry entry in entries.Values) entry.FrameStartTicks = entry.Ticks;
                    return;
                }

                Frames++;
                TotalSeconds += ms / 1000.0;
                if (ms > WorstMs) WorstMs = ms;
                if (samples.Count < MaxFrameSamples) samples.Add((float)ms);

                if (collected)
                {
                    Collections++;
                    CollectionFrames++;
                    collectionFrameTotalMs += ms;
                    if (ms > WorstCollectionMs) WorstCollectionMs = ms;
                }

                if (lastMemory >= 0 && memory > lastMemory) allocated += memory - lastMemory;
                lastMemory = memory;

                if (ms >= SpikeMs) RecordSpike(ms, collected);
                foreach (Entry entry in entries.Values) entry.FrameStartTicks = entry.Ticks;
            }

            //Only a spike's own frame is looked at, so most frames cost one pass over the entries
            private static void RecordSpike(double ms, bool collected)
            {
                SpikeCount++;
                if (Spikes.Count >= MaxSpikes) return;

                IEnumerable<string> heaviest = entries.Values
                    .Where(entry => entry.Ticks > entry.FrameStartTicks)
                    .OrderByDescending(entry => entry.Ticks - entry.FrameStartTicks)
                    .Take(SpikeMethods)
                    .Select(entry => $"{entry.Name} {ToMs(entry.Ticks - entry.FrameStartTicks):0.0} ms");
                Spikes.Add($"{ms:0.0} ms{(collected ? ", garbage collection" : "")}, at {TotalSeconds:0.0}s: {string.Join(", ", heaviest)}");
            }
        }
    }
}
