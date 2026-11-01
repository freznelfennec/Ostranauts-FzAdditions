using Freznel.FzAdditions.Util;
using UnityEngine;

namespace Freznel.FzAdditions.UI.Controls
{
    public enum LampColor { White, Red, Amber, Yellow, Green, Blue }

    public static class LampColors
    {
        public static Color ToColor(this LampColor color)
        {
            switch (color)
            {
                case LampColor.Red: return SpriteUtil.ColorFromHex("#FF4030FF");
                case LampColor.Amber: return SpriteUtil.ColorFromHex("#FFB000FF");
                case LampColor.Yellow: return SpriteUtil.ColorFromHex("#FFD800FF");
                case LampColor.Green: return SpriteUtil.ColorFromHex("#40E060FF");
                case LampColor.Blue: return SpriteUtil.ColorFromHex("#40A0FFFF");
                default: return SpriteUtil.ColorFromHex("#FFF9EFFF");
            }
        }
    }
}
