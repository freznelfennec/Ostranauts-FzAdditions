using System;
using System.Collections.Generic;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    public static class Constants
    {
        public const double SecondsPerHour = 3600.0;

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
        public const string CellCO = "ItmFzJumpEnergyCell"; //The installed, working cell, the only one whose look follows its state
        public const string CellLookOff = "ItmFzJumpEnergyCellOff"; //COOverlays giving the cell's look, the levels go on the end
        public const string CellLookIdle = "ItmFzJumpEnergyCellIdle";
        public const string CellLookGrid = "ItmFzJumpEnergyCellGrid";
        public const string CellLookFast = "ItmFzJumpEnergyCellFast";
        public const string CellLookDischarge = "ItmFzJumpEnergyCellDischarge";
        public const string CellGlowSuffix = "lit"; //A look's emissive layer is its image with this on the end
        public const int CellLevels = 6; //Pips in each of the cell's columns
        public const int CellHalfLevels = 12; //Idle and discharging count in half pips, a part charged pip shows unlit and a full one lit
        public const float CellFlashRate = 2f; //Flashes a real second of a discharging cell's part charged pip
        public const string TapCond = "IsFzJumpReactorTap";
        public const string TapInstalledTrigger = "TIsFzJumpReactorTapPresent";
        public const string TapActiveTrigger = "TIsFzJumpReactorTapActive";
        public const string TapOrCellTrigger = "TIsFzJumpReactorTapActiveOrCell";
        public const string TapChargeRateCond = "StatFzReactorTapChargeRate";
        public const string TapBurnRateCond = "StatFzReactorTapBurnRate";
        public const string SeriesEnergyCond = "StatFzJumpSeriesEnergy";
        public const string JumpFailureCond = "StatFzJumpFailure";
        public const string SeriesFinishedCond = "IsFzJumpSeriesFinished";
        public const string NavComSettingProp = "fFzJumpEnergy";
        public const string NavComShowPlotProp = "bFzJumpShowPlot";
        public const string NavComSeriesProp = "bFzJumpSeries";
        public const string NavComThrustProp = "bFzJumpThrustTorch";
        public const string NavComLegacyTorchProp = "bFzJumpBrakeTorch"; //What thrust mode was saved as when it only set braking
        public const string SeriesGUIPropMap = "FzJumpSeries"; //On the nav station a series was started from
        public const string MatchGUIPropMap = "FzJumpMatch"; //On the nav station a velocity match was started from
        public const string SeriesKindProp = "strKind";
        public const string SeriesIDProp = "strID";
        public const string SeriesOffsetXProp = "fOffsetX";
        public const string SeriesOffsetYProp = "fOffsetY";
        public const string SeriesTorchProp = "bTorch";
        public const string MatchCrossProp = "bCross"; //With a match's target, only its cross-velocity is taken out
        public const string NavComMatchCrossProp = "bFzJumpMatchCross"; //What the match confirmation was last set to
        public const string BurnTorchProp = "bBurnTorch"; //A correction burn under way, kept with the series
        public const string BurnPhaseProp = "strBurnPhase";
        public const string BurnSpeedProp = "fBurnSpeed";
        public const string BurnFlowProp = "strBurnFlow";
        public const string BurnCoreProp = "fBurnCore";
        public const string JumpChargingCond = "StatFzJumpCharging"; //The energy of a single jump charging, so a load starts it again
        public const string JumpDrainedCond = "StatFzJumpDrained"; //kWh the jump charging has taken from the cells so far, a load doesn't take it twice
        public const string NavComFollowProp = "strFzJumpFollow"; //The target a series setting follows after CALC
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

        //The most full jumps a series can be set to
        public const int MaxSeriesJumps = 100;

        //Share of the torch's sustainable thrust a plot counts on for braking
        public const double JumpTorchBrakingMargin = 0.95;

        //In meters per second, a series corrects its course once it is off by more than this or the angle, whichever is more
        public const double CorrectionMinDeltaV = 0.5;

        //In radians, about half a degree
        public const double CorrectionAngle = 0.0087;

        //In meters per second, a velocity match stops once the velocity against its target is down to this
        public const double MatchTolerance = 0.1;

        //Share of the threshold a correction burn stops within, so it doesn't start again straight away
        public const double CorrectionFinishShare = 0.2;

        //In game seconds, a torch correction is throttled to take about this long so the reactor's updates can't overshoot it
        public const double CorrectionTorchBurnTime = 20.0;

        //The reactor core temperature a torch correction holds, the one it starts at kept within these. The core runs
        //at 0.725 when full and its walls take damage past 0.75, and it goes out if it cools to nothing.
        public const double CorrectionCoreMin = 0.3;
        public const double CorrectionCoreMax = 0.7;

        //In game seconds, how quickly the flow brings the core back to its temperature during a torch correction
        public const double CorrectionCoreSettleTime = 5.0;

        //In radians, how far off its heading the ship can be and still burn the torch
        public const double CorrectionTorchAim = 0.035;

        //In radians per second squared, what RCS turns the ship by at full input, and the turn rate a correction holds to
        public const double CorrectionTurnAcceleration = 1.0;
        public const double CorrectionMaxTurnRate = 0.5;

        //In AU, the game's no wake range around a station, where the torch can't fire
        public const double NoWakeDistance = 2.005376018132665E-06;

        //In meters per second, a series arriving at a no wake zone faster than this stops outside it to brake on the
        //torch, slower and it goes in and brakes on RCS
        public const double SeriesTorchArrivalSpeed = 200.0;

        //In meters, a series within this of where it should stop has arrived
        public const double SeriesArrivalTolerance = 1000.0;

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
