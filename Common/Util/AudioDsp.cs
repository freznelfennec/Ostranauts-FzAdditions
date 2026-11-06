using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Util
{
    //Small building blocks for sound synthesised as it plays. Each one runs a sample at a time on the audio thread.
    public static class AudioDsp
    {
        public static double Clamp01(double x)
        {
            return x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x;
        }

        public static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        //From a phase in cycles, -1 to 1
        public static double Saw(double phase)
        {
            return 2.0 * (phase - Math.Floor(phase)) - 1.0;
        }

        //Smooth random wander between -1 and 1, toward a new target every period
        public class Drift
        {
            private readonly Random random;
            private readonly double period;
            private double from, to, position;

            public Drift(int seed, double period)
            {
                random = new Random(seed);
                this.period = period;
                from = random.NextDouble() * 2.0 - 1.0;
                to = random.NextDouble() * 2.0 - 1.0;
            }

            public double Next(double dt)
            {
                position += dt / period;
                if (position >= 1.0)
                {
                    position -= 1.0;
                    from = to;
                    to = random.NextDouble() * 2.0 - 1.0;
                }
                double eased = position * position * (3.0 - 2.0 * position);
                return Lerp(from, to, eased);
            }
        }

        //State variable filter. Q from about 0.5, soft, to 10, ringing.
        public class Svf
        {
            private readonly int rate;
            private double low, band;

            public double Low { get; private set; }
            public double Band { get; private set; }
            public double High { get; private set; }

            public Svf(int rate)
            {
                this.rate = rate;
            }

            public void Run(double x, double cutoff, double q)
            {
                double f = 2.0 * Math.Sin(Math.PI * Math.Min(cutoff, rate / 6.0) / rate);
                low += f * band;
                double high = x - low - band / q;
                band += f * high;
                Low = low;
                Band = band;
                High = high;
            }
        }

        //Feedback comb tuned to a pitch, so noise through it rings at that pitch's harmonics
        public class Comb
        {
            private readonly int rate;
            private readonly double[] buffer = new double[8192];
            private int write;

            public Comb(int rate)
            {
                this.rate = rate;
            }

            public double Run(double x, double pitch, double feedback)
            {
                double delay = Math.Min(rate / Math.Max(pitch, 1.0), buffer.Length - 2);
                double read = write - delay;
                while (read < 0.0) read += buffer.Length;
                int index = (int)read;
                double fraction = read - index;
                double y = buffer[index % buffer.Length] * (1.0 - fraction) + buffer[(index + 1) % buffer.Length] * fraction;
                double output = x + y * feedback;
                buffer[write] = output;
                write = (write + 1) % buffer.Length;
                return output;
            }
        }

        //A small room: four damped combs into two allpasses. Spread offsets the lengths, so the sides differ.
        public class Reverb
        {
            private static readonly int[] CombLengths = { 1557, 1617, 1491, 1422 }; //At 44.1 kHz
            private static readonly int[] AllpassLengths = { 225, 556 };

            private readonly double[][] combs;
            private readonly int[] combIndex;
            private readonly double[] combFilter;
            private readonly double[][] allpasses;
            private readonly int[] allpassIndex;

            public Reverb(int rate, double size, int spread)
            {
                double scale = rate / 44100.0;
                combs = CombLengths.Select(length => new double[(int)((length * size + spread) * scale)]).ToArray();
                combIndex = new int[combs.Length];
                combFilter = new double[combs.Length];
                allpasses = AllpassLengths.Select(length => new double[(int)((length + spread / 3) * scale)]).ToArray();
                allpassIndex = new int[allpasses.Length];
            }

            public double Run(double x, double feedback, double damp)
            {
                double output = 0.0;
                for (int i = 0; i < combs.Length; i++)
                {
                    double y = combs[i][combIndex[i]];
                    combFilter[i] = y * (1.0 - damp) + combFilter[i] * damp;
                    combs[i][combIndex[i]] = x + combFilter[i] * feedback;
                    combIndex[i] = (combIndex[i] + 1) % combs[i].Length;
                    output += y;
                }
                output *= 0.25;

                for (int i = 0; i < allpasses.Length; i++)
                {
                    double b = allpasses[i][allpassIndex[i]];
                    allpasses[i][allpassIndex[i]] = output + b * 0.5;
                    allpassIndex[i] = (allpassIndex[i] + 1) % allpasses[i].Length;
                    output = b - output * 0.5;
                }
                return output;
            }
        }
    }
}
