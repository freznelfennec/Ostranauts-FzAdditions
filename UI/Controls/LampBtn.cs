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
using UnityEngine.Windows;

using UnityEngine.InputSystem;

namespace Freznel.FzAdditions.UI.Controls
{
    public class LampBtn : FzUIElement, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private static readonly Sprite BtnOffSprite = SpriteUtil.LoadModSprite("LampBtnOff.png");
        private static readonly Sprite BtnOnSprite = SpriteUtil.LoadModSprite("LampBtnOn.png");
        private static readonly Sprite BtnHoverSprite = SpriteUtil.LoadModSprite("LampBtnHover.png");
        private static readonly Color OffColor = SpriteUtil.ColorFromHex("#565656FF");
        private static readonly Color DefaultOnColor = SpriteUtil.ColorFromHex("#FFF9EFFF");
        private static readonly string DownAudio = "ShipUIBtnReactorCoilFwdIn";
        private static readonly string UpAudio = "ShipUIBtnReactorCoilFwdOut";

        public enum LampBtnState { Off, Hover, On }

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
        private bool _enabled;
        [SerializeField]
        private LampBtnState _state;
        [SerializeField]
        private LampBtnState _downState;
        [SerializeField]
        private LampBtnState _upState;
        [SerializeField]
        private Color _onColor;

        public event Action<bool> OnChanged;

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public bool On
        {
            get => _state == LampBtnState.On || (_state == LampBtnState.Hover && _downState == LampBtnState.On);
            set {
                LampBtnState newState = value ? LampBtnState.On : LampBtnState.Off;
                if (gameObject == null || _state == newState) return;
                _state = newState;
                SetVisual();
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
                if (_state == LampBtnState.On) SetVisual();
            }
        }


        public LampBtn() : base()
        {
            On = false;
            Enabled = true;
        }

        private void SetVisual()
        {
            if (gameObject == null) return;
            TextMeshProUGUI tmp = gameObject.GetComponentInChildren<TextMeshProUGUI>();
            Image image = gameObject.GetComponent<Image>();
            if (tmp == null || image == null) return;
            switch (_state)
            {
                case LampBtnState.On:
                    tmp.color = _onColor;
                    image.sprite = BtnOnSprite;
                    break;
                case LampBtnState.Off:
                    tmp.color = OffColor;
                    image.sprite = BtnOffSprite;
                    break;
                case LampBtnState.Hover:
                    tmp.color = _downState == LampBtnState.On ? _onColor : OffColor;
                    image.sprite = BtnHoverSprite;
                    break;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _downState = _state;
            _upState = _state == LampBtnState.On ? LampBtnState.Off : LampBtnState.On;
            _state = LampBtnState.Hover;
            SetVisual();
            AudioManager.am.PlayAudioEmitter(DownAudio, false);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_state == LampBtnState.Hover)
            {
                if (_enabled)
                {
                    _state = _upState;
                    OnChanged?.Invoke(_state == LampBtnState.On);
                }
                else
                {
                    _state = _downState;
                }
                SetVisual();
                AudioManager.am.PlayAudioEmitter(UpAudio, false);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_state == LampBtnState.Hover)
            {
                _state = _downState;
                SetVisual();
                AudioManager.am.PlayAudioEmitter(UpAudio, false);
            }
        }
    }
}
