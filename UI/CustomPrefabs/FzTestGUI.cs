using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    public class FzTestGUI : GUIData
    {

        public static GameObject CreatePrefab()
        {
            GameObject mainPanel = Controls.Panel.CreatePanel("FzGUITest", 0.6f, 0.5f, 0.2f, 0.25f);
            mainPanel.AddComponent<Animator>();
            mainPanel.AddComponent<FzTestGUI>();

            return mainPanel;
        }





    }
}
