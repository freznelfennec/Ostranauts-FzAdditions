using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
using static Freznel.FzAdditions.Util.AudioDsp;
using Random = System.Random;

namespace Freznel.FzAdditions.UI
{
    //The jump sequence's sound, synthesised as it plays so it keeps step with the charge at any game speed.
    //FzJumpEffect drives it: the charge's progress each frame, a pulse as each wave lands, then the hit or a
    //power-down. Past the hit the charge carries on under it and spins down as the field and emitters fade.
    //It outlives the effect, so a hit's thunder or a power-down plays out after the effect has gone.
    //Nothing here is saved, a load silences it and a charge that starts again starts it again.
    public class FzJumpAudio : MonoBehaviour
    {
        public const float SpinUpTime = 1f; //Game seconds the charge takes to spin up, the emitter lead and a little after
        private const string MixerGroupName = "PrefsEffects"; //The game's sound effects volume
        private const float Volume = 0.45f;

        private static FzJumpAudio instance;

        private AudioSource source;
        private Synth synth;
        private bool paused;

        //Written on the main thread and read on the audio thread
        private volatile float spin;
        private volatile float progress;
        private volatile float landingIn = float.MaxValue;
        private volatile float fieldLevel = 1f;
        private volatile float emitterLevel = 1f;
        private volatile bool finished = true;
        private int generation, waveCount, hitCount, downCount;

        //A charge has started
        public static void Begin()
        {
            FzJumpAudio audio = Get();
            if (audio == null) return;

            audio.spin = 0f;
            audio.progress = 0f;
            audio.landingIn = float.MaxValue;
            audio.fieldLevel = 1f;
            audio.emitterLevel = 1f;
            Interlocked.Increment(ref audio.generation);
            audio.finished = false;
            audio.paused = false;
            audio.source.Play();
        }

        //Spin and progress run from 0 to 1, landingIn is game seconds until the last wave lands
        public static void SetCharge(float spin, float progress, float landingIn)
        {
            if (instance == null) return;
            instance.spin = spin;
            instance.progress = progress;
            instance.landingIn = landingIn;
        }

        //After the hit, how much of the field and the emitters is left, 1 at the flash and 0 once each has faded.
        //The charge spins down with them.
        public static void SetRelease(float field, float emitters)
        {
            if (instance == null) return;
            instance.fieldLevel = field;
            instance.emitterLevel = emitters;
        }

        public static void WaveLanded()
        {
            if (instance != null) Interlocked.Increment(ref instance.waveCount);
        }

        public static void Hit()
        {
            if (instance != null) Interlocked.Increment(ref instance.hitCount);
        }

        //Only a charge still building powers down, a hit already under way plays out
        public static void PowerDown()
        {
            if (instance != null) Interlocked.Increment(ref instance.downCount);
        }

        //Cuts it off at once, as a save loads
        public static void Silence()
        {
            if (instance == null) return;
            instance.finished = true;
            instance.source.Stop();
        }

        private static FzJumpAudio Get()
        {
            if (instance != null) return instance;

            GameObject go = new GameObject("FzJumpAudio");
            DontDestroyOnLoad(go);
            int rate = AudioSettings.outputSampleRate;

            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.clip = AudioClip.Create("FzJumpSilence", rate, 2, rate, false); //Carries the synthesis, which is added on top
            if (AudioManager.am != null && AudioManager.MixerGroups.TryGetValue(MixerGroupName, out AudioMixerGroup group)) source.outputAudioMixerGroup = group;

            instance = go.AddComponent<FzJumpAudio>();
            instance.source = source;
            instance.synth = new Synth(rate);
            return instance;
        }

