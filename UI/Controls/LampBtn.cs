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
    public class LampBtn
    {
        private static readonly Sprite BtnOffSprite = SpriteUtil.LoadModSprite("LampBtnOff.png");
        private static readonly Sprite BtnOnSprite = SpriteUtil.LoadModSprite("LampBtnOn.png");

        public static GameObject Create(string name, string label, float width, float height, float offsetX, float offsetY)
        {
            GameObject btn = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image));
            btn.layer = LayerMask.NameToLayer("UI");

            RectTransform rect = btn.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(offsetX, offsetY);
            rect.anchorMax = new Vector2(offsetX + width, offsetY + height);
            rect.pivot = new Vector2(0.5f, 0.5f);

            Image image = btn.GetComponent<Image>();
            image.sprite = BtnOffSprite;
            image.type = Image.Type.Simple;










            return btn;
        }




    }
}
