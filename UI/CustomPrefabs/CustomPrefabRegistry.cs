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

        public static Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

        static CustomPrefabRegistry()
        {
            Prefabs.Add("FzTestGUI", FzTestGUI.CreatePrefab());




            FzAdditions.Logger.LogInfo("Created custom GUI prefabs");
        }


    }
}
