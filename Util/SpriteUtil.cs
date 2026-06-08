using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.Util
{
    public static class SpriteUtil
    {
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
    }
}
