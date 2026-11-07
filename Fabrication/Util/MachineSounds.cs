using System;

namespace Freznel.FzAdditions.Util
{
    //Sounds for the recycler and fabricator, rendered once into mono clips that each machine plays from where it is.
    //Kept quiet and soft-edged so a room full of them blends rather than stacks. While a machine runs, a steady low
    //hum carries the sound, and printer noises come and go on top of it: low, slow stepper motors, buzzing at a pitch
    //set by how fast the head moves. The fabricator is a 3D printer: long, unhurried moves with long rests between,
    //and a layer change now and then. The recycler prints in reverse: a long, low pass out across the item with a
    //soft hiss as it comes apart, a return, a row advance, and a rest. Starting and finishing are soft, so a short run
    //is a swell of hum rather than a burst of noises. Plain C#, so it can be rendered outside the game too.
    public static class MachineSounds
    {
        public enum Clip { RecyclerIdle, RecyclerWork, RecyclerStart, RecyclerFinish, FabricatorIdle, FabricatorWork, FabricatorStart, FabricatorFinish }

        public const double LoopSeconds = 12.0;
        public const double StartSeconds = 1.4;
        private const double Seam = 0.4; //Seconds crossfaded at a loop's ends so it repeats without a click

        private const double RecyclerHum = 49.0, FabricatorHum = 58.0; //The running hum's pitch, a little apart so the two can be told apart

        public static float[] Render(Clip clip, int rate)
        {
            switch (clip)
            {
                case Clip.RecyclerIdle: return Loop(rate, 2.0, (t, s) => s.Idle(t, 52.0));
                case Clip.FabricatorIdle: return Loop(rate, 2.0, (t, s) => s.Idle(t, 78.0));
                case Clip.RecyclerWork: return Loop(rate, LoopSeconds, (t, s) => s.Hum(t, RecyclerHum) + s.RecyclerWork(t));
                case Clip.FabricatorWork: return Loop(rate, LoopSeconds, (t, s) => s.Hum(t, FabricatorHum) + s.FabricatorWork(t));
                case Clip.RecyclerStart: return Once(rate, StartSeconds, (t, s) => s.Start(t, RecyclerHum, 49));
                case Clip.FabricatorStart: return Once(rate, StartSeconds, (t, s) => s.Start(t, FabricatorHum, 65));
                case Clip.RecyclerFinish: return Once(rate, 1.6, (t, s) => s.RecyclerFinish(t));
                default: return Once(rate, 1.2, (t, s) => s.FabricatorFinish(t));
            }
        }

        private static float[] Once(int rate, double seconds, Func<double, Voice, double> sample)
        {
            Voice voice = new Voice(rate, 7);
            float[] data = new float[(int)(seconds * rate)];
            for (int i = 0; i < data.Length; i++) data[i] = (float)Soft(sample((double)i / rate, voice));
            return data;
        }

        //Rendered a seam longer, then the extra tail is faded over the head so the end runs into the start
        private static float[] Loop(int rate, double seconds, Func<double, Voice, double> sample)
        {
            Voice voice = new Voice(rate, 11);
            int n = (int)(seconds * rate), seam = (int)(Seam * rate);
            double[] raw = new double[n + seam];
            for (int i = 0; i < raw.Length; i++) raw[i] = sample((double)i / rate, voice);

            float[] data = new float[n];
            for (int i = 0; i < n; i++)
            {
                double v = raw[i];
                if (i < seam)
                {
                    double w = (double)i / seam;
                    v = raw[i] * w + raw[n + i] * (1.0 - w);
                }
                data[i] = (float)Soft(v);
            }
            return data;
        }

        private static double Soft(double x)
        {
            return Math.Tanh(x * 0.8) / 0.8;
        }

        //A stepper motor: a narrow pulse at each step, through a resonant band for the motor's whine and a lower one
        //for the frame it's bolted to. Pitch is the step rate.
        private class Stepper
        {
            private readonly AudioDsp.Svf whine, frame;
            private readonly double dt, whineAt, frameAt;
            private double phase;

            public Stepper(int rate, double whineAt, double frameAt)
            {
                dt = 1.0 / rate;
                whine = new AudioDsp.Svf(rate); frame = new AudioDsp.Svf(rate);
                this.whineAt = whineAt; this.frameAt = frameAt;
            }

