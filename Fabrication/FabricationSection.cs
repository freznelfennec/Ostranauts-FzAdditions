using Freznel.FzAdditions.Patches;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions
{
    //Recycling, fabrication and blueprints, hooked into what the common code offers when the plugin loads. Its Harmony
    //patches are applied with the rest of the assembly's.
    public static class FabricationSection
    {
        public static void Init()
        {
            FabConsoleCommands.Register();
        }
    }
}
