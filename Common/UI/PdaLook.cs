using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI
{
    //The game's PDA look, for the mod's own PDA apps. The PDA is drawn by the game's scene, so its sprites and fonts
    //can't be loaded from Resources: they're taken off the live PDA (GUIPDA.instance) by name, the names 'fz dumpui pda'
    //lists. Null when there's no PDA (no game loaded) or the game renamed one, so callers keep a plain look to fall
    //back on.
    public static class PdaLook
    {
        public const string Frame = "GUIPDA";
        public const string Roughness = "PDA roughness";
        public const string Border3 = "BorderSliced3px";
        public const string Border4 = "BorderSliced4px";
        public const string RoundedX32 = "Rounded Corner Rect POT 9-Sliced x32";
        public const string Scanlines = "GUIScanlinealpha";
        public const string Battery = "ic_battery_60_black_48dp";
        public const string Signal = "ic_signal_cellular_2_bar_black_48dp";
        public const string Bluetooth = "ic_bluetooth_connected_black_48dp";
        public const string HomeIcon = "IcoPDAIconFlatHome";
        public const string CloseIcon = "IcoPDAIconFlatClose";

        public const string KodeMono = "KodeMono-VariableFont_wght SDF";

        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, TMP_FontAsset> fonts = new Dictionary<string, TMP_FontAsset>();

        public static Sprite Sprite(string name)
        {
            if (sprites.TryGetValue(name, out Sprite cached) && cached != null) return cached;
            if (GUIPDA.instance == null) return null;
            foreach (Image image in GUIPDA.instance.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite != null && image.sprite.name == name)
                {
                    sprites[name] = image.sprite;
                    return image.sprite;
                }
            }
            return null;
        }

        //A font the PDA uses, by its asset name
        public static TMP_FontAsset Font(string name)
        {
            if (fonts.TryGetValue(name, out TMP_FontAsset cached) && cached != null) return cached;
            if (GUIPDA.instance == null) return null;
            foreach (TMP_Text text in GUIPDA.instance.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font != null && text.font.name == name)
                {
                    fonts[name] = text.font;
                    return text.font;
                }
            }
            return null;
        }

        //Puts a PDA sprite on an image, sliced when it has borders; leaves it as it is when the PDA hasn't the sprite
        public static bool Apply(Image image, string name)
        {
            Sprite sprite = Sprite(name);
            if (image == null || sprite == null) return false;
            image.sprite = sprite;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            return true;
        }
    }
}
