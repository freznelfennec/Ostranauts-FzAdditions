using Freznel.FzAdditions.Patches;
using Freznel.FzAdditions.Util;
using Freznel.FzAdditions.Util.AI;

namespace Freznel.FzAdditions
{
    //Missions at temporary corporate sites, hooked into what the common code offers when the plugin loads. So far it
    //has the station defence pilot and a test encounter.
    public static class MissionsSection
    {
        public static void Init()
        {
            FzAI.RegisterPilot(FzStationDefensePilot.TypeName, ship => new FzStationDefensePilot(ship));
            FzSpawns.RegisterKind(TestEncounter.SpawnKind, TestEncounter.RegPrefix, legacyCond: TestEncounter.LegacyCond);
            MissionConsoleCommands.Register();
        }
    }
}
