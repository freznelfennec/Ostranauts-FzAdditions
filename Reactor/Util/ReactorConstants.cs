using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions.Util
{
    public static class ReactorConstants
    {
        //A blanket's state, on the blanket item and, while fitted, on the reactor. All persisted.
        public const string BlanketTierCond = "StatFzBlanketTier"; //On the reactor, 0 for no blanket
        public const string BlanketConvertedCond = "StatFzBlanketConverted"; //Units of its loaded volatiles converted
        public const string BlanketD2OCond = "StatFzBlanketD2O"; //kg bred and yet to return
        public const string BlanketHe3Cond = "StatFzBlanketHe3";
        public const string BlanketCond = "IsFzBreederBlanket";

        public const string BlanketPrefix = "ItmFzBreederBlanket"; //Plus the tier, 01 to 03
        public const string SpentSuffix = "Spent";
        public const string FitInteraction = "FzBlanketFit";
        public const string RemoveInteraction = "FzBlanketRemove";

        public const double VolatilesPerKg = 5.0; //Loaded volatiles a kg of bred fuel uses
        public const string VolatilesMaterial = "Volatiles";
        public const string ExoticsMaterial = "Exotics";
        public const double WaitingDone = 0.01; //kg, less bred fuel than this waiting is returned at once
    }
}
