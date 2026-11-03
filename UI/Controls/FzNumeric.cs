using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Freznel.FzAdditions.UI.Controls
{
    public class FzNumeric : FzUIElement
    {
        private static readonly string Font = "Fonts & Materials/Jura-Regular SDF";
        private static readonly string TextName = "Text";
        private static readonly float MinFontSize = 9f;
        private static readonly float MaxFontSize = 36f;
        private static readonly string TopLabelName = "TopLabel";
        private static readonly string BottomLabelName = "BottomLabel";
        private static readonly float LabelHeight = 0.35f;
        private static readonly float LabelMinFontSize = 6f;
        private static readonly float LabelMaxFontSize = 11f;

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY, LampColor color = LampColor.Yellow, string unit = null, string format = "0.00", string topLabel = null, string bottomLabel = null)
        {
            return Create(parent, name, width, height, offsetX, offsetY, color.ToColor(), unit, format, topLabel, bottomLabel);
        }

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY, Color color, string unit = null, string format = "0.00", string topLabel = null, string bottomLabel = null)
        {
            GameObject numeric = new GameObject(name, typeof(RectTransform), typeof(FzNumeric));
            numeric.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                numeric.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

            FzNumeric element = numeric.GetComponent<FzNumeric>();
            element.SetTransform(width, height, offsetX, offsetY);

            Panel.CreateScreen(numeric);

            TMP_Text text = Label.Create(numeric, TextName, "", 0.9f, 0.8f, 0.05f, 0.1f, MinFontSize, MaxFontSize).GetComponent<TMP_Text>();
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(Font);
            if (font != null) text.font = font; else FzAdditions.Logger.LogError("Could not find font " + Font);

            element._color = color;
            element._unit = unit;
            element._format = format;
            element.UpdateText();
            element.TopLabel = topLabel;
            element.BottomLabel = bottomLabel;

            return numeric;
        }

        //A Formatter for a time in seconds, negative when there is no time to show
        public static string FormatDuration(float seconds)
        {
            if (seconds < 0f) return "--:--:--";
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            string clock = $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}";
            return time.Days >= 1 ? $"{time.Days:00}D {clock}" : clock;
        }

        //A Formatter for energy in kWh, it brings its own unit so leave Unit empty
        public static string FormatEnergy(float kWh)
        {
            return FormatKilo(kWh, "kWh", "MWh");
        }

        //A Formatter for power in kW, it brings its own unit so leave Unit empty
        public static string FormatPower(float kW)
        {
            return FormatKilo(kW, "kW", "MW");
        }

        private static string FormatKilo(float value, string unit, string kiloUnit)
        {
            return value >= 1000f
                ? (value / 1000f).ToString("0.00", CultureInfo.InvariantCulture) + kiloUnit
                : value.ToString("0.0", CultureInfo.InvariantCulture) + unit;
        }

        [SerializeField]
        private float _value;
        [SerializeField]
        private Color _color;
        [SerializeField]
        private string _unit;
        [SerializeField]
        private string _format;

        private Func<float, string> _formatter;

        public float Value
        {
            get => _value;
            set
            {
                //The text only needs rebuilding when the value has moved
                if (_value == value) return;
                _value = value;
                UpdateText();
            }
        }

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                UpdateText();
            }
        }

        //Null or empty shows the number alone
        public string Unit
        {
            get => _unit;
            set
            {
                _unit = value;
                UpdateText();
            }
        }

        public string Format
        {
            get => _format;
            set
            {
                _format = value;
                UpdateText();
            }
        }

        //Replaces Format when set, for text a number format can't express
        public Func<float, string> Formatter
        {
            get => _formatter;
            set
            {
                _formatter = value;
                UpdateText();
            }
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

        public void SetColor(LampColor color)
        {
            Color = color.ToColor();
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

            if (label == null) label = Label.Create(gameObject, labelName, text, 1f, LabelHeight, 0f, labelOffsetY, LabelMinFontSize, LabelMaxFontSize).transform;
            label.GetComponent<TMP_Text>().text = text;
            label.gameObject.SetActive(true);
        }

        private void UpdateText()
        {
            TMP_Text text = transform.Find(TextName)?.GetComponent<TMP_Text>();
            if (text == null) return;
            text.text = (_formatter != null ? _formatter(_value) : _value.ToString(_format, CultureInfo.InvariantCulture)) + _unit;
            text.color = _color;
        }
    }
}
