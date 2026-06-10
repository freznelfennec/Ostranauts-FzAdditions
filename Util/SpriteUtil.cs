using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.Util
{
    public static class SpriteUtil
    {
        private static readonly string DataModName = "FzAdditions";

        private static Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        private static Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

        public static Sprite FindSprite(string prefabName, string spriteName)
        {
            string key = prefabName + ':' + spriteName;
            if (spriteCache.ContainsKey(key)) return spriteCache[key];

            if (!prefabCache.TryGetValue(prefabName, out GameObject prefab))
            {
                prefab = Resources.Load<GameObject>(prefabName);
                prefabCache.Add(prefabName, prefab);
            }

            if (prefab == null)
            {
                FzAdditions.Logger.LogError("Failed to find prefab " + prefabName);
                return null;
            }

            Sprite result = FindSpriteRecurse(prefab, spriteName);
            if (result != null) spriteCache[key] = result; else FzAdditions.Logger.LogError("Failed to find sprite " + spriteName + " in prefab " + prefabName);
            return result;
        }

        private static Sprite FindSpriteRecurse(GameObject parent, string spriteName)
        {
            Image image = parent.GetComponent<Image>();
            if (image != null && image.sprite != null && image.sprite.name == spriteName) return image.sprite;

            for (int i = 0; i < parent.transform.childCount; i++)
            {
                GameObject child = parent.transform.GetChild(i)?.gameObject;
                if (child != null)
                {
                    var result = FindSpriteRecurse(child, spriteName);
                    if (result != null) return result;
                }
            }
            return null;
        }

        public static Sprite LoadModSprite(string name)
        {
            string spritePath = Path.Combine(Application.dataPath, "Mods", DataModName, "images", name);
            
            if (!File.Exists(spritePath))
            {
                FzAdditions.Logger.LogError("Could not find sprite image at " + spritePath);
                return null;
            }

            Texture2D texture = new Texture2D(1, 1);

            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(spritePath)))
            {
                FzAdditions.Logger.LogError("Failed to load image " + spritePath);
                return null;
            }

            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        public static Color ColorFromHex(string color) => ColorUtility.TryParseHtmlString(color, out var result) ? result : Color.white;
    }
}
