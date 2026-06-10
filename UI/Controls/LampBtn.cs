using Freznel.FzAdditions.Util;
using Ostranauts.ShipGUIs.Market.GUICargoPod;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    public class LampBtn : FzUIElement, IPointerClickHandler //TODO: Use MouseDownEvent/MouseUpEvent to animate the button like base game buttons
    {
        private static readonly Sprite BtnOffSprite = SpriteUtil.LoadModSprite("LampBtnOff.png");
        private static readonly Sprite BtnOnSprite = SpriteUtil.LoadModSprite("LampBtnOn.png");
        private static readonly Color OffColor = SpriteUtil.ColorFromHex("#565656FF");
        private static readonly Color DefaultOnColor = SpriteUtil.ColorFromHex("#FFF9EFFF");

        static LampBtn() { }

        public static GameObject Create(GameObject parent, string name, string label, float width, float height, float offsetX, float offsetY)
        {
            return Create(parent, name, label, width, height, offsetX, offsetY, DefaultOnColor);
        }

        public static GameObject Create(GameObject parent, string name, string label, float width, float height, float offsetX, float offsetY, Color onColor)
        {
            GameObject btn = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(LampBtn));
            btn.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                btn.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

            LampBtn element = btn.GetComponent<LampBtn>();
            element.SetTransform(width, height, offsetX, offsetY);
            element.OnColor = onColor;

            Image image = btn.GetComponent<Image>();
            image.sprite = BtnOffSprite;
            image.type = Image.Type.Simple;

            GameObject lbl = new GameObject("Label", typeof(TextMeshProUGUI), typeof(FzUIElement));
            lbl.GetComponent<RectTransform>().SetParent(btn.GetComponent<RectTransform>(), false);
            lbl.GetComponent<FzUIElement>().SetTransform(0.5f, 0.65f, 0.24f, 0.18f);

            TextMeshProUGUI text = lbl.GetComponent<TextMeshProUGUI>();
            text.textWrappingMode = TextWrappingModes.Normal;
            text.color = OffColor;
            text.fontSizeMin = 9;
            text.fontSizeMax = 36;
            text.enableAutoSizing = true;
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;

            return btn;
        }

        [SerializeField]
        private bool _on;
        [SerializeField]
        private Color _onColor;

        public event Action<bool> OnChanged;

        public bool On
        {
            get => _on;
            set {
                if (gameObject == null || value == _on) return;
                _on = value;
                Image image = gameObject.GetComponent<Image>();
                image.sprite = value ? BtnOnSprite : BtnOffSprite;
                SetColor(_on ? _onColor : OffColor);
                OnChanged?.Invoke(value);
            }
        }

        public Color OnColor
        {
            get => _onColor;
            set
            {
                _onColor = value;
                FzAdditions.Logger.LogInfo($"Set on color: {_onColor.ToString()}");
                if (_on)
                {
                    SetColor(value);
                }
            }
        }


        public LampBtn() : base()
        {
            On = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            On = !_on;
        }

        private void SetColor(Color color)
        {
            TextMeshProUGUI tmp = gameObject.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp == null) return;
            FzAdditions.Logger.LogInfo($"Apply color: {color.ToString()}");
            tmp.color = color;
        }
    }
}
