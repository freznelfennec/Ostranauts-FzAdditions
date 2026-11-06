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

        private static readonly string ScreenPrefab = "GUIShip/GUIOrbitDraw/NavModMooringControl";
        private static readonly string ScreenRimSprite = "Rounded Corner Rect POT 9-Sliced";
        private static readonly string ScreenBackgroundSprite = "Background";
        private static readonly Color ScreenRimColor = SpriteUtil.ColorFromHex("#7A7A7AFF");
        private static readonly Color ScreenBackgroundColor = SpriteUtil.ColorFromHex("#000000FF");
        public static readonly string ScreenBackgroundName = "Background";
        private static readonly float TitleWidth = 0.9f;
        private static readonly float TitleTopMargin = 0.02f;
        private static readonly float TitleMinFontSize = 8f;
        private static readonly float TitleMaxFontSize = 14f;

        //Rounded black instrument screen with a thin border, filling the parent
        public static void CreateScreen(GameObject parent)
        {
            CreateScreenLayer(parent, "Rim", ScreenRimSprite, ScreenRimColor);
            CreateScreenLayer(parent, ScreenBackgroundName, ScreenBackgroundSprite, ScreenBackgroundColor);
            parent.AddComponent<FzScreen>();
        }

        private static void CreateScreenLayer(GameObject parent, string name, string spriteName, Color color)
        {
            GameObject layer = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image));
            layer.layer = parent.layer;

            RectTransform rect = layer.GetComponent<RectTransform>();
            rect.SetParent(parent.GetComponent<RectTransform>(), false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;

            Image image = layer.GetComponent<Image>();
            image.sprite = SpriteUtil.FindSprite(ScreenPrefab, spriteName);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
        }

        //Centered along the top edge, height is a fraction of the panel's height
        public static GameObject AddTitle(GameObject panel, string title, float height)
        {
            return Label.Create(panel, "Title", title, TitleWidth, height, (1f - TitleWidth) / 2f, 1f - TitleTopMargin - height, TitleMinFontSize, TitleMaxFontSize);
        }

        //Invisible root for a GUI made of several sibling panels
        public static GameObject CreateContainer(string name, float width, float height, float offsetX, float offsetY)
        {
            GameObject container = new GameObject(name, typeof(RectTransform), typeof(FzUIElement));
            container.layer = LayerMask.NameToLayer("UI");
            container.GetComponent<FzUIElement>().SetTransform(width, height, offsetX, offsetY);
            return container;
        }

        public static GameObject CreatePanel(string name, float width, float height, float offsetX, float offsetY, GameObject parent = null)
        {
            GameObject panel = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(FzUIElement));
            panel.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                panel.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

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
