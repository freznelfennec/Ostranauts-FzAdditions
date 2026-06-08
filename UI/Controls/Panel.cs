using Freznel.FzAdditions.UI.CustomPrefabs;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    public static class Panel
    {

        public static GameObject CreatePanel(string name, float width, float height, float offsetX, float offsetY)
        {
            GameObject panel = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image));
            panel.layer = LayerMask.NameToLayer("UI");

            RectTransform centerPanelRect = panel.GetComponent<RectTransform>();
            centerPanelRect.anchorMin = new Vector2(offsetX, offsetY);
            centerPanelRect.anchorMax = new Vector2(offsetX + width, offsetY + height);
            centerPanelRect.pivot = new Vector2(0.5f, 0.5f);

            Image centerPanelImage = panel.GetComponent<Image>();
            centerPanelImage.sprite = SpriteUtil.FindSprite("GUIShip/GUIReactor", "GUIPanel128x256");
            centerPanelImage.type = Image.Type.Sliced;
            centerPanelImage.fillMethod = Image.FillMethod.Radial360;
            centerPanelImage.fillCenter = true;
            centerPanelImage.fillClockwise = true;

            return panel;
        }





    }
}
