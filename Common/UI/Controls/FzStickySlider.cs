using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    //Horizontal spring loaded slider, like the Time/Zoom module's fast forward. It rests at the centre and
    //raises OnHeld each frame it is pulled away, from -1 at the left end to 1 at the right.
    public class FzStickySlider : FzUIElement, IPointerDownHandler, IPointerUpHandler
    {
        private static readonly string SpritePrefab = "GUIShip/GUIOrbitDraw/NavModTimeZoom";
        private static readonly string TrackPrefab = "GUIShip/GUIOrbitDraw/NavModSensorsMFD";
        private static readonly string TrackSprite = "GUIRectEmbossed";
        private static readonly string HandleSprite = "GUISliderHandleRotated";
        private static readonly Color TrackColor = SpriteUtil.ColorFromHex("#1C1E20FF");
        private static readonly Color MarkColor = SpriteUtil.ColorFromHex("#8C8C8CFF");
        private static readonly float LabelMinFontSize = 9f;
        private static readonly float LabelMaxFontSize = 16f;
        private static readonly float LabelWidth = 0.06f; //Each end of the control, the track fills the rest
        private static readonly float TravelInset = 0.03f; //Of the track
        private static readonly float HandleWidth = 0.28f;
        private static readonly float MarkWidth = 0.003f; //Of the track
        private static readonly float MarkHeight = 0.28f; //Of the track
        private static readonly float MarksPerWidth = 28f; //The stock slider's spacing, per full control width. Odd counts keep one at the centre.

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY, string leftLabel = null, string rightLabel = null)
        {
            GameObject sld = new GameObject(name, typeof(RectTransform), typeof(Slider), typeof(FzStickySlider));
            sld.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                sld.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

            FzStickySlider element = sld.GetComponent<FzStickySlider>();
            element.SetTransform(width, height, offsetX, offsetY);

            //Drawn rather than the stock base sprite, which stretches its marks at this width
            float trackWidth = 1f - LabelWidth * 2f;
            GameObject track = CreateImage(sld, "Background", trackWidth, 1f, LabelWidth, 0f, TrackColor);
            Image trackImage = track.GetComponent<Image>();
            trackImage.sprite = SpriteUtil.FindSprite(TrackPrefab, TrackSprite);
            trackImage.type = Image.Type.Sliced;

            int marks = Mathf.RoundToInt(MarksPerWidth * width) | 1;
            for (int i = 0; i < marks; i++)
            {
                float position = TravelInset + (1f - TravelInset * 2f) * i / (marks - 1);
                CreateImage(track, "Mark" + i, MarkWidth, MarkHeight, position - MarkWidth / 2f, (1f - MarkHeight) / 2f, MarkColor);
            }

            GameObject area = new GameObject("Handle Slide Area", typeof(RectTransform), typeof(FzUIElement));
            area.layer = sld.layer;
            RectTransform areaRect = area.GetComponent<RectTransform>();
            areaRect.SetParent(sld.GetComponent<RectTransform>(), false);
            area.GetComponent<FzUIElement>().SetTransform(trackWidth * (1f - TravelInset * 2f), 1f, LabelWidth + trackWidth * TravelInset, 0f);

            GameObject handle = new GameObject("Handle", typeof(CanvasRenderer), typeof(RectTransform), typeof(Image));
            handle.layer = sld.layer;
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.SetParent(areaRect, false);
            handleRect.sizeDelta = Vector2.zero;

            Image handleImage = handle.GetComponent<Image>();
            handleImage.sprite = SpriteUtil.FindSprite(SpritePrefab, HandleSprite);
            handleImage.preserveAspect = true;

            Slider slider = sld.GetComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.minValue = -1f;
            slider.maxValue = 1f;
            slider.value = 0f;

            if (!string.IsNullOrEmpty(leftLabel)) Label.Create(sld, "LeftLabel", leftLabel, LabelWidth, 1f, 0f, 0f, LabelMinFontSize, LabelMaxFontSize);
            if (!string.IsNullOrEmpty(rightLabel)) Label.Create(sld, "RightLabel", rightLabel, LabelWidth, 1f, 1f - LabelWidth, 0f, LabelMinFontSize, LabelMaxFontSize);

            return sld;
        }

        private static GameObject CreateImage(GameObject parent, string name, float width, float height, float offsetX, float offsetY, Color color)
        {
            GameObject image = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(FzUIElement));
            image.layer = parent.layer;
            image.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            image.GetComponent<FzUIElement>().SetTransform(width, height, offsetX, offsetY);
            image.GetComponent<Image>().color = color;
            image.GetComponent<Image>().raycastTarget = false;
            return image;
        }

        private Slider slider;
        private bool pressed;

        //Raised every frame the slider is pulled away from the centre
        public event Action<float> OnHeld;

        private Slider Inner => slider != null ? slider : slider = GetComponent<Slider>();

        private void Start()
        {
            UpdateHandleSize();
        }

        private void OnRectTransformDimensionsChange()
        {
            UpdateHandleSize();
        }

        //Slider moves the handle by anchor, so its width has to follow the control's
        private void UpdateHandleSize()
        {
            RectTransform handle = Inner.handleRect;
            if (handle != null) handle.sizeDelta = new Vector2(GetComponent<RectTransform>().rect.width * HandleWidth, 0f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
        }

        //A disabled control never gets its pointer up
        private void OnDisable()
        {
            pressed = false;
            Inner.SetValueWithoutNotify(0f);
        }

        private void Update()
        {
            if (!pressed)
            {
                if (Inner.value != 0f) Inner.SetValueWithoutNotify(0f);
                return;
            }

            if (Inner.value != 0f) OnHeld?.Invoke(Inner.value);
        }
    }
}
