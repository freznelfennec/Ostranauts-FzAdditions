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
    //Shows an installed cell's state on its sprite. Each look is a COOverlay of the cell, only its images and
    //animation are used, so the cell stays the CO it is. The look is worked out from the cell's state each frame,
    //nothing about it needs saving. The cell's one light sprite carries the look's emissive layer, and follows the
    //sprite's frame when the charging looks animate. A light sprite draws its whole texture, so their glow is an image
    //for each frame rather than a sheet. Discharging flashes its part charged pip by switching between still looks
    //itself, in real time, the game's animation broke when the look changed as quickly as a discharge changes it.
    public class FzCellLook : MonoBehaviour
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

        //As the cell is built, and from its power updates, which run whether or not it is on. Building covers a load,
        //whose power updates wait for the game to unpause. The look goes with the item.
        public static void Attach(CondOwner co)
        {
            if (co == null || co.strName != Constants.CellCO || co.Item == null || co.GetComponent<FzCellLook>() != null) return;

            FzCellLook cellLook = co.gameObject.AddComponent<FzCellLook>();
            cellLook.item = co.Item;
            if (AnimationUpdate != null) cellLook.animationUpdate = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), co.Item, AnimationUpdate);
            else if (!warnedNoAnimation)
            {
                warnedNoAnimation = true;
                FzAdditions.Logger.LogWarning("Item.OnItemAnimationUpdate not found, jump cells show charging as idle");
            }
        }

        //After the items' animations have moved on for the frame. A cell changed to another CO keeps the look it has.
        private void LateUpdate()
        {
            CondOwner co = item != null ? item.CO : null;
            if (co == null || co.strName != Constants.CellCO) return;

            bool flashOn = Time.unscaledTime * Constants.CellFlashRate % 1f < 0.5f;
            string wanted = GetLook(co, animationUpdate != null, flashOn);
            if (wanted != look && Apply(wanted)) look = wanted;
            UpdateGlow();
        }

        //Charging shows the pip being filled blinking. Idle and discharging light their full pips and show one holding
        //part of its charge unlit, discharging flashes it with the look a half pip lower.
        private static string GetLook(CondOwner co, bool canAnimate, bool flashOn)
        {
            //Before its first power update a cell shows its charge, nothing yet says whether it is on
            if (!JumpCell.IsOn(co) && JumpCell.HasRun(co)) return Constants.CellLookOff;

            double capacity = JumpCell.GetCapacity(co);
            double share = capacity > 0.0 ? JumpCell.GetCharge(co) / capacity : 0.0;
            int filled = Mathf.Clamp((int)Math.Floor(share * Constants.CellLevels), 0, Constants.CellLevels);
            bool part = filled < Constants.CellLevels && share * Constants.CellLevels - filled > 1E-06;
            int half = filled * 2 + (part ? 1 : 0);
            if (FzJumpEffect.IsCharging(co.ship)) return Constants.CellLookDischarge + (part && !flashOn ? half - 1 : half);

            if (JumpCell.IsFull(co)) return Constants.CellLookIdle + Constants.CellHalfLevels;
            if (canAnimate && JumpCell.GetCurrentRate(co) > 0.0)
            {
                return (JumpCell.IsTapCharging(co) ? Constants.CellLookFast : Constants.CellLookGrid) + Math.Min(filled, Constants.CellLevels - 1);
            }
            return Constants.CellLookIdle + half;
        }

        //The game adds an animation's update each time one is set up and only takes it away when the item goes, so a
        //look changing would otherwise leave the last one running over it
        private bool Apply(string name)
        {
            JsonCOOverlay overlay = DataHandler.GetCOOverlay(name);
            if (overlay == null) return false;

            if (animationUpdate != null) Item.ItemAnimationUpdate.RemoveListener(animationUpdate);
            item.SetAlt(overlay.strImg, overlay.strImgNorm, overlay.strImgDamaged, overlay.strDmgColor, overlay.objAnimation);

            glowImage = overlay.strImg + Constants.CellGlowSuffix;
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
