using Freznel.FzAdditions.UI.Controls;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Freznel.FzAdditions.UI.CustomPrefabs
{
    public class FzJumpReactorTapGUI : GUIData
    {
        private static readonly float RateStep = 100f;
        private static readonly float DisplayInterval = 0.5f; //Real seconds between refreshes, counting the cells scans the ship

        public static GameObject CreatePrefab()
        {
            GameObject root = Controls.Panel.CreateContainer("FzJumpReactorTapGUI", 0.302f, 0.329f, 0.349f, 0.335f);
            root.AddComponent<Animator>();
            root.AddComponent<FzJumpReactorTapGUI>();

            GameObject mainPanel = Controls.Panel.CreatePanel("MainPanel", 0.662f, 1f, 0f, 0f, root);
            Controls.Panel.AddTitle(mainPanel, "JUMP DRIVE REACTOR TAP", 0.1f);
            FzNumeric.Create(mainPanel, "CellsDisplay", 0.5f, 0.115f, 0.05f, 0.69f, LampColor.Yellow, null, "0", "CELLS");
            FzNumeric.Create(mainPanel, "ChargeDisplay", 0.5f, 0.115f, 0.05f, 0.525f, LampColor.Yellow, null, "0.0", "TOTAL CHARGE");
            FzNumeric.Create(mainPanel, "CapacityDisplay", 0.5f, 0.115f, 0.05f, 0.36f, LampColor.Yellow, null, "0.0", "TOTAL CAPACITY");
            FzNumeric.Create(mainPanel, "RateDisplay", 0.5f, 0.115f, 0.05f, 0.195f, LampColor.Yellow, null, "0.0", "CHARGE RATE");
            FzNumeric.Create(mainPanel, "TimeDisplay", 0.5f, 0.115f, 0.05f, 0.03f, LampColor.Yellow, null, "0", "CHARGE TIME");
            FzSlider.Create(mainPanel, "RateSlider", 0.14f, 0.7f, 0.7f, 0.08f, 0f, (float)Constants.DefaultTapChargeRate, RateStep, topLabel: "RATE", bottomLabel: "MIN");

            GameObject powerPanel = Controls.Panel.CreatePanel("PowerPanel", 0.305f, 1f, 0.695f, 0f, root);
            Controls.Panel.AddTitle(powerPanel, "POWER", 0.12f);
            FzLamp.Create(powerPanel, "PwrLamp", 0.8f, 0.12f, 0.1f, 0.73f, LampColor.Green, "POWER", stretch: true);
            FzLamp.Create(powerPanel, "FusingLamp", 0.8f, 0.12f, 0.1f, 0.59f, LampColor.Green, "FUSING", stretch: true);
            FzLamp.Create(powerPanel, "ChargingLamp", 0.8f, 0.12f, 0.1f, 0.45f, LampColor.Blue, "CHARGING", stretch: true);
            FzRocker.Create(powerPanel, "ChargeRocker", 0.33f, 0.28f, 0.335f, 0.05f, topLabel: "CHARGE", showIO: true);

            return root;
        }

        private FzLamp pwrLamp;
        private FzLamp fusingLamp;
        private FzLamp chargingLamp;
        private FzNumeric cellsDisplay;
        private FzNumeric chargeDisplay;
        private FzNumeric capacityDisplay;
        private FzNumeric rateDisplay;
        private FzNumeric timeDisplay;
        private float nextDisplayTime;

        public override void Init(CondOwner coSelf, Dictionary<string, string> dict, string strCOKey)
        {
            base.Init(coSelf, dict, strCOKey);

            pwrLamp = UnityUtil.FindDescendant(gameObject.transform, "PwrLamp").GetComponent<FzLamp>();
            fusingLamp = UnityUtil.FindDescendant(gameObject.transform, "FusingLamp").GetComponent<FzLamp>();
            chargingLamp = UnityUtil.FindDescendant(gameObject.transform, "ChargingLamp").GetComponent<FzLamp>();

            FzRocker chargeRocker = UnityUtil.FindDescendant(gameObject.transform, "ChargeRocker").GetComponent<FzRocker>();
            chargeRocker.SetOnWithoutNotify(JumpTap.IsChargeEnabled(COSelf));
            chargeRocker.OnChanged += (bool val) => SetPropMapData(Constants.TapChargeProp, val.ToString().ToLower());

            FzSlider rateSlider = UnityUtil.FindDescendant(gameObject.transform, "RateSlider").GetComponent<FzSlider>();
            rateSlider.SetRange(0f, (float)JumpTap.GetMaxRate(COSelf), RateStep);
            rateSlider.SetValueWithoutNotify((float)JumpTap.GetRateSetting(COSelf));
            rateSlider.OnChanged += (float val) => SetPropMapData(Constants.TapRateProp, val.ToString(CultureInfo.InvariantCulture));

            cellsDisplay = UnityUtil.FindDescendant(gameObject.transform, "CellsDisplay").GetComponent<FzNumeric>();
            chargeDisplay = UnityUtil.FindDescendant(gameObject.transform, "ChargeDisplay").GetComponent<FzNumeric>();
            chargeDisplay.Formatter = FzNumeric.FormatEnergy;
            capacityDisplay = UnityUtil.FindDescendant(gameObject.transform, "CapacityDisplay").GetComponent<FzNumeric>();
            capacityDisplay.Formatter = FzNumeric.FormatEnergy;
            rateDisplay = UnityUtil.FindDescendant(gameObject.transform, "RateDisplay").GetComponent<FzNumeric>();
            rateDisplay.Formatter = FzNumeric.FormatPower;
            timeDisplay = UnityUtil.FindDescendant(gameObject.transform, "TimeDisplay").GetComponent<FzNumeric>();
            timeDisplay.Formatter = FzNumeric.FormatDuration;

            UpdateLamps();
            UpdateDisplays();
        }

        private void Update()
        {
            if (pwrLamp == null) return;
            UpdateLamps();
            if (Time.unscaledTime >= nextDisplayTime) UpdateDisplays();
        }

        private void UpdateLamps()
        {
            if (COSelf == null) return;
            pwrLamp.On = COSelf.HasCond("IsPowered");
            fusingLamp.On = ReactorFuel.IsFusing(COSelf.ship?.Reactor);
            chargingLamp.On = JumpTap.IsCharging(COSelf);
        }

        private void UpdateDisplays()
        {
            nextDisplayTime = Time.unscaledTime + DisplayInterval;
            if (COSelf == null) return;

            List<CondOwner> cells = JumpCell.GetCells(COSelf.ship);
            List<CondOwner> charging = cells.Where(JumpCell.IsTapCharging).ToList();
            double rate = charging.Sum(JumpCell.GetCurrentRate);
            double missing = charging.Sum(JumpCell.GetMissingCharge);

            SetDisplay(cellsDisplay, cells.Count);
            SetDisplay(chargeDisplay, cells.Sum(cell => cell.GetCondAmount(Constants.CellChargeCond)));
            SetDisplay(capacityDisplay, cells.Sum(JumpCell.GetCapacity));
            SetDisplay(rateDisplay, rate);
            SetDisplay(timeDisplay, rate > 0.0 ? Math.Ceiling(missing / rate * 3600.0) : -1.0);
        }

        //The text only needs rebuilding when the value has moved
        private static void SetDisplay(FzNumeric display, double value)
        {
            if (display.Value != (float)value) display.Value = (float)value;
        }

    }
}
