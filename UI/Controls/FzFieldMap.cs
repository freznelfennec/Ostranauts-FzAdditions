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
    //Top down plan of a ship's dock group and its jump field contour, drawn into a texture
    public class FzFieldMap : FzUIElement
    {
        private static readonly int TextureWidth = 320;
        private static readonly int CurveSegments = 12;
        private static readonly int LineThickness = 2;
        private static readonly int CrossSize = 4; //Pixels out from the emitter
        private static readonly float LabelWidth = 0.14f;
        private static readonly float LabelHeight = 0.09f;
        private static readonly float LabelOffset = 22f; //Pixels out from the contour
        private static readonly float LabelMinFontSize = 6f;
        private static readonly float LabelMaxFontSize = 9f;
        private static readonly float Padding = 0.06f; //Of the view, kept clear around the edges
        private static readonly Color32 BackgroundColor = new Color32(0, 0, 0, 255);
        private static readonly Color32 ObjectColor = new Color32(84, 96, 106, 255);
        private static readonly Color32 OutsideColor = new Color32(255, 40, 40, 255);

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY)
        {
            GameObject map = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(RawImage), typeof(FzFieldMap));
            map.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
            {
                map.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            }

            map.GetComponent<FzFieldMap>().SetTransform(width, height, offsetX, offsetY);
            map.GetComponent<RawImage>().raycastTarget = false;
            return map;
        }

        //Red at 0, yellow at 50, green at 80 and blue at 100
        public static Color GetStrengthColor(float strength)
        {
            if (strength <= 50f) return Color.Lerp(Color.red, Color.yellow, strength / 50f);
            if (strength <= 80f) return Color.Lerp(Color.yellow, Color.green, (strength - 50f) / 30f);
            return Color.Lerp(Color.green, Color.blue, (strength - 80f) / 20f);
        }

        private Texture2D texture;
        private Color32[] pixels;
        private int textureHeight;
        private readonly List<TMP_Text> strengthLabels = new List<TMP_Text>();

        //Each emitter's field strength, beside its stretch of the contour
        public bool ShowStrength { get; set; }

        //A white X over each emitter
        public bool ShowEmitters { get; set; }

        //Returns whether the ship has a contour
        public bool Render(Ship ship)
        {
            PrepareTexture();

            List<FieldContour.MapObject> objects = FieldContour.GetMapObjects(ship);
            List<Vector2> corners = FieldContour.Calculate(ship);
            List<FieldContour.Segment> segments = FieldContour.CalculateSegments(ship);
            List<Vector2> curve = FieldContour.Smooth(corners, CurveSegments);

            for (int i = 0; i < pixels.Length; i++) pixels[i] = BackgroundColor;

            int labels = 0;
            if (objects.Count > 0 || curve.Count > 0)
            {
                GetView(objects, curve, out Vector2 center, out float scale);

                foreach (FieldContour.MapObject mapObject in objects.OrderBy(entry => entry.Outside))
                {
                    DrawObject(mapObject, center, scale);
                }

                for (int i = 0; i < curve.Count; i++)
                {
                    //Each emitter holds the stretch either side of its own corner
                    int corner = Mathf.RoundToInt((float)i / CurveSegments) % segments.Count;
                    Color32 color = GetStrengthColor(segments[corner].Strength);
                    DrawLine(ToPixel(curve[i], center, scale), ToPixel(curve[(i + 1) % curve.Count], center, scale), color);
                }

                if (ShowEmitters)
                {
                    foreach (FieldContour.Emitter emitter in FieldContour.GetEmitters(ship))
                    {
                        DrawCross(ToPixel(emitter.Position, center, scale));
                    }
                }

                if (ShowStrength)
                {
                    foreach (FieldContour.Segment segment in segments)
                    {
                        PlaceStrengthLabel(labels++, segment, center, scale);
                    }
                }
            }

            for (int i = labels; i < strengthLabels.Count; i++) strengthLabels[i].gameObject.SetActive(false);

            texture.SetPixels32(pixels);
            texture.Apply();
            return curve.Count > 0;
        }

        //Fits the control's shape, which is only known once it is on screen
        private void PrepareTexture()
        {
            Rect rect = GetComponent<RectTransform>().rect;
            int height = rect.width > 0f ? Mathf.Max(Mathf.RoundToInt(TextureWidth * rect.height / rect.width), 1) : TextureWidth;
            if (texture != null && height == textureHeight) return;

            if (texture != null) Destroy(texture);
            textureHeight = height;
            texture = new Texture2D(TextureWidth, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[TextureWidth * height];
            GetComponent<RawImage>().texture = texture;
        }

        private void GetView(List<FieldContour.MapObject> objects, List<Vector2> curve, out Vector2 center, out float scale)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            foreach (Vector2 point in curve)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            foreach (FieldContour.MapObject mapObject in objects)
            {
                Vector2 half = new Vector2(mapObject.Width, mapObject.Height) / 2f;
                min = Vector2.Min(min, mapObject.Center - half);
                max = Vector2.Max(max, mapObject.Center + half);
            }

            center = (min + max) / 2f;
            Vector2 size = Vector2.Max(max - min, Vector2.one);
            scale = Mathf.Min(TextureWidth / size.x, textureHeight / size.y) * (1f - Padding * 2f);
        }

        private Vector2Int ToPixel(Vector2 world, Vector2 center, float scale)
        {
            Vector2 local = (world - center) * scale;
            return new Vector2Int(Mathf.RoundToInt(TextureWidth / 2f + local.x), Mathf.RoundToInt(textureHeight / 2f + local.y));
        }

        private void DrawObject(FieldContour.MapObject mapObject, Vector2 center, float scale)
        {
            Vector2 half = new Vector2(mapObject.Width, mapObject.Height) / 2f;
            Vector2Int min = ToPixel(mapObject.Center - half, center, scale);
            Vector2Int max = ToPixel(mapObject.Center + half, center, scale);
            Color32 color = mapObject.Outside ? OutsideColor : ObjectColor;

            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++) SetPixel(x, y, color);
            }
        }

        private void DrawLine(Vector2Int from, Vector2Int to, Color32 color)
        {
            int steps = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y), 1);
            for (int i = 0; i <= steps; i++)
            {
                Vector2Int point = Vector2Int.RoundToInt(Vector2.Lerp(from, to, (float)i / steps));
                for (int y = 0; y < LineThickness; y++)
                {
                    for (int x = 0; x < LineThickness; x++) SetPixel(point.x + x, point.y + y, color);
                }
            }
        }

        private void DrawCross(Vector2Int center)
        {
            for (int i = -CrossSize; i <= CrossSize; i++)
            {
                SetPixel(center.x + i, center.y + i, Color.white);
                SetPixel(center.x + i, center.y - i, Color.white);
                SetPixel(center.x + i + 1, center.y + i, Color.white);
                SetPixel(center.x + i + 1, center.y - i, Color.white);
            }
        }

        //Pushed away from the middle of the view, so it sits outside the contour line and clear of the ship
        private void PlaceStrengthLabel(int index, FieldContour.Segment segment, Vector2 center, float scale)
        {
            if (index >= strengthLabels.Count)
            {
                strengthLabels.Add(Label.Create(gameObject, "Strength" + index, "", LabelWidth, LabelHeight, 0f, 0f, LabelMinFontSize, LabelMaxFontSize).GetComponent<TMP_Text>());
                strengthLabels[index].color = Color.white;
            }

            Vector2 point = segment.Emitter.ContourPoint;
            Vector2Int pixel = ToPixel(point + (point - center).normalized * (LabelOffset / scale), center, scale);
            float x = Mathf.Clamp((float)pixel.x / TextureWidth, LabelWidth / 2f, 1f - LabelWidth / 2f);
            float y = Mathf.Clamp((float)pixel.y / textureHeight, LabelHeight / 2f, 1f - LabelHeight / 2f);

            TMP_Text label = strengthLabels[index];
            label.text = $"{segment.Strength:0}%";
            label.GetComponent<FzUIElement>().SetTransform(LabelWidth, LabelHeight, x - LabelWidth / 2f, y - LabelHeight / 2f);
            label.gameObject.SetActive(true);
        }

        private void SetPixel(int x, int y, Color32 color)
        {
            if (x >= 0 && y >= 0 && x < TextureWidth && y < textureHeight) pixels[x + y * TextureWidth] = color;
        }

        private void OnDestroy()
        {
            if (texture != null) Destroy(texture);
        }
    }
}
