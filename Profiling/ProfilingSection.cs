using Freznel.FzAdditions.Patches;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions
{
    //A timing profiler for finding what costs the game time. Loading the plugin only adds its console command, its
    //patches are applied by 'fz profile start' and removed by 'fz profile stop'.
    public static class ProfilingSection
    {
        public static void Init()
        {
            ProfileConsoleCommands.Register();
        }
    }
}
