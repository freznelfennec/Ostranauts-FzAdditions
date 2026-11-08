using Freznel.FzAdditions.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Freznel.FzAdditions
{
    //Reactor fuel mechanics, starting with breeder blankets, hooked into what the common code and the Fabrication
    //section offer when the plugin loads. Its Harmony patches are applied with the rest of the assembly's.
    public static class ReactorSection
    {
        public static void Init()
        {
            //Only reactors whose blanket has volatiles left are worth measuring
            ReactorFuel.AddBurnWatcher(BreederBlanket.IsBreeding, (reactor, burnedA, burnedB, seconds) => BreederBlanket.Breed(reactor, burnedA, burnedB));
            ReactorFuel.ReactorRun += (reactor, seconds) =>
            {
                if (BreederBlanket.HasBlanket(reactor)) BreederBlanket.Release(reactor, seconds);
            };

            //A part used blanket recycles for half its unconverted volatiles and exotics, and all its bred fuel
            Recycler.AddYieldShare(BreederBlanket.RecycleShare);
            Recycler.AddReserveSource(BreederBlanket.RecycleReserves);
        }
    }
}
