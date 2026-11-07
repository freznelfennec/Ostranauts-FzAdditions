using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;

namespace Freznel.FzAdditions.UI
{
    //A recycler's or fabricator's sound, played from where the machine is, the way the game's own machines are: a 3D
    //source with the game's falloff curve and range, through the Ambient mixer group. A faint hum while it's powered
    //and waiting, a working loop while it runs, a start and a finish on top, and a wind-down when it stops. Each
    //machine plays slightly sharp or flat and from its own place in the loop, so a room of them doesn't drone in
    //unison. The clips are rendered once, as the plugin loads, and shared.
    //
    //Sound is only what's heard from the machine's state, so nothing here is saved. FzAreaProjector makes and drives
    //one for each machine it shows, and drops them when the scene empties.
    public class FzMachineSound : MonoBehaviour
    {
        public enum State { Off, Idle, Working }

        private const string MixerGroupName = "Ambient"; //The game's ambient volume, as its machines use
        private const string FalloffCurve = "FalloffLogShallowMin20Max500"; //The CO2 scrubber's falloff and range
        private const float MinDistance = 20f, MaxDistance = 50f;
        private const float Volume = 0.6f;
        private const float PitchSpread = 0.03f; //Each machine up to this much sharp or flat
        private const float FadeTime = 0.4f; //Seconds for a loop to fade in or out
        private const float WindDownTime = 1f; //Seconds the working loop slows and fades when the machine stops
        private const float WindDownPitch = 0.4f; //How far it slows by the end
        private const float WorkAfterStart = (float)MachineSounds.StartSeconds - 0.15f; //The loop comes in as the start ends
        private const float LingerTime = 2.5f; //Seconds it keeps sounding as working after a run, so runs back to back sound like one

        private static readonly Dictionary<MachineSounds.Clip, AudioClip> clips = new Dictionary<MachineSounds.Clip, AudioClip>();

        //Renders every clip at the output's rate, once. Called as the plugin loads, so it doesn't hitch the game later.
        public static void Prepare()
        {
            if (clips.Count > 0) return;
            int rate = AudioSettings.outputSampleRate;
            foreach (MachineSounds.Clip clip in Enum.GetValues(typeof(MachineSounds.Clip)))
            {
                float[] data = MachineSounds.Render(clip, rate);
                AudioClip audio = AudioClip.Create("Fz" + clip, data.Length, 1, rate, false);
                audio.SetData(data, 0);
                clips[clip] = audio;
            }
        }

        private static AudioClip Get(MachineSounds.Clip clip)
        {
            Prepare();
            return clips[clip];
        }

        private AudioSource idleSource, workSource, eventSource;
        private bool fabricator;
        private float pitch, idleLevel, workLevel, workDelay, windDown = -1f;
        private float idleOffset, workOffset; //Where in its loop this machine starts each time
        private float lingerLeft;
        private State state = State.Off;
        private string lastItem;
        private double lastProgress;
        private bool paused;

        public static FzMachineSound Create(CondOwner machine)
        {
            GameObject obj = new GameObject("FzMachineSound_" + machine.strID);
            FzMachineSound sound = obj.AddComponent<FzMachineSound>();
            sound.fabricator = Fabricator.IsFabricator(machine);

            //The same machine always plays at the same pitch and from the same place in its loop
            int hash = machine.strID.GetHashCode();
            System.Random random = new System.Random(hash);
            sound.pitch = 1f + ((float)random.NextDouble() * 2f - 1f) * PitchSpread;

            sound.idleSource = sound.AddSource(Get(sound.fabricator ? MachineSounds.Clip.FabricatorIdle : MachineSounds.Clip.RecyclerIdle), true);
            sound.workSource = sound.AddSource(Get(sound.fabricator ? MachineSounds.Clip.FabricatorWork : MachineSounds.Clip.RecyclerWork), true);
            sound.eventSource = sound.AddSource(null, false);
            sound.idleOffset = (float)(random.NextDouble() * sound.idleSource.clip.length);
            sound.workOffset = (float)(random.NextDouble() * sound.workSource.clip.length);
            return sound;
        }

