using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    public class FzJumpEnergyCellGUI : GUIData
    {

        public static GameObject CreatePrefab()
        {
            GameObject root = Controls.Panel.CreateContainer("FzJumpEnergyCellGUI", 0.302f, 0.329f, 0.349f, 0.335f);
            root.AddComponent<Animator>();
            root.AddComponent<FzJumpEnergyCellGUI>();

            GameObject mainPanel = Controls.Panel.CreatePanel("MainPanel", 0.662f, 1f, 0f, 0f, root);
            Controls.Panel.AddTitle(mainPanel, "JUMP DRIVE ENERGY CELL", 0.1f);
            FzNumeric.Create(mainPanel, "ChargeDisplay", 0.5f, 0.14f, 0.05f, 0.655f, LampColor.Yellow, null, "0.0", "CHARGE");
            FzNumeric.Create(mainPanel, "CapacityDisplay", 0.5f, 0.14f, 0.05f, 0.45f, LampColor.Yellow, null, "0.0", "MAXIMUM");
            FzNumeric.Create(mainPanel, "RateDisplay", 0.5f, 0.14f, 0.05f, 0.245f, LampColor.Yellow, null, "0.0", "CHARGE RATE");
            FzNumeric.Create(mainPanel, "TimeDisplay", 0.5f, 0.14f, 0.05f, 0.04f, LampColor.Yellow, null, "0", "CHARGE TIME");
            LampBtn.Create(mainPanel, "ChargeBtn", "GRID\nCHARGE", 0.3f, 0.3f, 0.625f, 0.47f);
            FzLamp.Create(mainPanel, "ChargingLamp", 0.34f, 0.12f, 0.605f, 0.255f, LampColor.Blue, "CHARGING", stretch: true);

            GameObject powerPanel = Controls.Panel.CreatePanel("PowerPanel", 0.305f, 1f, 0.695f, 0f, root);
            Controls.Panel.AddTitle(powerPanel, "POWER", 0.12f);
            FzLamp.Create(powerPanel, "PwrLamp", 0.6f, 0.2f, 0.2f, 0.6f, LampColor.Green, "POWER");
            FzRocker.Create(powerPanel, "PwrRocker", 0.33f, 0.34f, 0.335f, 0.14f, showIO: true);

            return root;
        }

        private FzLamp pwrLamp;
        private FzLamp chargingLamp;
        private FzNumeric chargeDisplay;
        private FzNumeric capacityDisplay;
        private FzNumeric rateDisplay;
        private FzNumeric timeDisplay;

        public override void Init(CondOwner coSelf, Dictionary<string, string> dict, string strCOKey)
        {
            base.Init(coSelf, dict, strCOKey);

            FzRocker pwrRocker = UnityUtil.FindDescendant<FzRocker>(gameObject.transform, "PwrRocker");
            pwrRocker.SetOnWithoutNotify(!COSelf.HasCond("IsOverrideOff"));
            pwrRocker.OnChanged += SetPower;

            pwrLamp = UnityUtil.FindDescendant<FzLamp>(gameObject.transform, "PwrLamp");
            chargingLamp = UnityUtil.FindDescendant<FzLamp>(gameObject.transform, "ChargingLamp");
            UpdatePowerLamp();

            chargeDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "ChargeDisplay");
            chargeDisplay.Formatter = FzNumeric.FormatEnergy;
            capacityDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "CapacityDisplay");
            capacityDisplay.Formatter = FzNumeric.FormatEnergy;
            rateDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "RateDisplay");
            rateDisplay.Formatter = FzNumeric.FormatPower;
            timeDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "TimeDisplay");
            timeDisplay.Formatter = FzNumeric.FormatDuration;
            UpdateDisplays();

            //Set the state first, the On setter raises OnChanged
            LampBtn chargeBtn = UnityUtil.FindDescendant<LampBtn>(gameObject.transform, "ChargeBtn");
            chargeBtn.On = GetPropMapData(Constants.CellGridChargeProp, false);
            chargeBtn.OnChanged += (bool val) => SetPropMapData(Constants.CellGridChargeProp, val.ToString().ToLower());
        }

        //Power is lost and restored by the ship's circuits, not only by the rocker
        private void Update()
        {
            if (pwrLamp == null) return;
            UpdatePowerLamp();
            UpdateDisplays();
        }

        private void UpdateDisplays()
        {
            if (COSelf == null) return;
            chargeDisplay.Value = (float)JumpCell.GetCharge(COSelf);
            capacityDisplay.Value = (float)JumpCell.GetCapacity(COSelf);
            rateDisplay.Value = (float)JumpCell.GetCurrentRate(COSelf);
            timeDisplay.Value = (float)Math.Ceiling(JumpCell.GetChargeTime(COSelf));
        }

        private void UpdatePowerLamp()
        {
            if (COSelf == null) return;
            pwrLamp.On = COSelf.HasCond("IsPowered");
            chargingLamp.On = COSelf.HasCond("IsPowered") && JumpCell.GetCurrentRate(COSelf) > 0.0;
        }

        private void SetPower(bool on)
        {
            if (COSelf == null) return;
            COSelf.ZeroCondAmount("IsOverrideOff");
            COSelf.ZeroCondAmount("IsOverrideOn");
            COSelf.AddCondAmount(on ? "IsOverrideOn" : "IsOverrideOff", 1.0);
            JumpSequence.CancelForDriveChange(COSelf.ship);
        }

    }
}
