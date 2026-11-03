using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Freznel.FzAdditions.Util
{
    public static class UnityUtil
    {

        public static Transform FindDescendant(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;
            foreach (Transform child in parent)
            {
                if (child.gameObject?.name == name) return child;
                Transform result = FindDescendant(child, name);
                if (result != null) return result;
            }
            return null;
        }

        //Null if there is no such descendant or it has no such component
        public static T FindDescendant<T>(Transform parent, string name) where T : Component
        {
            return FindDescendant(parent, name)?.GetComponent<T>();
        }
    }
}