            public double Run(double steps)
            {
                phase += dt * steps;
                double p = phase - Math.Floor(phase);
                double pulse = p < 0.18 ? 1.0 : -0.22;
                whine.Run(pulse, Math.Max(steps * 2.0, whineAt), 3.0);
                frame.Run(pulse, frameAt, 2.0);
                return whine.Band * 0.5 + frame.Band * 0.35 + pulse * 0.08;
            }
        }

        //One sound being rendered, with its own noise, motors and filters
        private class Voice
        {
            private const double Accel = 0.12; //Seconds a heavy head takes to get up to speed, and to stop

            private readonly double dt;
            private readonly Random noise, moves;
            private readonly AudioDsp.Drift drift, wander;
            private readonly AudioDsp.Svf a, b, d, humTone;
            private readonly Stepper head, bed;
            private double phaseA, phaseB, humPhase;

            //The fabricator's moves: each a speed and a length, with a rest after
            private double moveStart, moveEnd = -1, moveSpeed, pauseEnd = 0.8, nextLayer = 9.0, layerAt = -10;
            private static readonly double[] Speeds = { 49, 65, 73, 98, 110 }; //Steps a second, a low scale so moves sit in key

            public Voice(int rate, int seed)
            {
                dt = 1.0 / rate;
                noise = new Random(seed);
                moves = new Random(seed * 31 + 5);
                drift = new AudioDsp.Drift(seed + 1, 0.9);
                wander = new AudioDsp.Drift(seed + 2, 1.7);
                a = new AudioDsp.Svf(rate); b = new AudioDsp.Svf(rate); d = new AudioDsp.Svf(rate); humTone = new AudioDsp.Svf(rate);
                head = new Stepper(rate, 380, 220);
                bed = new Stepper(rate, 240, 110);
            }

            private double Noise() { return noise.NextDouble() * 2.0 - 1.0; }

            //A move's speed ramps up at its start and back down at its end, as a heavy motor accelerates
            private static double Ramp(double t, double start, double end)
            {
                double inMove = Math.Min(t - start, end - t);
                if (inMove <= 0) return 0;
                double u = Math.Min(inMove / Accel, 1.0);
                return u * u * (3 - 2 * u);
            }

            //A move: the motor at a speed, rising from 40% as it ramps, at a level
            private double Move(Stepper motor, double t, double start, double end, double speed, double level)
            {
                if (t < start || t >= end) return 0;
                double ramp = Ramp(t, start, end);
                return motor.Run(speed * (0.4 + 0.6 * ramp)) * ramp * level;
            }

            //A soft knock as a move comes to rest against something
            private double Knock(double t, double at, double hz, double level)
            {
                double s = t - at;
                if (s < 0 || s >= 0.3) return 0;
                phaseB += dt * (hz + hz * 0.5 * Math.Exp(-s * 20));
                return Math.Sin(2 * Math.PI * phaseB) * Math.Exp(-s * 13) * Math.Min(s / 0.004, 1) * level;
            }

            //Powered and waiting: a faint mains hum, barely there
            public double Idle(double t, double hz)
            {
                phaseA += dt * hz; phaseB += dt * hz * 2.0;
                return (Math.Sin(2 * Math.PI * phaseA) * 0.6 + Math.Sin(2 * Math.PI * phaseB) * 0.25) * 0.035 * (1 + 0.1 * drift.Next(dt));
            }

            //Running: a steady, low hum with a few soft harmonics, rounded off and drifting very slightly
            public double Hum(double t, double hz)
            {
                humPhase += dt * hz * (1 + 0.002 * wander.Next(dt));
                double h = Math.Sin(2 * Math.PI * humPhase) + 0.4 * Math.Sin(4 * Math.PI * humPhase) + 0.18 * Math.Sin(6 * Math.PI * humPhase) + 0.08 * Math.Sin(8 * Math.PI * humPhase);
                humTone.Run(h, 260, 0.7);
                return humTone.Low * 0.1 * (1 + 0.06 * drift.Next(dt));
            }

