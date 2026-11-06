using Freznel.FzAdditions.Patches;
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
            CustomPrefabRegistry.Register("FzJumpEnergyCellGUI", FzJumpEnergyCellGUI.CreatePrefab);
            CustomPrefabRegistry.Register("FzJumpReactorTapGUI", FzJumpReactorTapGUI.CreatePrefab);
            CustomPrefabRegistry.Register("FzJumpNavCom", FzJumpNavCom.CreatePrefab);
            CustomPrefabRegistry.Register("FzJumpNavComDmg", FzJumpNavCom.CreateDamagedPrefab);

            JumpConsoleCommands.Register();
            JumpSequence.Init();
        }
    }
}
