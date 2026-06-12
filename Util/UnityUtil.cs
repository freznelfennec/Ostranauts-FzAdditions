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
                return FindDescendant(child, name);
            }
            return null;
        }



    }
}
