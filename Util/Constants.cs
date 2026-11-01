using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    public static class Constants
    {
        public static readonly long DoubleMantissaMask = (1L << 52) - 1L;

        public const string EmitterCond = "IsFzJumpFieldEmitter";
        public const string FieldOffsetMaxCond = "StatFzFieldOffsetMax";
        public const string FieldSegmentMaxCond = "StatFzFieldSegmentMax";
        public const string FieldEnergyMinCond = "StatFzFieldEnergyMin";
        public const string FieldEnergyMaxCond = "StatFzFieldEnergyMax";
        public const string EmitterGUIPropMap = "FzJumpFieldEmitterGUI";
        public const string FieldOffsetProp = "fOffset";
        public const string FieldReinforcementProp = "fReinforcement";
                public const string CellCond = "IsFzJumpEnergyCell";
        public const string CellGUIPropMap = "FzJumpEnergyCellGUI";
public const string CellChargeCond = "StatFzJumpCharge";
        public const string CellChargeMaxCond = "StatFzJumpChargeMax";
        public const string CellChargeRateCond = "StatFzJumpChargeRate";
        public const string CellGridChargeProp = "bGridCharge";
        public const string TapCond = "IsFzJumpReactorTap";
        public const string TapInstalledTrigger = "TIsFzJumpReactorTapPresent";
        public const string TapActiveTrigger = "TIsFzJumpReactorTapActive";
        public const string TapOrCellTrigger = "TIsFzJumpReactorTapActiveOrCell";
        public const string TapChargeRateCond = "StatFzReactorTapChargeRate";
        public const string TapBurnRateCond = "StatFzReactorTapBurnRate";
        public const string SeriesEnergyCond = "StatFzJumpSeriesEnergy";
        public const string FieldFailedCond = "IsFzJumpFieldFailed";
        public const string SeriesFinishedCond = "IsFzJumpSeriesFinished";
        public const string NavComSettingProp = "fFzJumpEnergy";
        public const string NavComShowPlotProp = "bFzJumpShowPlot";
        public const string NavComSeriesProp = "bFzJumpSeries";
        public const string TapGUIPropMap = "FzJumpReactorTapGUI";
        public const string TapChargeProp = "bTapCharge";
        public const string TapRateProp = "fTapRate";
        public const string CellInstalledTrigger = "TIsFzJumpEnergyCellFitted";
        public const float MinFieldOffset = 0f;

        //Used if a cell has no StatFzJumpChargeRate, in kW
        public const double DefaultCellChargeRate = 200.0;

        //Game seconds of jump time for each kWh past the field's need
        public const double JumpSecondsPerKWh = 5.0;

        //Compounding per hour of jump time. About 4% at 14 hours, 35% at 100 hours.
        public const double JumpTimeScalingExponent = 0.003;

        //In meters per second squared, the hardest a plot assumes the ship brakes after a jump. 4g.
        public const double JumpBrakingLimit = 39.24;

        //Share of the torch's sustainable thrust a plot counts on for braking
        public const double JumpTorchBrakingMargin = 0.95;

        //In meters, how far a jump moves the ship with no jump time
        public const double MinJumpDistance = 100.0;

        //In meters, how close a jump can end to its target
        public const double JumpBufferObject = 5000.0;
        public const double JumpBufferPlanet = 300000000.0;

        public const double MetersPerAU = 149597870700.0;

        //Used if a tap has no StatFzReactorTapChargeRate, in kW
        public const double DefaultTapChargeRate = 100000.0;

        //Used if a tap has no StatFzReactorTapBurnRate, in reactor burn units per kWh
        public const double DefaultTapBurnRate = 0.002;

        //Multipliers at full reinforcement, a setting of 1
        public const float ReinforcementLengthBoost = 2f;
        public const float ReinforcementEnergyBoost = 4f;

        //Used if an emitter has no StatFzFieldOffsetMax, such as one placed in an older save
        public const float DefaultMaxFieldOffset = 5f;
    }
}