            //A slow 3D printer over the hum: long, low moves with long rests between, and a layer change now and then
            public double FabricatorWork(double t)
            {
                double v = 0;

                //Layer change: the head rests, the bed steps down slowly with a soft knock, and printing carries on
                if (t >= nextLayer)
                {
                    layerAt = t;
                    nextLayer = t + 10.0 + moves.NextDouble() * 3.0;
                    moveEnd = Math.Min(moveEnd, t);
                    pauseEnd = t + 2.2;
                }
                v += Move(bed, t, layerAt + 0.4, layerAt + 1.4, 41, 0.18);
                v += Knock(t, layerAt + 1.4, 40, 0.12);

                //Moves: a speed from the scale, a long unhurried length, usually a rest after
                if (t >= pauseEnd)
                {
                    moveStart = t;
                    moveEnd = t + 0.6 + Math.Pow(moves.NextDouble(), 1.3) * 1.6;
                    moveSpeed = Speeds[moves.Next(Speeds.Length)];
                    pauseEnd = moveEnd + (moves.NextDouble() < 0.85 ? 0.6 + moves.NextDouble() * 1.4 : 0.05);
                }
                v += Move(head, t, moveStart, moveEnd, moveSpeed, 0.17);

                //Cooling fan, steady and soft
                a.Run(Noise(), 1100, 0.7);
                return v + a.Band * 0.022 * (1 + 0.15 * drift.Next(dt));
            }

            //Printing in reverse over the hum: a long, low pass out across the item with a soft hiss as it comes apart,
            //a return, a row advance, and a rest. One pass to a loop, so it repeats cleanly.
            public double RecyclerWork(double t)
            {
                double n = Noise();
                double cycle = t % 12.0;
                double v = 0;

                //Scanning out, slow and steady, with a soft hiss of the item coming apart under the head
                v += Move(head, cycle, 0.6, 6.2, 49 * (1 + 0.008 * drift.Next(dt)), 0.22);
                if (cycle >= 0.6 && cycle < 6.2)
                {
                    b.Run(n, 300 * (1 + 0.1 * wander.Next(dt)), 0.9);
                    v += b.Band * 0.045 * Ramp(cycle, 0.6, 6.2);
                }

                //Returning, empty and a little quicker
                v += Move(head, cycle, 6.8, 8.4, 73, 0.15);

                //Stepping on to the next row, a short slow move ending in a soft knock, then a rest
                v += Move(bed, cycle, 8.9, 9.5, 37, 0.17);
                v += Knock(cycle, 9.5, 36, 0.11);

                d.Run(n, 1000, 0.7);
                return v + d.Band * 0.02 * (1 + 0.15 * drift.Next(dt));
            }

            //Starting: the hum swells up from nothing, with one slow, quiet move as the head wakes. The working loop
            //comes in under it as it ends.
            public double Start(double t, double humHz, double moveSpeed)
            {
                double u = Math.Min(t / StartSeconds, 1.0);
                double swell = u * u * (3 - 2 * u);
                double fade = 1.0 - Math.Max(0.0, (t - (StartSeconds - 0.3)) / 0.3);
                return Hum(t, humHz * (0.6 + 0.4 * swell)) * swell * fade + Move(head, t, 0.5, 1.2, moveSpeed, 0.1) * fade;
            }

            //An item gone: a soft low thump, and a hiss falling slowly away as the last of it goes
            public double RecyclerFinish(double t)
            {
                phaseA += dt * (34 + 30 * Math.Exp(-t * 12));
                double v = Math.Sin(2 * Math.PI * phaseA) * Math.Exp(-t * 5) * Math.Min(t / 0.008, 1) * 0.26;
                b.Run(Noise(), 220 + 900 * Math.Exp(-t * 2.5), 1.0);
                return v + b.Band * Math.Exp(-t * 2.4) * Math.Min(t / 0.06, 1) * 0.14;
            }

            //An item done: the bed lowers a little and the part is let go with a soft, low clack
            public double FabricatorFinish(double t)
            {
                double v = Move(bed, t, 0.0, 0.5, 49, 0.14);
                double s = t - 0.55;
                if (s >= 0)
                {
                    a.Run(Noise(), 700, 0.8);
                    v += a.Band * Math.Exp(-s * 35) * Math.Min(s / 0.003, 1) * 0.26;
                    phaseA += dt * 55;
                    v += Math.Sin(2 * Math.PI * phaseA) * Math.Exp(-s * 15) * 0.14;
                }
                return v;
            }
        }
    }
}
