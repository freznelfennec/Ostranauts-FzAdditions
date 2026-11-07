using BepInEx;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.Util
{
    //Dumps a piece of the game's UI so the mod's own UI can match it: every object under it (hidden ones too) with its
    //rect, its components, and for images their sprite, texture, slicing and colour, for text its font, size and
    //colour; and every sprite it uses saved as a PNG. Written to BepInEx/FzUIDump/<name>/ by 'fz dumpui'.
    //
    //Sprites drawn by the game's scene, such as the PDA's, can't be loaded from Resources; a mod reuses them by finding
    //the live object at runtime (GUIPDA.instance) and taking the sprite off it, so the dump lists each sprite's path.
    public static class UIDump
    {
        public static string Dump(GameObject root, string name, out int objects, out int sprites)
        {
            string folder = Path.Combine(Paths.BepInExRootPath, "FzUIDump", Safe(name));
            string spriteFolder = Path.Combine(folder, "sprites");
            Directory.CreateDirectory(spriteFolder);
            var text = new StringBuilder();
            var saved = new Dictionary<Sprite, string>();
            objects = 0;
            Walk(root.transform, 0, "", text, saved, spriteFolder, ref objects);
            File.WriteAllText(Path.Combine(folder, "hierarchy.txt"), text.ToString());

            var index = new StringBuilder("sprite\ttexture\ttexture size\tsprite rect\tborder (l,b,r,t)\tfile\tfirst used at\r\n");
            foreach (var pair in spriteUses) index.Append(pair.Value).Append("\r\n");
            File.WriteAllText(Path.Combine(folder, "sprites.tsv"), index.ToString());
            sprites = saved.Count;
            spriteUses.Clear();
            return folder;
        }

        private static readonly Dictionary<Sprite, string> spriteUses = new Dictionary<Sprite, string>();

        private static void Walk(Transform t, int depth, string path, StringBuilder text, Dictionary<Sprite, string> saved, string spriteFolder, ref int objects)
        {
            objects++;
            string here = path.Length == 0 ? t.name : path + "/" + t.name;
            string indent = new string(' ', depth * 2);
            text.Append(indent).Append(t.name).Append(t.gameObject.activeSelf ? "" : "  [inactive]").Append("\r\n");

            if (t is RectTransform rt)
            {
                text.Append(indent).Append("  rect: anchors ").Append(V(rt.anchorMin)).Append("-").Append(V(rt.anchorMax))
                    .Append(" pivot ").Append(V(rt.pivot)).Append(" pos ").Append(V(rt.anchoredPosition))
                    .Append(" sizeDelta ").Append(V(rt.sizeDelta)).Append(" size ").Append(V(rt.rect.size)).Append("\r\n");
            }
            var components = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToList();
            if (components.Count > 0) text.Append(indent).Append("  components: ").Append(string.Join(", ", components)).Append("\r\n");

            Image image = t.GetComponent<Image>();
            if (image != null)
            {
                text.Append(indent).Append("  image: sprite ").Append(image.sprite != null ? image.sprite.name : "none")
                    .Append(" type ").Append(image.type).Append(" color ").Append(C(image.color))
                    .Append(image.material != null && image.material != image.defaultMaterial ? " material " + image.material.name : "")
                    .Append(image.raycastTarget ? " raycast" : "").Append("\r\n");
                if (image.sprite != null) SaveSprite(image.sprite, here, saved, spriteFolder);
            }
            RawImage raw = t.GetComponent<RawImage>();
            if (raw != null) text.Append(indent).Append("  rawimage: texture ").Append(raw.texture != null ? raw.texture.name : "none").Append(" color ").Append(C(raw.color)).Append("\r\n");

            TMP_Text tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                string sample = (tmp.text ?? "").Replace("\r", " ").Replace("\n", " | ");
                if (sample.Length > 80) sample = sample.Substring(0, 80) + "...";
                text.Append(indent).Append("  text: \"").Append(sample).Append("\" font ").Append(tmp.font != null ? tmp.font.name : "none")
                    .Append(" material ").Append(tmp.fontSharedMaterial != null ? tmp.fontSharedMaterial.name : "none")
                    .Append(" size ").Append(F(tmp.fontSize)).Append(tmp.enableAutoSizing ? $" auto {F(tmp.fontSizeMin)}-{F(tmp.fontSizeMax)}" : "")
                    .Append(" style ").Append(tmp.fontStyle).Append(" align ").Append(tmp.alignment).Append(" color ").Append(C(tmp.color)).Append("\r\n");
            }
            Text legacy = t.GetComponent<Text>();
            if (legacy != null) text.Append(indent).Append("  legacy text: \"").Append(legacy.text).Append("\" font ").Append(legacy.font != null ? legacy.font.name : "none").Append(" size ").Append(legacy.fontSize).Append(" color ").Append(C(legacy.color)).Append("\r\n");

            Selectable sel = t.GetComponent<Selectable>();
            if (sel != null)
            {
                text.Append(indent).Append("  ").Append(sel.GetType().Name).Append(": transition ").Append(sel.transition);
                if (sel.transition == Selectable.Transition.SpriteSwap)
                {
                    SpriteState s = sel.spriteState;
                    text.Append(" highlighted ").Append(s.highlightedSprite?.name ?? "none").Append(" pressed ").Append(s.pressedSprite?.name ?? "none")
                        .Append(" selected ").Append(s.selectedSprite?.name ?? "none").Append(" disabled ").Append(s.disabledSprite?.name ?? "none");
                    foreach (Sprite sp in new[] { s.highlightedSprite, s.pressedSprite, s.selectedSprite, s.disabledSprite })
                    {
                        if (sp != null) SaveSprite(sp, here, saved, spriteFolder);
                    }
                }
                else if (sel.transition == Selectable.Transition.ColorTint)
                {
                    ColorBlock b = sel.colors;
                    text.Append(" normal ").Append(C(b.normalColor)).Append(" highlighted ").Append(C(b.highlightedColor)).Append(" pressed ").Append(C(b.pressedColor)).Append(" disabled ").Append(C(b.disabledColor));
                }
                text.Append("\r\n");
            }
            LayoutGroup layout = t.GetComponent<LayoutGroup>();
            if (layout != null) text.Append(indent).Append("  layout: ").Append(layout.GetType().Name).Append(" padding ").Append(layout.padding).Append("\r\n");

            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, here, text, saved, spriteFolder, ref objects);
        }

        //Saves a sprite's own part of its texture as a PNG. Textures the game keeps unreadable are copied through a
        //render texture first.
        private static void SaveSprite(Sprite sprite, string usedAt, Dictionary<Sprite, string> saved, string folder)
        {
            if (saved.ContainsKey(sprite)) return;
            string file = Safe(sprite.name) + ".png";
            int n = 1;
            while (saved.ContainsValue(file)) file = Safe(sprite.name) + "_" + (++n) + ".png";
            saved[sprite] = file;
            Texture2D tex = sprite.texture;
            Rect r = sprite.textureRect;
            Vector4 border = sprite.border;
            spriteUses[sprite] = $"{sprite.name}\t{(tex != null ? tex.name : "none")}\t{(tex != null ? tex.width + "x" + tex.height : "")}\t{F(r.x)},{F(r.y)} {F(r.width)}x{F(r.height)}\t{F(border.x)},{F(border.y)},{F(border.z)},{F(border.w)}\t{file}\t{usedAt}";
            if (tex == null) return;
            try
            {
                RenderTexture rtex = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture previous = RenderTexture.active;
                Graphics.Blit(tex, rtex);
                RenderTexture.active = rtex;
                int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
                copy.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rtex);
                File.WriteAllBytes(Path.Combine(folder, file), ImageConversion.EncodeToPNG(copy));
                UnityEngine.Object.Destroy(copy);
            }
            catch (Exception ex)
            {
                FzAdditions.Logger.LogWarning($"Couldn't save sprite {sprite.name}: {ex.Message}");
            }
        }

        private static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "unnamed") sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.Length == 0 ? "unnamed" : sb.ToString();
        }

        private static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
        private static string V(Vector2 v) => "(" + F(v.x) + "," + F(v.y) + ")";
        private static string C(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
    }
}
