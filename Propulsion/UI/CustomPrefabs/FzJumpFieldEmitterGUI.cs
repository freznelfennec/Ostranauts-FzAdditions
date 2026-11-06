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
    public class FzJumpFieldEmitterGUI : GUIData
    {
        //Damage fraction where the ready lamp starts to flicker, and where it reaches full rate
        private static readonly float FlickerStartDamage = 0.2f;
        private static readonly float FlickerFullDamage = 0.6f;
        private static readonly float MinOffset = Constants.MinFieldOffset;


        public static GameObject CreatePrefab()
        {
            GameObject root = Controls.Panel.CreateContainer("FzJumpFieldEmitterGUI", 0.442f, 0.329f, 0.279f, 0.335f);
            root.AddComponent<Animator>();
            root.AddComponent<FzJumpFieldEmitterGUI>();

            GameObject fieldPanel = Controls.Panel.CreatePanel("FieldPanel", 0.769f, 1f, 0f, 0f, root);
            Controls.Panel.AddTitle(fieldPanel, "FIELD ADJUSTMENT", 0.1f);
            FzDistanceMeter.Create(fieldPanel, "OffsetMeter", 0.6f, 0.22f, 0.05f, 0.62f, 0f, Constants.DefaultMaxFieldOffset, 10, unit: "m");
            FzSlider.Create(fieldPanel, "OffsetSlider", 0.12f, 0.7f, 0.7f, 0.1f, MinOffset, Constants.DefaultMaxFieldOffset, topLabel: "OFFSET", bottomLabel: "MIN");
            FzSlider.Create(fieldPanel, "ReinforcementSlider", 0.12f, 0.7f, 0.85f, 0.1f, 0f, 1f, topLabel: "REINF", bottomLabel: "MIN");
            FzNumeric.Create(fieldPanel, "StrengthDisplay", 0.27f, 0.2f, 0.05f, 0.22f, LampColor.Yellow, "%", "0", "FIELD STRENGTH");
            FzNumeric.Create(fieldPanel, "EnergyDisplay", 0.27f, 0.2f, 0.36f, 0.22f, LampColor.Yellow, null, "0", "FIELD ENERGY");

            GameObject powerPanel = Controls.Panel.CreatePanel("PowerPanel", 0.208f, 1f, 0.792f, 0f, root);
            Controls.Panel.AddTitle(powerPanel, "POWER", 0.12f);
            FzLamp.Create(powerPanel, "PwrLamp", 0.6f, 0.2f, 0.2f, 0.645f, LampColor.Green, "POWER");
            FzLamp.Create(powerPanel, "ReadyLamp", 0.6f, 0.2f, 0.2f, 0.43f, LampColor.Green, "READY");
            FzRocker.Create(powerPanel, "PwrRocker", 0.33f, 0.34f, 0.335f, 0.06f, showIO: true);

            return root;
        }

        private FzRocker pwrRocker;
        private FzLamp pwrLamp;
        private FzLamp readyLamp;
        private FzNumeric strengthDisplay;
        private float fieldStrength;
        private FzSlider offsetSlider;
        private FzSlider reinforcementSlider;
        private FzNumeric energyDisplay;

        public override void Init(CondOwner coSelf, Dictionary<string, string> dict, string strCOKey)
        {
            base.Init(coSelf, dict, strCOKey);

            pwrRocker = UnityUtil.FindDescendant<FzRocker>(gameObject.transform, "PwrRocker");
            pwrRocker.SetOnWithoutNotify(!COSelf.HasCond("IsOverrideOff"));
            pwrRocker.OnChanged += SetPower;

            pwrLamp = UnityUtil.FindDescendant<FzLamp>(gameObject.transform, "PwrLamp");
            readyLamp = UnityUtil.FindDescendant<FzLamp>(gameObject.transform, "ReadyLamp");
            UpdatePowerLamp();

            float maxOffset = FieldContour.GetMaxOffset(COSelf);

            offsetSlider = UnityUtil.FindDescendant<FzSlider>(gameObject.transform, "OffsetSlider");
            offsetSlider.SetRange(MinOffset, maxOffset);
            offsetSlider.SetValueWithoutNotify(GetFloatProp(Constants.FieldOffsetProp, MinOffset));
            offsetSlider.OnChanged += (float val) => SetPropMapData(Constants.FieldOffsetProp, val.ToString(CultureInfo.InvariantCulture));

            FzDistanceMeter offsetMeter = UnityUtil.FindDescendant<FzDistanceMeter>(gameObject.transform, "OffsetMeter");
            offsetMeter.SetRange(0f, maxOffset);
            offsetMeter.Distance = offsetSlider.Value;
            offsetSlider.OnChanged += (float val) => offsetMeter.Distance = val;

            reinforcementSlider = UnityUtil.FindDescendant<FzSlider>(gameObject.transform, "ReinforcementSlider");
            reinforcementSlider.SetValueWithoutNotify(FieldContour.GetReinforcement(COSelf));
            reinforcementSlider.OnChanged += (float val) => SetPropMapData(Constants.FieldReinforcementProp, val.ToString(CultureInfo.InvariantCulture));

            energyDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "EnergyDisplay");
            energyDisplay.Formatter = FzNumeric.FormatEnergy;
            UpdateFieldEnergy();
            offsetSlider.OnChanged += (float val) => UpdateFieldEnergy();
            reinforcementSlider.OnChanged += (float val) => UpdateFieldEnergy();

            //Recalculating scans the whole ship, so wait until the slider is let go
            strengthDisplay = UnityUtil.FindDescendant<FzNumeric>(gameObject.transform, "StrengthDisplay");
            UpdateFieldStrength();
            offsetSlider.OnReleased += (float val) => UpdateFieldStrength();
            reinforcementSlider.OnReleased += (float val) => UpdateFieldStrength();
        }

        private float GetFloatProp(string key, float defaultValue)
        {
            return float.TryParse(GetPropMapData(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : defaultValue;
        }

        //Power is lost and restored by the ship's circuits, not only by the rocker
        private void Update()
        {
            if (pwrLamp != null) UpdatePowerLamp();
        }

        private void UpdateFieldEnergy()
        {
            energyDisplay.Value = FieldContour.GetFieldEnergy(COSelf, offsetSlider.Value);
        }

        private void UpdateFieldStrength()
        {
            fieldStrength = FieldContour.GetStrength(COSelf);
            strengthDisplay.Value = fieldStrength;
            UpdatePowerLamp();
        }

        private void UpdatePowerLamp()
        {
            if (COSelf == null) return;
            bool powered = COSelf.HasCond("IsPowered");
            pwrLamp.On = powered;
            if (!powered || fieldStrength <= 0f)
            {
                readyLamp.State = FzLamp.LampState.Off;
                return;
            }

            float damage = 1f - (float)COSelf.GetDamageState();
            readyLamp.FlickerRate = Mathf.InverseLerp(FlickerStartDamage, FlickerFullDamage, damage);
            readyLamp.State = damage >= FlickerStartDamage ? FzLamp.LampState.Flicker : FzLamp.LampState.On;
        }

        private void SetPower(bool on)
        {
            if (COSelf == null) return;
            COSelf.ZeroCondAmount("IsOverrideOff");
            COSelf.ZeroCondAmount("IsOverrideOn");
            COSelf.AddCondAmount(on ? "IsOverrideOn" : "IsOverrideOff", 1.0);
        }

    }
}