        //Follows the game's pause, and stops the source once the sound has played out
        private void Update()
        {
            if (source == null) return;
            if (finished)
            {
                if (source.isPlaying) source.Stop();
                paused = false;
                return;
            }

            bool gamePaused = CrewSim.Paused;
            if (gamePaused && !paused)
            {
                source.Pause();
                paused = true;
            }
            else if (!gamePaused && paused)
            {
                source.UnPause();
                paused = false;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (finished || synth == null) return;

            synth.Sync(Volatile.Read(ref generation), Volatile.Read(ref waveCount), Volatile.Read(ref hitCount), Volatile.Read(ref downCount), spin, progress, landingIn, fieldLevel, emitterLevel);
            int frames = data.Length / channels;
            for (int i = 0; i < frames; i++)
            {
                synth.Next(out double left, out double right);
                data[i * channels] += (float)(left * Volume);
                if (channels > 1) data[i * channels + 1] += (float)(right * Volume);
            }
            if (synth.Done) finished = true;
        }

        //Everything here runs on the audio thread. Times are real seconds, the charge's place comes from the effect.
        private class Synth
        {
            private enum Mode { Charge, Hit, Down, Done }

            private const double MinPulseGap = 1.0 / 12.0; //Pulses closer than this blur into the charge, fast forwarded
            private const double DownTime = 0.6; //The same as the effect's fade
            private const double HitLength = 1.8;
            private const double DownTail = 0.8; //Real seconds of the room ringing on once the power-down is over
            private const double HitDuck = 0.6; //How far the charge drops under the hit, so the hit stands out
            private const double HitDuckRecovery = 0.35; //Seconds for the charge to come most of the way back
            private const double ReleaseTail = 0.6; //Seconds of the room ringing on once the field has gone

            private readonly int rate;
            private readonly double dt;
            private int seenGeneration = -1, seenWaves, seenHits, seenDowns;
            private Mode mode = Mode.Done;
            private double targetSpin, targetProgress, targetLanding, targetField, targetEmitters;
            private double spin, progress, landing, field, emitters;
            private double age, pulseAt, pulseSide, hitAge, downAge, quietAge;

            private Random noise, noiseR, crackle;
            private Drift wobbleDrift, slowDrift, flutterDrift;
            private Svf[] bodyTone, formant, formant2, spinBand, air, swell, clap, rumble;
            private Svf popTone;
            private Comb[] tuned;
            private Reverb[] room;
            private double[] voice;
            private double sub, spinA, spinB, spinTrem, thump, grainAt, grainSide, grainLevel;

            private static readonly double[] Detune = { -0.013, -0.006, -0.001, 0.002, 0.007, 0.012 };

            public bool Done => mode == Mode.Done;

            public Synth(int rate)
            {
                this.rate = rate;
                dt = 1.0 / rate;
            }

            private Svf[] Pair()
            {
                return new[] { new Svf(rate), new Svf(rate) };
            }

            private void Reset()
            {
                noise = new Random(3); noiseR = new Random(11); crackle = new Random(19);
                wobbleDrift = new Drift(1, 0.7); slowDrift = new Drift(2, 1.3); flutterDrift = new Drift(3, 0.23);
                bodyTone = Pair(); formant = Pair(); formant2 = Pair(); spinBand = Pair(); air = Pair(); swell = Pair(); clap = Pair(); rumble = Pair();
                popTone = new Svf(rate);
                tuned = new[] { new Comb(rate), new Comb(rate) };
                room = new[] { new Reverb(rate, 1.1, 0), new Reverb(rate, 1.1, 23) };
                voice = new double[Detune.Length];
                sub = spinA = spinB = spinTrem = thump = 0.0;
                grainAt = -1.0;
                spin = progress = 0.0;
                field = emitters = 1.0;
                landing = double.MaxValue;
                age = hitAge = downAge = quietAge = 0.0;
                pulseAt = -10.0;
                pulseSide = 1.0;
                mode = Mode.Charge;
            }

            public void Sync(int generation, int waves, int hits, int downs, float spin, float progress, float landingIn, float field, float emitters)
            {
                if (generation != seenGeneration)
                {
                    seenGeneration = generation;
                    seenWaves = waves; seenHits = hits; seenDowns = downs;
                    Reset();
                }
                if (waves != seenWaves)
                {
                    seenWaves = waves;
                    if (mode == Mode.Charge && age - pulseAt >= MinPulseGap)
                    {
                        pulseAt = age;
                        pulseSide = -pulseSide;
                    }
                }
                if (hits != seenHits)
                {
                    seenHits = hits;
                    if (mode == Mode.Charge) { mode = Mode.Hit; hitAge = 0.0; }
                }
                if (downs != seenDowns)
                {
                    seenDowns = downs;
                    if (mode == Mode.Charge) { mode = Mode.Down; downAge = 0.0; }
                }
                targetSpin = spin;
                targetProgress = progress;
                targetLanding = landingIn;
                targetField = field;
                targetEmitters = emitters;
            }

            public void Next(out double left, out double right)
            {
                left = right = 0.0;
                if (mode == Mode.Done) return;

                age += dt;
                double smooth = Math.Min(dt / 0.03, 1.0);
                spin += (targetSpin - spin) * smooth;
                progress += (targetProgress - progress) * smooth;
                field += (targetField - field) * smooth;
                emitters += (targetEmitters - emitters) * smooth;
                landing = targetLanding;

                double wobble = wobbleDrift.Next(dt), slow = slowDrift.Next(dt), flutter = flutterDrift.Next(dt);
                double nz = noise.NextDouble() * 2.0 - 1.0, nzR = noiseR.NextDouble() * 2.0 - 1.0;
                double l = 0.0, r = 0.0;

                if (mode == Mode.Charge || mode == Mode.Down)
                {
                    double drop = 1.0;
                    //The power-down keeps step with the effect's fade, both in real time, then the room rings out
                    if (mode == Mode.Down)
                    {
                        downAge += dt;
                        drop = Math.Max(0.0, 1.0 - downAge / DownTime);
                        quietAge = drop <= 0.0 ? quietAge + dt : 0.0;
                        if (quietAge > DownTail) mode = Mode.Done;
                    }
                    if (drop > 0.0) Charge(drop, drop, wobble, slow, flutter, nz, nzR, ref l, ref r);
                }
                else if (mode == Mode.Hit)
                {
                    //The charge carries on through the flash and winds down with the field and emitters
                    hitAge += dt;
                    if (field > 0.001 || emitters > 0.001) Charge(field, emitters, wobble, slow, flutter, nz, nzR, ref l, ref r);
                    HitLayers(slow, nz, nzR, ref l, ref r);

                    quietAge = field <= 0.001 && emitters <= 0.001 ? quietAge + dt : 0.0;
                    if (hitAge > HitLength && quietAge > ReleaseTail) mode = Mode.Done;
                }

                double wet = mode == Mode.Hit ? 0.35 : 0.25;
                l += room[0].Run(l, 0.76, 0.4) * wet;
                r += room[1].Run(r, 0.76, 0.4) * wet;
                left = Math.Tanh(l * 0.6) / 0.6;
                right = Math.Tanh(r * 0.6) / 0.6;
            }

            //The charge building, or winding down with drop falling from 1 to 0, the emitters on their own drop. The
            //spin-up is the wind-down's curves played forwards. After the hit the charge carries on under it and
            //winds down as the field fades, ducked as the hit lands so the hit stands out.
            private void Charge(double drop, double emitterDrop, double wobble, double slow, double flutter, double nz, double nzR, ref double l, ref double r)
            {
                double p = progress;
                double spinPitch = Lerp(0.3, 1.0, spin * spin) * Lerp(0.3, 1.0, drop * drop);
                double level = Math.Sqrt(spin) * Math.Sqrt(drop);
                double bright = Lerp(0.25, 1.0, spin) * Lerp(0.25, 1.0, drop);
                double upper = spin * drop; //The whine and noise come in after the body and go before it
                bool building = mode == Mode.Charge;
                double duck = mode == Mode.Hit ? 1.0 - HitDuck * Math.Exp(-hitAge / HitDuckRecovery) : 1.0;
                double root = Lerp(41, 62, p) * spinPitch * (1.0 + 0.003 * wobble);

                //Emitters: two saws on the body's fourth harmonic through a resonant band, with a turbine flutter.
                //They wind down on their own, ahead of the field.
                double emitterPitch = Lerp(0.3, 1.0, spin * spin) * Lerp(0.3, 1.0, emitterDrop * emitterDrop);
                double emitterRoot = Lerp(41, 62, p) * emitterPitch * (1.0 + 0.003 * wobble);
                spinA += dt * emitterRoot * 4.0; spinB += dt * emitterRoot * 4.0 * 1.007;
                spinTrem += dt * Lerp(7, 22, p) * emitterPitch;
                double trem = 0.7 + 0.3 * Math.Sin(2.0 * Math.PI * spinTrem);
                double band = Lerp(320, 1300, p) * Lerp(0.25, 1.0, spin) * Lerp(0.25, 1.0, emitterDrop);
                spinBand[0].Run(Saw(spinA), band, 4); spinBand[1].Run(Saw(spinB), band * 1.03, 4);
                double eg = Lerp(0.16, 0.1, p) * Math.Sqrt(spin) * Math.Sqrt(emitterDrop) * trem * duck;
                l += spinBand[0].Band * eg; r += spinBand[1].Band * eg;

                //Body: six detuned saws split between the sides, and the whine drawn out of their harmonics
                double sl = 0.0, sr = 0.0;
                for (int k = 0; k < voice.Length; k++)
                {
                    voice[k] += dt * root * (1.0 + Detune[k] * (1.0 + 0.3 * slow));
                    double s = Saw(voice[k]);
                    if (k % 2 == 0) sl += s; else sr += s;
                }
                sl /= 3.0; sr /= 3.0;
                double tone = Lerp(170, 1300, Math.Pow(p, 1.4)) * bright * (1.0 + 0.12 * wobble);
                double resonance = Lerp(1.2, 4.0, p);
                bodyTone[0].Run(sl, tone, resonance); bodyTone[1].Run(sr, tone, resonance);
                double bl = Lerp(0.14, 0.2, p) * level * duck;
                l += bodyTone[0].Low * bl; r += bodyTone[1].Low * bl;

                double f = 210 * Math.Pow(720.0 / 210.0, Math.Pow(p, 1.2)) * spinPitch * (1.0 + 0.012 * flutter);
                formant[0].Run(sl, f, 9); formant[1].Run(sr, f * 1.004, 9);
                formant2[0].Run(sl, f * 1.5, 7); formant2[1].Run(sr, f * 1.5 * 0.997, 7);
                double wl = Lerp(0.03, 0.26, Math.Pow(p, 1.4)) * level * upper * duck;
                l += (formant[0].Band + 0.45 * formant2[0].Band) * wl; r += (formant[1].Band + 0.45 * formant2[1].Band) * wl;

                sub += dt * root * 0.5;
                double sb = Math.Sin(2.0 * Math.PI * sub) * Lerp(0.12, 0.2, p) * level * duck;
                l += sb; r += sb;

                //Noise tuned to the body's pitch, ducked by each wave pulse and breathing back in before the next
                double breath = 0.35 + 0.65 * (1.0 - Math.Exp(-(age - pulseAt) * 7.0));
                double feedback = Lerp(0.6, 0.93, p);
                double tl = tuned[0].Run(nz * 0.1, root * 2.0, feedback), tr = tuned[1].Run(nzR * 0.1, root * 2.0 * 1.003, feedback);
                double cut = Lerp(350, 2200, p) * spinPitch;
                air[0].Run(tl, cut, 1.4); air[1].Run(tr, cut, 1.4);
                double ag = 0.4 * Math.Pow(p, 1.8) * breath * level * upper * duck;
                l += air[0].Band * ag; r += air[1].Band * ag;

                if (!building) return;

                //Swell into the hit over the last 0.45 game seconds
                double sw = Clamp01(1.0 - landing / 0.45);
                if (sw > 0.0)
                {
                    double opening = Lerp(300, 7000, sw * sw);
                    swell[0].Run(nz, opening, 0.8); swell[1].Run(nzR, opening, 0.8);
                    double g = Math.Pow(sw, 3) * 0.5 * (1.0 - 0.7 * Clamp01((0.04 - landing) / 0.04));
                    l += swell[0].Low * g; r += swell[1].Low * g;
                }

                //A wave landing: a thrum with a soft click, on alternate sides
                double a = age - pulseAt;
                if (a < 0.3)
                {
                    double pf = Lerp(110, 48, Clamp01(a / 0.1));
                    double thrum = Math.Sin(2.0 * Math.PI * pf * a) * Math.Exp(-a * 18.0) * Clamp01(a / 0.003);
                    double click = nz * Math.Exp(-a * 300.0) * 0.25;
                    double v = (thrum * 0.3 + click * 0.6) * Lerp(0.5, 0.85, p);
                    double pan = 0.5 + 0.25 * pulseSide;
                    l += v * (1.0 - pan) * 1.3; r += v * pan * 1.3;
                }
            }

            //A pop, then a thunderclap that crackles and rolls off within about a second
            private void HitLayers(double slow, double nz, double nzR, ref double l, ref double r)
            {
                double a = hitAge, hl = 0.0, hr = 0.0;

                thump += dt * (45 + 75 * Math.Exp(-a * 30.0));
                double th = Math.Tanh(Math.Sin(2.0 * Math.PI * thump) * 3.0) * Math.Exp(-a * 11.0) * Clamp01(a / 0.001);
                popTone.Run(nz, 1800, 0.7);
                double knock = (popTone.High * 1.6 + Math.Sign(Math.Sin(2.0 * Math.PI * 160.0 * a))) * Math.Exp(-a * 450.0);
                hl += th * 0.9 + knock * 0.7; hr += th * 0.9 + knock * 0.7;

                double cut = 600 + 9000 * Math.Exp(-a * 9.0);
                clap[0].Run(nz, cut, 0.8); clap[1].Run(nzR, cut, 0.8);
                double ce = Math.Exp(-a * 7.0) * Clamp01(a / 0.0015);
                hl += clap[0].Low * ce * 1.2; hr += clap[1].Low * ce * 1.2;

                double grainRate = 220 * Math.Exp(-a * 5.0);
                if (crackle.NextDouble() < grainRate * dt)
                {
                    grainAt = a;
                    grainSide = crackle.NextDouble() * 2.0 - 1.0;
                    grainLevel = 0.4 + 0.6 * crackle.NextDouble();
                }
                if (grainAt >= 0.0 && a - grainAt < 0.025)
                {
                    double g = nz * Math.Exp(-(a - grainAt) * 180.0) * grainLevel * Math.Exp(-a * 2.5) * 0.9;
                    hl += g * (1.0 - grainSide * 0.6); hr += g * (1.0 + grainSide * 0.6);
                }

                double roll = Lerp(260, 80, Clamp01(a / 1.0));
                rumble[0].Run(nz, roll, 0.9); rumble[1].Run(nzR, roll, 0.9);
                double re = Math.Exp(-a * 3.2) * Clamp01(a / 0.03) * 1.6;
                hl += rumble[0].Low * re * (1.0 + 0.3 * slow); hr += rumble[1].Low * re * (1.0 - 0.3 * slow);

                l += Math.Tanh(hl * 1.3) / 1.3; r += Math.Tanh(hr * 1.3) / 1.3;
            }
        }
    }
}
