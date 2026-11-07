using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    //Names and defaults for SMES units (superconducting magnetic energy storage) and the reactor taps that charge them
    public static class SmesConstants
    {
        public const double SecondsPerHour = 3600.0;

        public const string UnitCond = "IsFzSmes";
        public const string UnitGUIPropMap = "FzSmesGUI";
        public const string ChargeCond = "StatFzSmesCharge";
        public const string ChargeMaxCond = "StatFzSmesChargeMax";
        public const string ChargeRateCond = "StatFzSmesChargeRate";
        public const string GridChargeProp = "bGridCharge";
        public const string UnitFittedTrigger = "TIsFzSmesFitted";
        public const string UnitCO = "ItmFzSmes01"; //The installed, working unit, the only one whose look follows its state
        public const string LookOff = "ItmFzSmes01Off"; //COOverlays giving the unit's look, the levels go on the end
        public const string LookIdle = "ItmFzSmes01Idle";
        public const string LookGrid = "ItmFzSmes01Grid";
        public const string LookFast = "ItmFzSmes01Fast";
        public const string LookDischarge = "ItmFzSmes01Discharge";
        public const string GlowSuffix = "lit"; //A look's emissive layer is its image with this on the end
        public const int Levels = 6; //Pips in each of the unit's columns
        public const int HalfLevels = 12; //Idle and discharging count in half pips, a part charged pip shows unlit and a full one lit
        public const float FlashRate = 2f; //Flashes a real second of a discharging unit's part charged pip

        public const string TapCond = "IsFzSmesReactorTap";
        public const string TapInstalledTrigger = "TIsFzSmesReactorTapPresent";
        public const string TapActiveTrigger = "TIsFzSmesReactorTapActive";
        public const string TapOrUnitTrigger = "TIsFzSmesReactorTapActiveOrSmes";
        public const string TapChargeRateCond = "StatFzSmesReactorTapChargeRate";
        public const string TapBurnRateCond = "StatFzSmesReactorTapBurnRate";
        public const string TapGUIPropMap = "FzSmesReactorTapGUI";
        public const string TapChargeProp = "bTapCharge";
        public const string TapRateProp = "fTapRate";

        //Used if a unit has no StatFzSmesChargeRate, in kW
        public const double DefaultChargeRate = 200.0;

        //Used if a tap has no StatFzSmesReactorTapChargeRate, in kW
        public const double DefaultTapChargeRate = 100000.0;

        //Used if a tap has no StatFzSmesReactorTapBurnRate, in reactor burn units per kWh
        public const double DefaultTapBurnRate = 0.002;
    }
}
