using Freznel.FzAdditions.Patches;
using Freznel.FzAdditions.UI;
using Freznel.FzAdditions.UI.CustomPrefabs;
using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions
{
    //The jump drive, hooked into what the common code offers when the plugin loads. Its Harmony patches are
    //applied with the rest of the assembly's.
    public static class PropulsionSection
    {
        public static void Init()
        {
            CustomPrefabRegistry.Register("FzJumpFieldEmitterGUI", FzJumpFieldEmitterGUI.CreatePrefab);
            CustomPrefabRegistry.Register("FzJumpNavCom", FzJumpNavCom.CreatePrefab);
            CustomPrefabRegistry.Register("FzJumpNavComDmg", FzJumpNavCom.CreateDamagedPrefab);

            JumpConsoleCommands.Register();
            JumpSequence.Init();

            //The jump drive draws on the ship's SMES units. A series recharges them itself through a time skip, even
            //one it finishes partway through, a jump charging shows them discharging, and switching one on or off
            //cancels what the drive was doing with it.
            Smes.AddRechargeHold(co => GUIFFWD.Active && (JumpSequence.IsSeriesRunning(co.ship) || JumpSequence.IsSkipped(co.ship)));
            Smes.AddDischargeCheck(co => FzJumpEffect.IsCharging(co.ship));
            Smes.PowerSwitched += co => JumpSequence.CancelForDriveChange(co.ship);
        }
    }
}
