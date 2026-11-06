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
    public class FzLamp : FzUIElement
    {
        private static readonly string SourcePrefab = "GUIShip/GUIReactor";
        private static readonly string SourceLamp = "bmpFeedLbl";
        private static readonly string LabelName = "Label";
        private static readonly Color LabelColor = SpriteUtil.ColorFromHex("#00000087");
        private static readonly float LabelMinFontSize = 6f;
        private static readonly float LabelMaxFontSize = 11f;

        //Seconds lit between dropouts at the slowest and fastest flicker rate
        private static readonly float FlickerSlowInterval = 1.5f;
        private static readonly float FlickerFastInterval = 0.15f;
        private static readonly float FlickerIntervalJitter = 0.6f;
        private static readonly float FlickerMinDropout = 0.05f;
        private static readonly float FlickerMaxDropout = 0.2f;

        public enum LampState { Off, Flicker, On }

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY, LampColor color = LampColor.White, string label = null, bool stretch = false)
        {
            return Create(parent, name, width, height, offsetX, offsetY, color.ToColor(), label, stretch);
        }

        //Stretch fills the whole rect, otherwise the lamp keeps its sprite's shape and height limits its width
        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY, Color color, string label = null, bool stretch = false)
        {
            GameObject lamp = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image), typeof(FzLamp));
            lamp.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                lamp.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

            FzLamp element = lamp.GetComponent<FzLamp>();
            element.SetTransform(width, height, offsetX, offsetY);

            GUILamp source = UnityUtil.FindDescendant<GUILamp>(Resources.Load<GameObject>(SourcePrefab)?.transform, SourceLamp);
            if (source == null || source.aSprites == null || source.aSprites.Length == 0)
            {
                FzAdditions.Logger.LogError("Could not find lamp " + SourceLamp + " in prefab " + SourcePrefab);
            }
            else
            {
                element._offSprite = source.aSprites[0];
                element._onSprite = source.aSprites[source.aSprites.Length - 1];
            }

            lamp.GetComponent<Image>().preserveAspect = !stretch;
            element._color = color;
            element.SetVisual();
            element.LabelText = label;

            return lamp;
        }

        [SerializeField]
        private LampState _state;
        [SerializeField]
        private Color _color;
        [SerializeField]
        private Sprite _offSprite;
        [SerializeField]
        private Sprite _onSprite;

        private float flickerRate = 1f;
        private float flickerTimer;
        private bool flickerLit;

        public LampState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                flickerLit = true;
                flickerTimer = NextFlickerDuration();
                SetVisual();
            }
        }

        //0 for occasional dropouts, 1 for constant flickering
        public float FlickerRate
        {
            get => flickerRate;
            set => flickerRate = Mathf.Clamp01(value);
        }

        public bool On
        {
            get => _state == LampState.On;
            set => State = value ? LampState.On : LampState.Off;
        }

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                SetVisual();
            }
        }

        //Null or empty hides the label
        public string LabelText
        {
            get
            {
                Transform label = transform.Find(LabelName);
                return label != null && label.gameObject.activeSelf ? label.GetComponent<TMP_Text>().text : null;
            }
            set
            {
                Transform label = transform.Find(LabelName);
                if (string.IsNullOrEmpty(value))
                {
                    if (label != null) label.gameObject.SetActive(false);
                    return;
                }

                if (label == null)
                {
                    label = Label.Create(gameObject, LabelName, value, 0.72f, 0.6f, 0.14f, 0.21f, LabelMinFontSize, LabelMaxFontSize).transform;
                    label.GetComponent<TMP_Text>().color = LabelColor;
                }
                label.GetComponent<TMP_Text>().text = value;
                label.gameObject.SetActive(true);
            }
        }

        public void SetColor(LampColor color)
        {
            Color = color.ToColor();
        }

        private void Update()
        {
            if (_state != LampState.Flicker) return;
            flickerTimer -= Time.deltaTime;
            if (flickerTimer > 0f) return;
            flickerLit = !flickerLit;
            flickerTimer = NextFlickerDuration();
            SetVisual();
        }

        private float NextFlickerDuration()
        {
            if (!flickerLit) return UnityEngine.Random.Range(FlickerMinDropout, FlickerMaxDropout);
            float interval = Mathf.Lerp(FlickerSlowInterval, FlickerFastInterval, flickerRate);
            return interval * UnityEngine.Random.Range(1f - FlickerIntervalJitter, 1f + FlickerIntervalJitter);
        }

        private bool IsLit()
        {
            return _state == LampState.Flicker ? flickerLit : _state == LampState.On;
        }

        private void SetVisual()
        {
            Image image = GetComponent<Image>();
            if (image == null) return;
            Sprite sprite = IsLit() ? _onSprite : _offSprite;
            if (image.sprite != sprite) image.sprite = sprite;
            image.color = _color;
        }
    }
}
