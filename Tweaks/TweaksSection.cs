using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions
{
    //Changes to how the base game runs, for speed, that leave what it does the same. Its Harmony patches are applied
    //with the rest of the assembly's.
    public static class TweaksSection
    {
        public static void Init()
        {
            ShipWear.Init();
        }
    }
}
