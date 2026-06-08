using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Patches
{

    [HarmonyPatch(typeof(DataHandler))]
    public static class DataPatch
    {
        [HarmonyPatch(nameof(DataHandler.AllPostLoadAsync))]
        [HarmonyPostfix()]
        public static void AllPostLoadAsync_Postfix()
        {
            FzAdditions.Logger.LogMessage("Starting data patch");
            FzAdditions.Logger.LogMessage($"{DataHandler.dictCOs.Count}");

            if (!DataHandler.dictCOs.TryGetValue("ItmAirPump02Off", out JsonCondOwner TruboPumpInstalledOff))
                FzAdditions.Logger.LogError("Could not find condowner ItmAirPump02Off");

            if (!DataHandler.dictCOs.TryGetValue("ItmAirPump02OnG", out JsonCondOwner TruboPumpInstalledOn))
                FzAdditions.Logger.LogError("Could not find condowner ItmAirPump02OnG");

            if (DataHandler.dictInteractions.TryGetValue("FzTestGUI", out JsonInteraction testGuiInteraction))
            {
                if (TruboPumpInstalledOff != null) TruboPumpInstalledOff.aInteractions = TruboPumpInstalledOff.aInteractions.Append(testGuiInteraction.strName).ToArray();
                if (TruboPumpInstalledOn != null) TruboPumpInstalledOn.aInteractions = TruboPumpInstalledOn.aInteractions.Append(testGuiInteraction.strName).ToArray();
            }
            else
            {
                FzAdditions.Logger.LogError("Cound not find interaction FzTestGUI");
            }

            if (DataHandler.dictGUIPropMapUnparsed.TryGetValue("FzTestGUI", out JsonGUIPropMap map))
            {
                string[] appMap = { "FzTestGUI", "FzTestGUI" };
                if (TruboPumpInstalledOff != null) TruboPumpInstalledOff.mapGUIPropMaps = (appMap).Concat(TruboPumpInstalledOff.mapGUIPropMaps).ToArray();
                if (TruboPumpInstalledOn != null) TruboPumpInstalledOn.mapGUIPropMaps = (appMap).Concat(TruboPumpInstalledOn.mapGUIPropMaps).ToArray();
            }
            else
            {
                FzAdditions.Logger.LogError("Cound not find guipropmap FzTestGUI");
            }

            FzAdditions.Logger.LogMessage("Data patch complete");
        }

    }
}
