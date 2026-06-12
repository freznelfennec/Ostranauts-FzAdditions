using Freznel.FzAdditions.UI.Controls;
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
            GameObject mainPanel = Controls.Panel.CreatePanel("FzGUITest", 0.5f, 0.7f, 0.25f, 0.15f);
            mainPanel.AddComponent<Animator>();
            mainPanel.AddComponent<FzTestGUI>();

            var btn = LampBtn.Create(mainPanel, "TestLampBtn", "TEST\nBTN", 0.15f, 0.2f, 0.02f, 0.025f);

            return mainPanel;
        }

        private LampBtn testBtn;

        public override void Init(CondOwner coSelf, Dictionary<string, string> dict, string strCOKey)
        {
            base.Init(coSelf, dict, strCOKey);

            //Test lamp button

            testBtn = UnityUtil.FindDescendant(gameObject.transform, "TestLampBtn").GetComponent<LampBtn>();
            testBtn.OnChanged += (bool val) =>
            {
                FzAdditions.Logger.LogInfo($"Btn state: ${val}");
                SetPropMapData("bTest", val.ToString().ToLower());
            };

            bool testBtnState = GetPropMapData("bTest", false);
            testBtn.On = testBtnState;










        }


    }
}
