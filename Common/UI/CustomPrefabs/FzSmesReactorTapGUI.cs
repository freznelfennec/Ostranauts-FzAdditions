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
    public class FzSmesReactorTapGUI : GUIData
    {
        private static readonly float RateStep = 100f;
        private static readonly float DisplayInterval = 0.5f; //Real seconds between refreshes, counting the units scans the ship

        public static GameObject CreatePrefab()
        {
            GameObject root = Controls.Panel.CreateContainer("FzSmesReactorTapGUI", 0.302f, 0.329f, 0.349f, 0.335f);
            root.AddComponent<Animator>();
            root.AddComponent<FzSmesReactorTapGUI>();

            GameObject mainPanel = Controls.Panel.CreatePanel("MainPanel", 0.662f, 1f, 0f, 0f, root);
            Controls.Panel.AddTitle(mainPanel, "SMES REACTOR TAP", 0.1f);
            FzNumeric.Create(mainPanel, "UnitsDisplay", 0.5f, 0.115f, 0.05f, 0.69f, LampColor.Yellow, null, "0", "SMES UNITS");
            FzNumeric.Create(mainPanel, "ChargeDisplay", 0.5f, 0.115f, 0.05f, 0.525f, LampColor.Yellow, null, "0.0", "TOTAL CHARGE");
            FzNumeric.Create(mainPanel, "CapacityDisplay", 0.5f, 0.115f, 0.05f, 0.36f, LampColor.Yellow, null, "0.0", "TOTAL CAPACITY");
            FzNumeric.Create(mainPanel, "RateDisplay", 0.5f, 0.115f, 0.05f, 0.195f, LampColor.Yellow, null, "0.0", "CHARGE RATE");
            FzNumeric.Create(mainPanel, "TimeDisplay", 0.5f, 0.115f, 0.05f, 0.03f, LampColor.Yellow, null, "0", "CHARGE TIME");
            FzSlider.Create(mainPanel, "RateSlider", 0.14f, 0.7f, 0.7f, 0.08f, 0f, (float)SmesConstants.DefaultTapChargeRate, RateStep, topLabel: "RATE", bottomLabel: "MIN");

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
        private FzNumeric unitsDisplay;
        private FzNumeric chargeDisplay;
        private FzNumeric capacityDisplay;
        private FzNumeric rateDisplay;
        private FzNumeric timeDisplay;
        private float nextDisplayTime;

        public override void Init(CondOwner coSelf, Dictionary<string, string> dict, string strCOKey)
        {
            base.Init(coSelf, dict, strCOKey);

            pwrLamp = UnityUtil.FindDescendant<FzLamp>(gameObject.transform, "PwrLamp");
            fusingLamp = UnityUtil.FindDescendant<FzLamp>(gameObject.transform, "FusingLamp");
            chargingLamp = UnityUtil.FindDescendant<FzLamp>(gameObject.transform, "ChargingLamp");

            FzRocker chargeRocker = UnityUtil.FindDescendant<FzRocker>(gameObject.transform, "ChargeRocker");
            chargeRocker.SetOnWithoutNotify(SmesTap.IsChargeEnabled(COSelf));
            chargeRocker.OnChanged += (bool val) => SetPropMapData(SmesConstants.TapChargeProp, val.ToString().ToLower());

            FzSlider rateSlider = UnityUtil.FindDescendant<FzSlider>(gameObject.transform, "RateSlider");
            rateSlider.SetRange(0f, (float)SmesTap.GetMaxRate(COSelf), RateStep);
            rateSlider.SetValueWithoutNotify((float)SmesTap.GetRateSetting(COSelf));
            rateSlider.OnChanged += (float val) => SetPropMapData(SmesConstants.TapRateProp, val.ToString(CultureInfo.InvariantCulture));

            unitsDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "UnitsDisplay");
            chargeDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "ChargeDisplay");
            chargeDisplay.Formatter = FzNumeric.FormatEnergy;
            capacityDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "CapacityDisplay");
            capacityDisplay.Formatter = FzNumeric.FormatEnergy;
            rateDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "RateDisplay");
            rateDisplay.Formatter = FzNumeric.FormatPower;
            timeDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "TimeDisplay");
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
            chargingLamp.On = SmesTap.IsCharging(COSelf);
        }

        private void UpdateDisplays()
        {
            nextDisplayTime = Time.unscaledTime + DisplayInterval;
            if (COSelf == null) return;

            List<CondOwner> units = Smes.GetUnits(COSelf.ship);
            List<CondOwner> charging = units.Where(Smes.IsTapCharging).ToList();
            double rate = charging.Sum(Smes.GetCurrentRate);
            double missing = charging.Sum(Smes.GetMissingCharge);

            unitsDisplay.Value = units.Count;
            chargeDisplay.Value = (float)Smes.GetTotalCharge(units);
            capacityDisplay.Value = (float)units.Sum(Smes.GetCapacity);
            rateDisplay.Value = (float)rate;
            timeDisplay.Value = rate > 0.0 ? (float)Math.Ceiling(missing / rate * SmesConstants.SecondsPerHour) : -1f;
        }

    }
}
