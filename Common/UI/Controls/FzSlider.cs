using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    public class FzSlider : FzUIElement, IPointerUpHandler
    {
        private static readonly string SpritePrefab = "GUIShip/GUIReactor";
        private static readonly string BaseSprite = "GUISliderBase";
        private static readonly string HandleSprite = "GUISliderHandle";
        private static readonly string TopLabelName = "TopLabel";
        private static readonly string BottomLabelName = "BottomLabel";
        private static readonly float LabelMinFontSize = 6f;
        private static readonly float LabelMaxFontSize = 11f;
        private static readonly float HandleHeight = 0.12f;
        private static readonly float LabelHeight = 0.06f;
        private static readonly float TravelInsetBottom = 0.093f;
        private static readonly float TravelInsetTop = 0.091f;

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY, float minValue, float maxValue, float step = 0f, string topLabel = null, string bottomLabel = null)
        {
            GameObject sld = new GameObject(name, typeof(RectTransform), typeof(Slider), typeof(FzSlider));
            sld.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                sld.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

            FzSlider element = sld.GetComponent<FzSlider>();
            element.SetTransform(width, height, offsetX, offsetY);

            GameObject bg = new GameObject("Background", typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(FzUIElement));
            bg.layer = sld.layer;
            bg.GetComponent<RectTransform>().SetParent(sld.GetComponent<RectTransform>(), false);
            bg.GetComponent<FzUIElement>().SetTransform(1f, 1f, 0f, 0f);
            bg.GetComponent<Image>().sprite = SpriteUtil.FindSprite(SpritePrefab, BaseSprite);


            //Inset to the base art's end marks, proportional so it holds at any slider size
            GameObject area = new GameObject("Handle Slide Area", typeof(RectTransform), typeof(FzUIElement));
            area.layer = sld.layer;
            RectTransform areaRect = area.GetComponent<RectTransform>();
            areaRect.SetParent(sld.GetComponent<RectTransform>(), false);
            area.GetComponent<FzUIElement>().SetTransform(1f, 1f - TravelInsetBottom - TravelInsetTop, 0f, TravelInsetBottom);

            GameObject handle = new GameObject("Handle", typeof(CanvasRenderer), typeof(RectTransform), typeof(Image));
            handle.layer = sld.layer;
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.SetParent(areaRect, false);
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(1f, 0f);
            handleRect.sizeDelta = Vector2.zero;

            Image handleImage = handle.GetComponent<Image>();
            handleImage.sprite = SpriteUtil.FindSprite(SpritePrefab, HandleSprite);
            handleImage.preserveAspect = true;

            Slider slider = sld.GetComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.handleRect = handleRect;
            element.UpdateHandleSize();
            slider.direction = Slider.Direction.BottomToTop;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            element.SetRange(minValue, maxValue, step);
            element.TopLabel = topLabel;
            element.BottomLabel = bottomLabel;

            return sld;
        }

        [SerializeField]
        private float _step;
        [SerializeField]
        private float _value;

        private Slider slider;

        public event Action<float> OnChanged;

        //Raised when the player lets go, after any OnChanged from the drag
        public event Action<float> OnReleased;

        public float MinValue => Inner.minValue;

        public float MaxValue => Inner.maxValue;

        public float Step
        {
            get => _step;
            set => SetRange(MinValue, MaxValue, value);
        }

        public float Value
        {
            get => _value;
            set => Apply(value, true);
        }

        public bool Enabled
        {
            get => Inner.interactable;
            set => Inner.interactable = value;
        }

        //Null or empty hides the label
        public string TopLabel
        {
            get => GetLabel(TopLabelName);
            set => SetLabel(TopLabelName, value, 1f);
        }

        public string BottomLabel
        {
            get => GetLabel(BottomLabelName);
            set => SetLabel(BottomLabelName, value, -LabelHeight);
        }

        private Slider Inner => slider != null ? slider : slider = GetComponent<Slider>();

        private void Start()
        {
            UpdateHandleSize();
        }

        private void OnRectTransformDimensionsChange()
        {
            UpdateHandleSize();
        }

        //Slider moves the handle by anchor, so its height has to follow the control's
        private void UpdateHandleSize()
        {
            RectTransform handle = Inner.handleRect;
            if (handle != null) handle.sizeDelta = new Vector2(0f, GetComponent<RectTransform>().rect.height * HandleHeight);
        }

        //Listeners are not copied when the prefab is instantiated
        private void Awake()
        {
            Inner.onValueChanged.AddListener(HandleChanged);
        }

        public void SetRange(float minValue, float maxValue, float step = 0f)
        {
            if (maxValue <= minValue)
            {
                FzAdditions.Logger.LogError($"Invalid slider range {minValue} to {maxValue} on {name}");
                return;
            }

            _step = Mathf.Max(step, 0f);
            Inner.minValue = minValue;
            Inner.maxValue = maxValue;
            Apply(Inner.value, false);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            OnReleased?.Invoke(_value);
        }

        //For loading saved state, does not raise OnChanged
        public void SetValueWithoutNotify(float value)
        {
            Apply(value, false);
        }

        private string GetLabel(string labelName)
        {
            Transform label = transform.Find(labelName);
            return label != null && label.gameObject.activeSelf ? label.GetComponent<TMP_Text>().text : null;
        }

        private void SetLabel(string labelName, string text, float labelOffsetY)
        {
            Transform label = transform.Find(labelName);
            if (string.IsNullOrEmpty(text))
            {
                if (label != null) label.gameObject.SetActive(false);
                return;
            }

            if (label == null) label = CreateLabel(labelName, labelOffsetY);
            label.GetComponent<TMP_Text>().text = text;
            label.gameObject.SetActive(true);
        }

        private Transform CreateLabel(string labelName, float labelOffsetY)
        {
            //Wider than the slider so short labels are not squeezed
            return Label.Create(gameObject, labelName, null, 3f, LabelHeight, -1f, labelOffsetY, LabelMinFontSize, LabelMaxFontSize).transform;
        }

        private void HandleChanged(float raw)
        {
            Apply(raw, true);
        }

        private void Apply(float value, bool notify)
        {
            float snapped = Snap(value);
            bool changed = snapped != _value;
            _value = snapped;
            if (Inner.value != snapped) Inner.SetValueWithoutNotify(snapped);
            if (changed && notify) OnChanged?.Invoke(snapped);
        }

        private float Snap(float value)
        {
            double min = MinValue;
            double max = MaxValue;
            double clamped = Math.Min(Math.Max(value, min), max);
            if (_step <= 0f) return (float)clamped;
            double stepped = min + Math.Round((clamped - min) / _step) * _step;
            return (float)Math.Min(stepped, max);
        }
    }
}
