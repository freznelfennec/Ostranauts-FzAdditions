using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    public class FzRocker : FzUIElement
    {
        private static readonly string SpritePrefab = "GUIShip/GUIReactor";
        private static readonly string OffSprite = "GUIRockerOff";
        private static readonly string OnSprite = "GUIRockerOn";
        private static readonly string DownAudio = "ShipUIBtnReactorCryoOn";
        private static readonly string UpAudio = "ShipUIBtnReactorCryoOff";
        private static readonly string TopLabelName = "TopLabel";
        private static readonly string BottomLabelName = "BottomLabel";
        private static readonly string OnTextName = "OnText";
        private static readonly string OffTextName = "OffText";
        private static readonly string OnText = "I";
        private static readonly string OffText = "0";
        private static readonly float ArtAspect = 32f / 65f;
        private static readonly float IOGap = 0.24f;
        private static readonly float IOHeightOffset = 0.25f;
        private static readonly float IOBoxWidth = 0.5f;
        private static readonly float IOBoxHeight = 0.3f;
        private static readonly float LabelHeight = 0.2f;
        private static readonly float MinFontSize = 6f;
        private static readonly float MaxFontSize = 11f;

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY, string topLabel = null, string bottomLabel = null, bool showIO = false)
        {
            GameObject sw = new GameObject(name, typeof(RectTransform), typeof(Toggle), typeof(FzRocker));
            sw.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                sw.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

            FzRocker element = sw.GetComponent<FzRocker>();
            element.SetTransform(width, height, offsetX, offsetY);

            GameObject bg = new GameObject("Background", typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(FzUIElement));
            bg.layer = sw.layer;
            bg.GetComponent<RectTransform>().SetParent(sw.GetComponent<RectTransform>(), false);
            bg.GetComponent<FzUIElement>().SetTransform(1f, 1f, 0f, 0f);
            Image bgImage = bg.GetComponent<Image>();
            bgImage.sprite = SpriteUtil.FindSprite(SpritePrefab, OffSprite);
            bgImage.preserveAspect = true;

            GameObject check = new GameObject("Checkmark", typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(FzUIElement));
            check.layer = sw.layer;
            check.GetComponent<RectTransform>().SetParent(bg.GetComponent<RectTransform>(), false);
            check.GetComponent<FzUIElement>().SetTransform(1f, 1f, 0f, 0f);
            Image checkImage = check.GetComponent<Image>();
            checkImage.sprite = SpriteUtil.FindSprite(SpritePrefab, OnSprite);
            checkImage.preserveAspect = true;

            Toggle toggle = sw.GetComponent<Toggle>();
            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            toggle.transition = Selectable.Transition.None;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };

            AudioManager.AddBtnAudio(sw, DownAudio, UpAudio);

            element.TopLabel = topLabel;
            element.BottomLabel = bottomLabel;
            element.ShowIO = showIO;

            return sw;
        }

        private Toggle toggle;

        public event Action<bool> OnChanged;

        public bool On
        {
            get => Inner.isOn;
            set => Inner.isOn = value;
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
            set => SetLabel(TopLabelName, value, 3f, LabelHeight, -1f, 1f);
        }

        public string BottomLabel
        {
            get => GetLabel(BottomLabelName);
            set => SetLabel(BottomLabelName, value, 3f, LabelHeight, -1f, -LabelHeight);
        }

        public bool ShowIO
        {
            get => GetLabel(OnTextName) != null;
            set
            {
                SetLabel(OnTextName, value ? OnText : null, 0f, 0f, 0.5f, 0.5f);
                SetLabel(OffTextName, value ? OffText : null, 0f, 0f, 0.5f, 0.5f);
                UpdateIOLayout();
            }
        }

        private Toggle Inner => toggle != null ? toggle : toggle = GetComponent<Toggle>();

        //Listeners are not copied when the prefab is instantiated
        private void Awake()
        {
            Inner.onValueChanged.AddListener(HandleChanged);
        }

        private void Start()
        {
            UpdateIOLayout();
        }

        private void OnRectTransformDimensionsChange()
        {
            UpdateIOLayout();
        }

        //The art keeps its aspect inside the control, so measure from what is actually drawn
        private void UpdateIOLayout()
        {
            Rect rect = GetComponent<RectTransform>().rect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            float artHeight = Mathf.Min(rect.height, rect.width / ArtAspect);
            float artWidth = artHeight * ArtAspect;
            PlaceIOText(OnTextName, artWidth / rect.width, artHeight / rect.height, IOHeightOffset);
            PlaceIOText(OffTextName, artWidth / rect.width, artHeight / rect.height, -IOHeightOffset);
        }

        private void PlaceIOText(string labelName, float artWidth, float artHeight, float heightOffset)
        {
            RectTransform label = transform.Find(labelName) as RectTransform;
            if (label == null) return;
            float centerX = 0.5f + artWidth * (0.5f + IOGap);
            float centerY = 0.5f + artHeight * heightOffset;
            label.anchorMin = new Vector2(centerX - artWidth * IOBoxWidth / 2f, centerY - artHeight * IOBoxHeight / 2f);
            label.anchorMax = new Vector2(centerX + artWidth * IOBoxWidth / 2f, centerY + artHeight * IOBoxHeight / 2f);
            label.sizeDelta = Vector2.zero;
            label.anchoredPosition = Vector2.zero;
        }

        //For loading saved state, does not raise OnChanged
        public void SetOnWithoutNotify(bool on)
        {
            Inner.SetIsOnWithoutNotify(on);
        }

        private void HandleChanged(bool on)
        {
            OnChanged?.Invoke(on);
        }

        private string GetLabel(string labelName)
        {
            Transform label = transform.Find(labelName);
            return label != null && label.gameObject.activeSelf ? label.GetComponent<TMP_Text>().text : null;
        }

        private void SetLabel(string labelName, string text, float labelWidth, float labelHeight, float labelOffsetX, float labelOffsetY)
        {
            Transform label = transform.Find(labelName);
            if (string.IsNullOrEmpty(text))
            {
                if (label != null) label.gameObject.SetActive(false);
                return;
            }

            if (label == null) label = Label.Create(gameObject, labelName, text, labelWidth, labelHeight, labelOffsetX, labelOffsetY, MinFontSize, MaxFontSize).transform;
            label.GetComponent<TMP_Text>().text = text;
            label.gameObject.SetActive(true);
        }
    }
}
