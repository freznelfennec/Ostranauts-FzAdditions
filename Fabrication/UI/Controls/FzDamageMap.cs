using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.Controls
{
    //Top down plan of a ship for its damage recorder, drawn into a texture: floors, walls and fixtures in greys, damaged
    //items yellow, and the losses on the log red, or blue once a build order is queued for them
    public class FzDamageMap : FzUIElement
    {
        private const int TextureWidth = 256;
        private const float Padding = 0.04f; //Of the view, kept clear around the edges

        public static readonly Color32 Background = new Color32(0, 0, 0, 255);
        public static readonly Color32 FloorColor = new Color32(34, 40, 46, 255);
        public static readonly Color32 FixtureColor = new Color32(62, 72, 80, 255);
        public static readonly Color32 WallColor = new Color32(96, 108, 118, 255);
        public static readonly Color32 DamagedColor = new Color32(232, 190, 40, 255);
        public static readonly Color32 LostColor = new Color32(255, 60, 48, 255);
        public static readonly Color32 QueuedColor = new Color32(80, 196, 255, 255);

        private Texture2D texture;
        private Color32[] pixels;
        private int textureHeight;

        public static GameObject Create(GameObject parent, string name, float width, float height, float offsetX, float offsetY)
        {
            GameObject map = new GameObject(name, typeof(CanvasRenderer), typeof(RectTransform), typeof(RawImage), typeof(FzDamageMap));
            map.layer = LayerMask.NameToLayer("UI");
            if (parent != null) map.GetComponent<RectTransform>().SetParent(parent.GetComponent<RectTransform>(), false);
            map.GetComponent<FzDamageMap>().SetTransform(width, height, offsetX, offsetY);
            map.GetComponent<RawImage>().raycastTarget = false;
            return map;
        }

        private struct Box
        {
            public Vector2 Centre, Size;
            public Color32 Color;
            public int Layer;
        }

        //Redraws the plan from the ship as it stands and its recorder's log
        public void Draw(Ship ship, List<DamageRecorder.Record> records)
        {
            if (!PrepareTexture()) return;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Background;

            var boxes = new List<Box>();
            if (ship != null && !ship.bDestroyed)
            {
                foreach (CondOwner co in ship.GetCOs(null, bSubObjects: false, bAllowDocked: false, bAllowLocked: true) ?? new List<CondOwner>())
                {
                    if (co == null || co.bDestroyed || co.Item == null || !co.HasCond("IsInstalled")) continue;
                    string name = co.strName ?? "";
                    if (name.EndsWith("_Placeholder")) continue;
                    bool wall = name.StartsWith("ItmWall"), floor = name.StartsWith("ItmFloor");
                    Color32 color = co.HasCond("IsDamaged") ? DamagedColor : wall ? WallColor : floor ? FloorColor : FixtureColor;
                    int layer = co.HasCond("IsDamaged") ? 3 : wall ? 2 : floor ? 0 : 1;
                    boxes.Add(new Box { Centre = co.tf.position, Size = new Vector2(co.Item.nWidthInTiles, co.Item.nHeightInTiles), Color = color, Layer = layer });
                }
                foreach (DamageRecorder.Record record in records ?? new List<DamageRecorder.Record>())
                {
                    Vector2Int size = Fabricator.Size(record.CO);
                    bool turned = Mathf.RoundToInt(Mathf.Repeat(record.Rotation, 180f) / 90f) % 2 == 1;
                    boxes.Add(new Box
                    {
                        Centre = record.WorldPos(ship),
                        Size = turned ? new Vector2(size.y, size.x) : new Vector2(size.x, size.y),
                        Color = record.Queued ? QueuedColor : LostColor,
                        Layer = 4
                    });
                }
            }

            if (boxes.Count > 0)
            {
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
                foreach (Box box in boxes)
                {
                    min = Vector2.Min(min, box.Centre - box.Size / 2f);
                    max = Vector2.Max(max, box.Centre + box.Size / 2f);
                }
                Vector2 centre = (min + max) / 2f;
                Vector2 span = Vector2.Max(max - min, Vector2.one);
                float scale = Mathf.Min(TextureWidth / span.x, textureHeight / span.y) * (1f - Padding * 2f);
                foreach (Box box in boxes.OrderBy(b => b.Layer)) Fill(box, centre, scale);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
        }

        private void Fill(Box box, Vector2 centre, float scale)
        {
            Vector2 low = (box.Centre - box.Size / 2f - centre) * scale;
            Vector2 high = (box.Centre + box.Size / 2f - centre) * scale;
            int x0 = Mathf.FloorToInt(TextureWidth / 2f + low.x), x1 = Mathf.CeilToInt(TextureWidth / 2f + high.x) - 1;
            int y0 = Mathf.FloorToInt(textureHeight / 2f + low.y), y1 = Mathf.CeilToInt(textureHeight / 2f + high.y) - 1;
            x1 = Math.Max(x1, x0);
            y1 = Math.Max(y1, y0);
            for (int y = Math.Max(0, y0); y <= Math.Min(textureHeight - 1, y1); y++)
            {
                for (int x = Math.Max(0, x0); x <= Math.Min(TextureWidth - 1, x1); x++) pixels[y * TextureWidth + x] = box.Color;
            }
        }

        //Fits the control's shape, which is only known once it's on screen
        private bool PrepareTexture()
        {
            Rect rect = GetComponent<RectTransform>().rect;
            if (rect.width <= 0f || rect.height <= 0f) return texture != null;
            int height = Mathf.Max(Mathf.RoundToInt(TextureWidth * rect.height / rect.width), 1);
            if (texture != null && height == textureHeight) return true;
            if (texture != null) Destroy(texture);
            textureHeight = height;
            texture = new Texture2D(TextureWidth, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[TextureWidth * height];
            GetComponent<RawImage>().texture = texture;
            return true;
        }

        private void OnDestroy()
        {
            if (texture != null) Destroy(texture);
        }
    }
}