        private AudioSource AddSource(AudioClip clip, bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.volume = loop ? 0f : Volume;
            source.pitch = pitch;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.minDistance = MinDistance;
            source.maxDistance = MaxDistance;

            AnimationCurveAsset curve = Resources.Load<AnimationCurveAsset>("Curves/" + FalloffCurve);
            if (curve != null)
            {
                source.rolloffMode = AudioRolloffMode.Custom;
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve.curve);
            }
            else source.rolloffMode = AudioRolloffMode.Logarithmic;

            if (AudioManager.am != null && AudioManager.MixerGroups.TryGetValue(MixerGroupName, out AudioMixerGroup group)) source.outputAudioMixerGroup = group;
            return source;
        }

        //Each frame, from the machine's state. Item and progress are its current run, so a finished item can be heard:
        //the item changing, or a stack's progress starting over for its next unit.
        public void Follow(Transform machine, State next, string item, double progress)
        {
            if (machine != null) transform.position = machine.position;
            FollowPause();

            //A short gap between runs keeps sounding as working, so the next run doesn't wind down and start up again.
            //Off is heard at once.
            State heard = next;
            if (next == State.Working) lingerLeft = LingerTime;
            else if (next == State.Idle && state == State.Working && lingerLeft > 0f)
            {
                heard = State.Working;
                if (!paused) lingerLeft -= Time.unscaledDeltaTime;
            }

            if (heard == State.Working && state == State.Working)
            {
                bool itemDone = lastItem != null && item != lastItem;
                bool unitDone = item != null && item == lastItem && progress < lastProgress - 0.001;
                if (itemDone || unitDone) PlayEvent(fabricator ? MachineSounds.Clip.FabricatorFinish : MachineSounds.Clip.RecyclerFinish);
            }
            if (heard != state) ChangeState(heard);
            lastItem = item;
            lastProgress = progress;

            if (!paused) Mix(Time.unscaledDeltaTime);
        }

        private void ChangeState(State next)
        {
            if (state == State.Working) windDown = 0f;
            if (next == State.Working)
            {
                PlayEvent(fabricator ? MachineSounds.Clip.FabricatorStart : MachineSounds.Clip.RecyclerStart);
                workDelay = WorkAfterStart;
                windDown = -1f;
            }
            state = next;
        }

        private void PlayEvent(MachineSounds.Clip clip)
        {
            if (paused) return;
            eventSource.PlayOneShot(Get(clip), 1f);
        }

        //The game frozen, so is the machine: everything holds where it is until the game runs again
        private void FollowPause()
        {
            bool gamePaused = CrewSim.Paused;
            if (gamePaused == paused) return;
            paused = gamePaused;
            foreach (AudioSource source in new[] { idleSource, workSource, eventSource })
            {
                if (paused) source.Pause(); else source.UnPause();
            }
        }

        private void Mix(float dt)
        {
            float step = dt / FadeTime;
            idleLevel = Mathf.MoveTowards(idleLevel, state == State.Idle && windDown < 0f ? 1f : 0f, step);

            if (state == State.Working)
            {
                workDelay -= dt;
                workLevel = workDelay > 0f ? 0f : Mathf.MoveTowards(workLevel, 1f, step);
                workSource.pitch = pitch;
            }
            else if (windDown >= 0f)
            {
                //Slowing and fading, then the idle hum comes back in
                windDown += dt;
                float u = Mathf.Clamp01(windDown / WindDownTime);
                workSource.pitch = pitch * (1f - WindDownPitch * u);
                workLevel = Mathf.Min(workLevel, (1f - u) * (1f - u));
                if (u >= 1f) windDown = -1f;
            }
            else workLevel = 0f;

            SetLoop(idleSource, idleLevel, idleOffset);
            SetLoop(workSource, workLevel, workOffset);
        }

        //A silent loop is stopped rather than left playing, so a ship of switched off machines costs nothing
        private static void SetLoop(AudioSource source, float level, float offset)
        {
            source.volume = level * Volume;
            if (level > 0f && !source.isPlaying)
            {
                source.time = offset;
                source.Play();
            }
            else if (level <= 0f && source.isPlaying) source.Stop();
        }
    }
}
