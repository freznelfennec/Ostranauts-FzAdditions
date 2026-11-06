using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    public class FzDistanceMeter : FzUIElement
    {
        private static readonly string SpritePrefab = "GUIShip/GUIOrbitDraw/NavModMooringControl";
        private static readonly string PointerSprite = "GUIArrow";
        private static readonly string PointerName = "Pointer";
        private static readonly string MarkerPrefix = "Marker";
        private static readonly string MarkerLabelPrefix = "MarkerLabel";
        private static readonly Color LineColor = SpriteUtil.ColorFromHex("#9F9F9FFF");
        private static readonly Color PointerColor = SpriteUtil.ColorFromHex("#DC9038FF");
        private static readonly float LineThickness = 0.025f;
        private static readonly float TrackStart = 0.05f;
        private static readonly float TrackEnd = 0.95f;
        private static readonly float LineY = 0.55f;
        private static readonly float MarkerHeight = 0.2f;
        private static readonly float MarkerThickness = 0.004f;
        private static readonly float PointerMinY = 0.62f;
        private static readonly float PointerMaxY = 0.98f;
        private static readonly float PointerWidth = 0.05f;
        private static readonly float LabelWidth = 0.1f;
        private static readonly float LabelHeight = 0.36f;
        private static readonly float LabelY = 0.06f;
        private static readonly float LabelMinFontSize = 5f;
        private static readonly float LabelMaxFontSize = 8f;

        private static readonly string StartLineName = "StartLine";
        private static readonly float StartLineMinY = 0.12f;
        private static readonly float StartLineMaxY = 0.96f;
        private static readonly float StartLineThickness = 0.004f;
        private static readonly string UnitName = "Unit";
        private static readonly float UnitWidth = 0.08f;
        private static readonly float UnitGap = 0.01f;

        public enum MeterDirection { LeftToRight, RightToLeft }

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY, float minDistance, float maxDistance, int markerCount = 0, MeterDirection direction = MeterDirection.LeftToRight, string unit = null)
        {
            GameObject meter = new GameObject(name, typeof(RectTransform), typeof(FzDistanceMeter));
            meter.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                meter.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

            FzDistanceMeter element = meter.GetComponent<FzDistanceMeter>();
            element.SetTransform(width, height, offsetX, offsetY);

            Panel.CreateScreen(meter);

            Image line = CreateImage(meter, "Line", null, LineColor);
            line.rectTransform.anchorMin = new Vector2(TrackStart, LineY - LineThickness / 2f);
            line.rectTransform.anchorMax = new Vector2(TrackEnd, LineY + LineThickness / 2f);
            line.rectTransform.sizeDelta = Vector2.zero;

            Image startLine = CreateImage(meter, StartLineName, null, PointerColor);
            startLine.rectTransform.sizeDelta = Vector2.zero;

            //The arrow art points up, it has to point down at the line
            Image pointer = CreateImage(meter, PointerName, SpriteUtil.FindSprite(SpritePrefab, PointerSprite), PointerColor);
            pointer.preserveAspect = true;
            pointer.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);

            element._direction = direction;
            element.UpdateStartLine();
            element.SetRange(minDistance, maxDistance);
            element.MarkerCount = markerCount;
            element.Unit = unit;

            return meter;
        }

        private static Image CreateImage(GameObject parent, string name, Sprite sprite, Color color)
        {
            GameObject item = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(Image));
            item.layer = parent.layer;
            item.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);

            Image image = item.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        [SerializeField]
        private float _min;
        [SerializeField]
        private float _max;
        [SerializeField]
        private float _distance;
        [SerializeField]
        private int _markerCount;
        [SerializeField]
        private MeterDirection _direction;

        public MeterDirection Direction
        {
            get => _direction;
            set
            {
                _direction = value;
                UpdateStartLine();
                UpdatePointer();
                UpdateMarkers();
                UpdateUnit();
            }
        }

        //Null or empty hides the label
        public string Unit
        {
            get
            {
                Transform unit = transform.Find(UnitName);
                return unit != null && unit.gameObject.activeSelf ? unit.GetComponent<TMP_Text>().text : null;
            }
            set
            {
                Transform unit = transform.Find(UnitName);
                if (string.IsNullOrEmpty(value))
                {
                    if (unit != null) unit.gameObject.SetActive(false);
                    return;
                }

                if (unit == null) unit = Label.Create(gameObject, UnitName, value, UnitWidth, LabelHeight, 0f, LabelY, LabelMinFontSize, LabelMaxFontSize).transform;
                unit.GetComponent<TMP_Text>().text = value;
                unit.gameObject.SetActive(true);
                UpdateUnit();
            }
        }

        public float MinDistance => _min;

        public float MaxDistance => _max;

        public float Distance
        {
            get => _distance;
            set
            {
                _distance = Mathf.Clamp(value, _min, _max);
                UpdatePointer();
            }
        }

        //Markers divide the range evenly, the last sits on the end of the range and none on the start
        public int MarkerCount
        {
            get => _markerCount;
            set
            {
                _markerCount = Mathf.Max(value, 0);
                UpdateMarkers();
            }
        }

        public void SetRange(float minDistance, float maxDistance)
        {
            if (maxDistance <= minDistance)
            {
                FzAdditions.Logger.LogError($"Invalid meter range {minDistance} to {maxDistance} on {name}");
                return;
            }

            _min = minDistance;
            _max = maxDistance;
            Distance = _distance;
            UpdateMarkers();
        }

        private float TrackPosition(float position)
        {
            if (_direction == MeterDirection.RightToLeft) position = 1f - position;
            return TrackStart + (TrackEnd - TrackStart) * position;
        }

        private void UpdateStartLine()
        {
            RectTransform startLine = transform.Find(StartLineName) as RectTransform;
            if (startLine == null) return;

            float position = TrackPosition(0f);
            startLine.anchorMin = new Vector2(position - StartLineThickness / 2f, StartLineMinY);
            startLine.anchorMax = new Vector2(position + StartLineThickness / 2f, StartLineMaxY);
            startLine.anchoredPosition = Vector2.zero;
        }

        //Sits beside the start line, where there is no mark label
        private void UpdateUnit()
        {
            Transform unit = transform.Find(UnitName);
            if (unit == null) return;

            bool reversed = _direction == MeterDirection.RightToLeft;
            float start = TrackPosition(0f);
            float offsetX = reversed ? start - UnitGap - UnitWidth : start + UnitGap;
            unit.GetComponent<FzUIElement>().SetTransform(UnitWidth, LabelHeight, offsetX, LabelY);
            unit.GetComponent<TMP_Text>().alignment = reversed ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        }

        private void UpdatePointer()
        {
            RectTransform pointer = transform.Find(PointerName) as RectTransform;
            if (pointer == null) return;

            float position = TrackPosition((_distance - _min) / (_max - _min));
            pointer.anchorMin = new Vector2(position - PointerWidth / 2f, PointerMinY);
            pointer.anchorMax = new Vector2(position + PointerWidth / 2f, PointerMaxY);
            pointer.sizeDelta = Vector2.zero;
            pointer.anchoredPosition = Vector2.zero;
        }

        private void UpdateMarkers()
        {
            foreach (Transform child in transform.Cast<Transform>().ToList())
            {
                if (!child.name.StartsWith(MarkerPrefix)) continue;
                child.SetParent(null);
                Destroy(child.gameObject);
            }

            for (int i = 1; i <= _markerCount; i++)
            {
                float fraction = (float)i / _markerCount;
                float position = TrackPosition(fraction);

                Image marker = CreateImage(gameObject, MarkerPrefix + i, null, LineColor);
                marker.rectTransform.anchorMin = new Vector2(position - MarkerThickness / 2f, LineY - MarkerHeight / 2f);
                marker.rectTransform.anchorMax = new Vector2(position + MarkerThickness / 2f, LineY + MarkerHeight / 2f);
                marker.rectTransform.sizeDelta = Vector2.zero;

                string text = (_min + (_max - _min) * fraction).ToString("0.##", CultureInfo.InvariantCulture);
                Label.Create(gameObject, MarkerLabelPrefix + i, text, LabelWidth, LabelHeight, position - LabelWidth / 2f, LabelY, LabelMinFontSize, LabelMaxFontSize);
            }

            //Keep the pointer drawn over the markers
            transform.Find(PointerName)?.SetAsLastSibling();
        }
    }
}
