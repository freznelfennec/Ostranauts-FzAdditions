using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    public static class CustomPrefabRegistry
    {

        private static Dictionary<string, GameObject> Prefabs = new();
        private static Dictionary<string, Func<GameObject>> Factories = new();

        static CustomPrefabRegistry()
        {
            Factories.Add("FzTestGUI", FzTestGUI.CreatePrefab);
        }

        //Each section adds its own GUIs when the plugin loads
        public static void Register(string name, Func<GameObject> factory)
        {
            Factories[name] = factory;
        }

        public static GameObject GetPrefab(string name)
        {
            if (Prefabs.ContainsKey(name) && Prefabs[name] != null) return Prefabs[name];
            if (!Factories.ContainsKey(name)) return null;
            GameObject prefab = Factories[name]();
            if (prefab == null) return null;
            if (Prefabs.ContainsKey(name)) Prefabs[name] = prefab; else Prefabs.Add(name, prefab);
            return prefab;
        }


    }
}
