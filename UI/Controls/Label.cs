using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Freznel.FzAdditions.UI.Controls
{
    public static class Label
    {
        private static readonly string Font = "Fonts & Materials/NotoSansSC-Regular SDF 1024 Top 2500";
        private static readonly Color DefaultColor = SpriteUtil.ColorFromHex("#9F9F9FFF");

        public static GameObject Create(GameObject parent, string name, string label, float width, float height, float offsetX, float offsetY, float minFontSize, float maxFontSize)
        {
            GameObject lbl = new GameObject(name, typeof(TextMeshProUGUI), typeof(FzUIElement));
            lbl.layer = parent.layer;
            lbl.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            lbl.GetComponent<FzUIElement>().SetTransform(width, height, offsetX, offsetY);

            TextMeshProUGUI text = lbl.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMin = minFontSize;
            text.fontSizeMax = maxFontSize;
            text.raycastTarget = false;
            text.color = DefaultColor;
            text.text = label;

            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(Font);
            if (font != null) text.font = font; else FzAdditions.Logger.LogError("Could not find font " + Font);

            return lbl;
        }
    }
}
