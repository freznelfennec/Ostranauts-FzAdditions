using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions
{
    //Changes to how the base game's own features run: for speed, leaving what they do the same, and to how they're
    //used, such as nav console panels. Its Harmony patches are applied with the rest of the assembly's, except nav
    //console panels', which go on only when their setting is on.
    public static class TweaksSection
    {
        public static void Init()
        {
            ShipWear.Init();
            NavPanels.Init(FzAdditions.Settings);
        }
    }
}
