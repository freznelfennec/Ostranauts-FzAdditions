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
            GameObject panel = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(FzUIElement));
            panel.layer = LayerMask.NameToLayer("UI");

            panel.GetComponent<FzUIElement>().SetTransform(width, height, offsetX, offsetY);

            Image image = panel.GetComponent<Image>();
            image.sprite = SpriteUtil.FindSprite("GUIShip/GUIReactor", "GUIPanel128x256");
            image.type = Image.Type.Sliced;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillCenter = true;
            image.fillClockwise = true;

            return panel;
        }





    }
}
