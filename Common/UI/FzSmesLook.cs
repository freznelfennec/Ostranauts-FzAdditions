using Freznel.FzAdditions.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace Freznel.FzAdditions.UI
{
    //Shows an installed unit's state on its sprite. Each look is a COOverlay of the unit, only its images and
    //animation are used, so the unit stays the CO it is. The look is worked out from the unit's state each frame,
    //nothing about it needs saving. The unit's one light sprite carries the look's emissive layer, and follows the
    //sprite's frame when the charging looks animate. A light sprite draws its whole texture, so their glow is an image
    //for each frame rather than a sheet. Discharging flashes its part charged pip by switching between still looks
    //itself, in real time, the game's animation broke when the look changed as quickly as a discharge changes it.
    public class FzSmesLook : MonoBehaviour
    {
        private const string GlowFrameSuffix = "_f";

        //Private to the game, a game update could rename it. Without it the animated looks can't be cleared away, so
        //charging shows as idle instead.
        private static readonly MethodInfo AnimationUpdate = AccessTools.Method(typeof(Item), "OnItemAnimationUpdate");
        private static bool warnedNoAnimation;

        private Item item;
        private UnityAction animationUpdate; //Null if the game's method wasn't found
        private string look;
        private string glowImage;
        private bool glowFrames; //The glow has an image for each of the sprite's frames
        private int glowFrame = -1;

        //As the unit is built, and from its power updates, which run whether or not it is on. Building covers a load,
        //whose power updates wait for the game to unpause. The look goes with the item.
        public static void Attach(CondOwner co)
        {
            if (co == null || co.strName != SmesConstants.UnitCO || co.Item == null || co.GetComponent<FzSmesLook>() != null) return;

            FzSmesLook unitLook = co.gameObject.AddComponent<FzSmesLook>();
            unitLook.item = co.Item;
            if (AnimationUpdate != null) unitLook.animationUpdate = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), co.Item, AnimationUpdate);
            else if (!warnedNoAnimation)
            {
                warnedNoAnimation = true;
                FzAdditions.Logger.LogWarning("Item.OnItemAnimationUpdate not found, SMES units show charging as idle");
            }
        }

        //After the items' animations have moved on for the frame. A unit changed to another CO keeps the look it has.
        private void LateUpdate()
        {
            CondOwner co = item != null ? item.CO : null;
            if (co == null || co.strName != SmesConstants.UnitCO) return;

            bool flashOn = Time.unscaledTime * SmesConstants.FlashRate % 1f < 0.5f;
            string wanted = GetLook(co, animationUpdate != null, flashOn);
            if (wanted != look && Apply(wanted)) look = wanted;
            UpdateGlow();
        }

        //Charging shows the pip being filled blinking. Idle and discharging light their full pips and show one holding
        //part of its charge unlit, discharging flashes it with the look a half pip lower.
        private static string GetLook(CondOwner co, bool canAnimate, bool flashOn)
        {
            //Before its first power update a unit shows its charge, nothing yet says whether it is on
            if (!Smes.IsOn(co) && Smes.HasRun(co)) return SmesConstants.LookOff;

            double capacity = Smes.GetCapacity(co);
            double share = capacity > 0.0 ? Smes.GetCharge(co) / capacity : 0.0;
            int filled = Mathf.Clamp((int)Math.Floor(share * SmesConstants.Levels), 0, SmesConstants.Levels);
            bool part = filled < SmesConstants.Levels && share * SmesConstants.Levels - filled > 1E-06;
            int half = filled * 2 + (part ? 1 : 0);
            if (Smes.IsDischarging(co)) return SmesConstants.LookDischarge + (part && !flashOn ? half - 1 : half);

            if (Smes.IsFull(co)) return SmesConstants.LookIdle + SmesConstants.HalfLevels;
            if (canAnimate && Smes.GetCurrentRate(co) > 0.0)
            {
                return (Smes.IsTapCharging(co) ? SmesConstants.LookFast : SmesConstants.LookGrid) + Math.Min(filled, SmesConstants.Levels - 1);
            }
            return SmesConstants.LookIdle + half;
        }

        //The game adds an animation's update each time one is set up and only takes it away when the item goes, so a
        //look changing would otherwise leave the last one running over it
        private bool Apply(string name)
        {
            JsonCOOverlay overlay = DataHandler.GetCOOverlay(name);
            if (overlay == null) return false;

            if (animationUpdate != null) Item.ItemAnimationUpdate.RemoveListener(animationUpdate);
            item.SetAlt(overlay.strImg, overlay.strImgNorm, overlay.strImgDamaged, overlay.strDmgColor, overlay.objAnimation);

            glowImage = overlay.strImg + SmesConstants.GlowSuffix;
            glowFrames = overlay.objAnimation != null;
            glowFrame = -1;
            return true;
        }

        private void UpdateGlow()
        {
            Renderer glow = GetGlow();
            if (glow == null || glowImage == null) return;

            int frame = glowFrames ? item.SpriteSheetIndex : 0;
            if (frame == glowFrame) return;

            glow.sharedMaterial = DataHandler.GetMaterial(glow, glowFrames ? glowImage + GlowFrameSuffix + frame : glowImage);
            glowFrame = frame;
        }

        private Renderer GetGlow()
        {
            return item.dictLightSprites?.Keys.FirstOrDefault(tf => tf != null)?.GetComponent<Renderer>();
        }
    }
}
